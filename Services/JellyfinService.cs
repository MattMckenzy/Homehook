using Homehook.Extensions;
using Homehook.Models;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Services
{
    public class JellyfinService
    {
        private readonly IRestServiceCaller _jellyfinCaller;
        private readonly IConfiguration _configuration;

        public JellyfinService(StaticTokenCaller<JellyfinServiceAppProvider> jellyfinCaller, IConfiguration configuration)
        {
            _jellyfinCaller = jellyfinCaller;
            _configuration = configuration;
        }

        public async Task<HomeAssistantMedia> GetItems(JellyPhrase jellyPhrase)    
        {           
            // Get UserId from username
            CallResult<string> usernameCallResult = await _jellyfinCaller.GetRequestAsync<string>("users");
            IEnumerable<UserDto> users = JsonConvert.DeserializeObject<IEnumerable<UserDto>>(usernameCallResult.Content);
            string userId = users.FirstOrDefault(user => user.Name.Equals(jellyPhrase.JellyUser, StringComparison.InvariantCultureIgnoreCase)).Id;

            if (string.IsNullOrWhiteSpace(userId))
                throw new KeyNotFoundException($"Jellyfin User \"{jellyPhrase.JellyUser}\" not Found"); 

            ConcurrentBag<BaseItemDto> returningItems = new();
            List<Task> recursiveTasks = new();
            bool isContinueOrder = jellyPhrase.JellyOrderType == JellyOrderType.Continue;

            // Search for media matching search terms and add them to returning list.
            recursiveTasks.Add(Task.Run(async () =>
            {
                foreach (BaseItemDto item in await GetItems(jellyPhrase.SearchTerm, null, userId, isContinueOrder, jellyPhrase.JellyMediaType))
                    returningItems.Add(item);
            }));

            // Search for and retrieve media from folders matching search terms.
            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "filters", "IsFolder" }, { "searchTerm", jellyPhrase.SearchTerm } };
            CallResult<string> foldersCallResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{userId}/Items", queryParameters);

            foreach (BaseItemDto folder in JsonConvert.DeserializeObject<JellyItemDtos>(foldersCallResult.Content).Items)
            {
                // Retrieve media for matching parent folder items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    foreach (BaseItemDto childItem in await GetItems(null, folder.Id, userId, isContinueOrder, jellyPhrase.JellyMediaType))
                        returningItems.Add(childItem);
                }));

                // Retrieve media in all child folders that contain items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "filters", "IsFolder" }, { "parentId", folder.Id } };
                    CallResult<string> childFoldersCallResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{userId}/Items", queryParameters);

                    foreach (BaseItemDto childFolder in JsonConvert.DeserializeObject<JellyItemDtos>(childFoldersCallResult.Content).Items)
                    {
                        recursiveTasks.Add(Task.Run(async () =>
                        {
                            foreach (BaseItemDto childItem in await GetItems(null, childFolder.Id, userId, isContinueOrder, jellyPhrase.JellyMediaType))
                                returningItems.Add(childItem);
                        }));
                    }
                }));
            }

            // Wait for all folders to complete retrieving items.
            Task.WaitAll(recursiveTasks.ToArray());

            IEnumerable<BaseItemDto> items = returningItems.ToArray();

            // Order retrieved items by wanted order.
            items = jellyPhrase.JellyOrderType switch
            {
                JellyOrderType.Continue => items.OrderByDescending(item => item.UserData.LastPlayedDate),
                JellyOrderType.Shuffle => items.Shuffle(),
                JellyOrderType.Shortest => items.OrderBy(item => item.RunTimeTicks),
                JellyOrderType.Longest => items.OrderByDescending(item => item.RunTimeTicks),
                JellyOrderType.Oldest => items.OrderBy(item => item.DateCreated),
                _ => items.OrderByDescending(item => item.DateCreated),
            };

            return new HomeAssistantMedia
            {
                items = items.Take(_configuration.GetValue<int>("Services:Jellyfin:MaximumQueueSize")).Select((item, index) => new HomeAssistantMediaItem
                {
                    entity_id = $"media_player.{jellyPhrase.JellyDevice}",
                    media_content_type = item.MediaType,
                    media_content_id = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Videos/{item.Id}/stream?Static=true&api_key={userId}",
                    extra = new HomeAssistantExtra
                    {
                        enqueue = index != 0 ? true : null,
                        metadata = new HomeAssistantMedadata
                        {
                            title = item.Name,
                            images = new HomeAssistantImages[]
                            {
                                new HomeAssistantImages
                                {
                                    url = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                                }
                            },
                            metadataType = GetMetadataTypeId(item.MediaType),
                            subtitle = item.Overview,
                            seriesTitle = item.MediaType.Equals("Video", StringComparison.InvariantCultureIgnoreCase) ? item.SeriesName : null,
                            season = item.MediaType.Equals("Video", StringComparison.InvariantCultureIgnoreCase) ? item.ParentIndexNumber : null,
                            episode = item.MediaType.Equals("Video", StringComparison.InvariantCultureIgnoreCase) ? item.IndexNumber : null,
                            originalAirDate = item.MediaType.Equals("Video", StringComparison.InvariantCultureIgnoreCase) ? item.PremiereDate : null,
                            albumName = item.MediaType.Equals("Audio", StringComparison.InvariantCultureIgnoreCase) ? item.Album : null,
                            albumArtist = item.MediaType.Equals("Audio", StringComparison.InvariantCultureIgnoreCase) ? item.AlbumArtist : null,
                            trackNumber = item.MediaType.Equals("Audio", StringComparison.InvariantCultureIgnoreCase) ? item.IndexNumber : null,
                            discNumber = item.MediaType.Equals("Audio", StringComparison.InvariantCultureIgnoreCase) ? item.ParentIndexNumber : null,
                            releaseDate = item.MediaType.Equals("Audio", StringComparison.InvariantCultureIgnoreCase) ? (item.ProductionYear != null ? (DateTime?)new DateTime((int)item.ProductionYear, 12, 31) : null) : null,
                            creationDateTime = item.MediaType.Equals("Photo", StringComparison.InvariantCultureIgnoreCase) ? item.DateCreated : null
                        }
                    }
                })
            };
        }

        private static int GetMetadataTypeId(string mediaType)
        {
            switch (mediaType)
            {
                case "Video": return 2;
                case "Audio": return 3;
                case "Photo": return 4;
                default: return 0;
            }
        }

        private async Task<IEnumerable<BaseItemDto>> GetItems(string searchTerm, string parentId, string userId, bool isContinueOrder, JellyMediaType jellyMediaType)
        {
            List<BaseItemDto> returningItems = new();

            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "DateCreated" }, { "filters", $"IsNotFolder{(isContinueOrder ? ", IsResumable" : string.Empty)}" } };
            if (!string.IsNullOrWhiteSpace(searchTerm))
                queryParameters.Add("searchTerm", searchTerm);
            if (!string.IsNullOrWhiteSpace(parentId))
                queryParameters.Add("parentId", parentId);

            CallResult<string> callResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{userId}/Items", queryParameters);

            List<Task> recursiveTasks = new();
            foreach (BaseItemDto item in JsonConvert.DeserializeObject<JellyItemDtos>(callResult.Content).Items)
            {
                if (jellyMediaType == JellyMediaType.All || jellyMediaType.ToString().Equals(item.MediaType, StringComparison.InvariantCultureIgnoreCase))
                    returningItems.Add(item);                
            }

            Task.WaitAll(recursiveTasks.ToArray());
            return returningItems;
        }
    }
}