using Homehook.Extensions;
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

        public async Task<IEnumerable<Item>> GetItems(Phrase phrase, string device = "Homehook", string deviceId = "Homehook")
        {
            ConcurrentBag<Item> returningItems = new();
            List<Task> recursiveTasks = new();
            bool isContinueOrder = phrase.OrderType == OrderType.Continue;
            
            Dictionary<string, string> headerReplacements = new() { { "$Device", device }, { "$DeviceId", deviceId } };

            // Search for media matching search terms and add them to returning list.
            recursiveTasks.Add(Task.Run(async () =>
            {
                foreach (Item item in await GetItems(phrase, null, isContinueOrder, headerReplacements))
                    returningItems.Add(item);
            }));

            // Search for and retrieve media from folders matching search terms.
            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "filters", "IsFolder" }, { "searchTerm", phrase.SearchTerm } };
            CallResult<string> foldersCallResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

            foreach (Item folder in JsonConvert.DeserializeObject<JellyfinItems>(foldersCallResult.Content).Items)
            {
                // Retrieve media for matching parent folder items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    foreach (Item childItem in await GetItems(phrase, folder.Id, isContinueOrder, headerReplacements))
                        returningItems.Add(childItem);
                }));

                // Retrieve media in all child folders that contain items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "filters", "IsFolder" }, { "parentId", folder.Id } };
                    CallResult<string> childFoldersCallResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

                    foreach (Item childFolder in JsonConvert.DeserializeObject<JellyfinItems>(childFoldersCallResult.Content).Items)
                    {
                        recursiveTasks.Add(Task.Run(async () =>
                        {
                            foreach (Item childItem in await GetItems(phrase, childFolder.Id, isContinueOrder, headerReplacements))
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
                OrderType.Continue => items.OrderByDescending(item => item.UserData.LastPlayedDate),
                OrderType.Shuffle => items.Shuffle(),
                OrderType.Ordered => items.OrderBy(item => item.AlbumArtist).ThenBy(item => item.Album).ThenBy(item => item.SeriesName).ThenBy(item => item.ParentIndexNumber).ThenBy(item => item.IndexNumber),
                OrderType.Shortest => items.OrderBy(item => item.RunTimeTicks),
                OrderType.Longest => items.OrderByDescending(item => item.RunTimeTicks),
                OrderType.Oldest => items.OrderBy(item => item.DateCreated),
                _ => items.OrderByDescending(item => item.DateCreated),
            };

            return items.Take(_configuration.GetValue<int>("Services:Jellyfin:MaximumQueueSize"));
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

        private async Task<IEnumerable<Item>> GetItems(Phrase phrase, string parentId, bool isContinueOrder, Dictionary<string, string> headerReplacements)
        {
            List<Item> returningItems = new();

            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "DateCreated,Path" }, { "filters", $"IsNotFolder{(isContinueOrder ? ", IsResumable" : string.Empty)}" } };
            if (!string.IsNullOrWhiteSpace(parentId))
                queryParameters.Add("parentId", parentId);
            else
                queryParameters.Add("searchTerm", phrase.SearchTerm);

            CallResult<string> callResult = await _jellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

            List<Task> recursiveTasks = new();
            foreach (Item item in JsonConvert.DeserializeObject<JellyfinItems>(callResult.Content).Items)
            {
                if (phrase.MediaType == MediaType.All || phrase.MediaType.ToString().Equals(item.MediaType, StringComparison.InvariantCultureIgnoreCase))
                    returningItems.Add(item);                
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
    }
}