using GoogleCast.Models;
using GoogleCast.Models.Media;
using Homehook.Models;
using Homehook.Models.Jellyfin;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        private readonly Func<string, string, Task<string>> _accessTokenDelegate;


        public JellyfinService(AccessTokenCaller<JellyfinServiceAppProvider> jellyfinCaller, AccessTokenCaller<JellyfinAuthenticationServiceAppProvider> jellyfinAuthCaller, IConfiguration configuration)
        {
            _jellyfinCaller = jellyfinCaller;
            _configuration = configuration;
            _accessTokenDelegate = async (string credential, string code) =>
            {
                IRestServiceCaller restServiceCaller = jellyfinAuthCaller;
                Dictionary<string, string> headerReplacements = new() { { "$Device", "Homehook" }, { "$DeviceId", "Homehook" } };
                CallResult<string> callResult = await restServiceCaller.PostRequestAsync<string>("Users/AuthenticateByName", content: $"{{ \"Username\": \"{credential}\", \"pw\": \"{code}\" }}", headerReplacements: headerReplacements);
                return JObject.Parse(callResult.Content).Value<string>("AccessToken");
            };
        }

        public async Task<IEnumerable<QueueItem>> GetItems(JellyPhrase phrase, string device = "Homehook", string deviceId = "Homehook")
        {
            ConcurrentBag<Item> returningItems = new();
            List<Task> recursiveTasks = new();
            
            Dictionary<string, string> headerReplacements = new() { { "$Device", device }, { "$DeviceId", deviceId } };

            // Search for media matching search terms and add them to returning list.
            recursiveTasks.Add(Task.Run(async () =>
            {
                foreach (Item item in await GetItems(phrase, null, headerReplacements))    
                {
                    returningItems.Add(item);
                }
            }));

            // Search for and retrieve media from folders matching search terms.
            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "Path" }, { "filters", "IsFolder" }, { "searchTerm", phrase.SearchTerm } };
            CallResult<string> foldersCallResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

            foreach (Item folder in JsonConvert.DeserializeObject<JellyfinItems>(foldersCallResult.Content).Items)
            {
                if (!string.IsNullOrWhiteSpace(phrase.PathTerm) && (!folder.Path?.Contains(phrase.PathTerm, StringComparison.InvariantCultureIgnoreCase) ?? true))
                    continue;

                // Retrieve media for matching parent folder items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    foreach (Item childItem in await GetItems(phrase, folder.Id, headerReplacements))
                        returningItems.Add(childItem);
                }));

                // Retrieve media in all child folders that contain items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "Path" }, { "filters", "IsFolder" }, { "parentId", folder.Id } };
                    CallResult<string> childFoldersCallResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

                    foreach (Item childFolder in JsonConvert.DeserializeObject<JellyfinItems>(childFoldersCallResult.Content).Items)
                    {
                        recursiveTasks.Add(Task.Run(async () =>
                        {
                            foreach (Item childItem in await GetItems(phrase, childFolder.Id, headerReplacements))
                                returningItems.Add(childItem);
                        }));
                    }
                }));
            }

            // Wait for all folders to complete retrieving items.
            Task.WaitAll(recursiveTasks.ToArray());

            IEnumerable<Item> items = returningItems.ToArray();

            // Order retrieved items by wanted order.
            items = phrase.OrderType switch
            {
                OrderType.Watch => items.OrderBy(item => GetItemDate(item)),
                OrderType.Played => items.OrderByDescending(item => GetItemDate(item)),
                OrderType.Unplayed => items.OrderByDescending(item => GetItemDate(item)),
                OrderType.Continue => items.OrderByDescending(item => item.UserData.LastPlayedDate),
                OrderType.Shuffle => items.OrderBy(_ => Guid.NewGuid()),
                OrderType.Ordered => items.OrderBy(item => item.AlbumArtist).ThenBy(item => item.Album).ThenBy(item => item.SeriesName).ThenBy(item => item.ParentIndexNumber).ThenBy(item => item.IndexNumber),
                OrderType.Shortest => items.OrderBy(item => item.RunTimeTicks),
                OrderType.Longest => items.OrderByDescending(item => item.RunTimeTicks),
                OrderType.Oldest => items.OrderBy(item => GetItemDate(item)),
                _ => items.OrderByDescending(item => GetItemDate(item)),
            };

            return ItemsToQueueItems(items.Take(_configuration.GetValue<int>("Services:Jellyfin:MaximumQueueSize")), phrase);
        }

        private static DateTime? GetItemDate(Item item)
        {
            return item.MediaType switch
            {
                "Video" => item.PremiereDate == null ? item.DateCreated : item.PremiereDate,
                "Audio" => item.ProductionYear != null ? new DateTime((int)item.ProductionYear, 12, 31) : null,
                "Photo" => item.DateCreated,                
                _ => item.PremiereDate == null ? item.DateCreated : item.PremiereDate
            };
        }

        public async Task<string> GetUserId(string userName, string device = "Homehook", string deviceId = "Homehook")
        {
            Dictionary<string, string> headerReplacements = new() { { "$Device", device }, { "$DeviceId", deviceId } };

            // Get UserId from username
            CallResult<string> usernameCallResult = await _jellyfinCaller.GetRequestAsync<string>("users", credential: userName, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);
            IEnumerable<User> users = JsonConvert.DeserializeObject<IEnumerable<User>>(usernameCallResult.Content);
            string userId = users.FirstOrDefault(user => user.Name.Equals(userName, StringComparison.InvariantCultureIgnoreCase)).Id;

            if (string.IsNullOrWhiteSpace(userId))
                throw new KeyNotFoundException($"Jellyfin User \"{userName}\" not Found");
            return userId;
        }

        private async Task<IEnumerable<Item>> GetItems(JellyPhrase phrase, string parentId, Dictionary<string, string> headerReplacements)
        {
            List<Item> returningItems = new();

            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "DateCreated,Path" }, { "filters", $"IsNotFolder{(phrase.OrderType == OrderType.Continue ? ", IsResumable" : string.Empty)}" } };
            if (!string.IsNullOrWhiteSpace(parentId))
                queryParameters.Add("parentId", parentId);
            else
                queryParameters.Add("searchTerm", phrase.SearchTerm);

            CallResult<string> callResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

            List<Task> recursiveTasks = new();
            foreach (Item item in JsonConvert.DeserializeObject<JellyfinItems>(callResult.Content).Items)
            {
                if (!string.IsNullOrWhiteSpace(phrase.PathTerm) && (!item.Path?.Contains(phrase.PathTerm, StringComparison.InvariantCultureIgnoreCase) ?? true))
                    continue;

                if ((phrase.MediaType == MediaType.All || phrase.MediaType.ToString().Equals(item.MediaType, StringComparison.InvariantCultureIgnoreCase)))
                {
                    switch (phrase.OrderType)
                    {
                        case OrderType.Played:
                            if (item.UserData.Played != null && (bool)item.UserData.Played)
                                returningItems.Add(item);
                            break;

                        case OrderType.Unplayed:
                        case OrderType.Watch:
                            if (item.UserData.Played != null && !(bool)item.UserData.Played)
                                returningItems.Add(item);
                            break;

                        default:
                            returningItems.Add(item);
                            break;
                    }
                }             
            }

            Task.WaitAll(recursiveTasks.ToArray());
            return returningItems;
        }
    
        public async Task UpdateProgress(Progress progress, string userName, string device, string deviceId, bool isStopped = false)
        {
            string route;
            if (progress.EventName == null && isStopped)
                route = "Sessions/Playing/Stopped";
            else if (progress.EventName == null)
                route = "Sessions/Playing";
            else
                route = "Sessions/Playing/Progress";

            Dictionary<string, string> headerReplacements = new() { { "$Device", device }, { "$DeviceId", deviceId } };

            await _jellyfinCaller.PostRequestAsync<string>(route, content: JsonConvert.SerializeObject(progress), credential: userName, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);
        }

        private IEnumerable<QueueItem> ItemsToQueueItems(IEnumerable<Item> items, JellyPhrase phrase)
        {
            return items.Select((item, index) => new QueueItem
            {
                Autoplay = true,
                StartTime = (phrase.OrderType == OrderType.Continue || phrase.OrderType == OrderType.Unplayed || phrase.OrderType == OrderType.Watch) && item.UserData.PlaybackPositionTicks != null ? Convert.ToInt32(Math.Round(Convert.ToDecimal(item.UserData.PlaybackPositionTicks / 10000000), 0, MidpointRounding.ToZero)) : 0,
                Media = new()
                {
                    ContentType = item.MediaType,
                    ContentId = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Videos/{item.Id}/stream?static=true&api_key={phrase.UserId}",
                    Duration = item.RunTimeTicks == null ? null : TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds,
                    StreamType = StreamType.Buffered,
                    Metadata = GetMetadata(item, phrase.UserId),
                    CustomData = new Dictionary<string, string>() { { "Id", item.Id }, { "Username", phrase.User } }
                },
            });
        }

        private MediaMetadata GetMetadata(Item item, string userId)
        {
            return item.MediaType switch
            {
                "Video" => new MediaMetadata
                {
                    MetadataType = MetadataType.TvShow,
                    Title = item.Name,
                    SeriesTitle = string.IsNullOrWhiteSpace(item.SeriesName) ? item.Path : item.SeriesName,
                    Season = item.ParentIndexNumber,
                    Episode = item.IndexNumber,
                    OriginalAirDate = GetItemDate(item).ToString(),
                    Images = new Image[] { new Image { Url = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}" } },
                },
                "Audio" => new MediaMetadata
                {
                    MetadataType = MetadataType.Music,
                    Title = item.Name,
                    AlbumName = string.IsNullOrWhiteSpace(item.Album) ? item.Path : item.Album,
                    AlbumArtist = item.AlbumArtist,
                    Artist = item.Artists == null ? null : string.Join(", ", item.Artists),
                    DiscNumber = item.ParentIndexNumber,
                    TrackNumber = item.IndexNumber,
                    ReleaseDate = GetItemDate(item).ToString(),
                    Images = new Image[] { new Image { Url = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}" } },
                },
                "Photo" => new MediaMetadata
                {
                    MetadataType = MetadataType.Photo,
                    Title = $"{item.Name} ({item.Path})",
                    CreationDateTime = GetItemDate(item).ToString(),
                },
                _ => new MediaMetadata
                {
                    MetadataType = MetadataType.Default,
                    Title = item.Name,
                    Subtitle = string.IsNullOrWhiteSpace(item.Overview) ? item.Path : item.Overview,
                    ReleaseDate = GetItemDate(item).ToString(),
                    Images = new Image[] { new Image { Url = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}" } },
                }
            };
        }
    }
}