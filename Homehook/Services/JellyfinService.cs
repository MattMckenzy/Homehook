using Homehook.Models;
using Homehook.Models.Jellyfin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using WonkCast.Common.Models;

namespace Homehook.Services
{
    public class JellyfinService
    {
        private readonly IRestServiceCaller JellyfinCaller;
        private readonly IConfiguration Configuration;

        private readonly Func<string, string, Task<string>> _accessTokenDelegate;


        public JellyfinService(AccessTokenCaller<JellyfinServiceAppProvider> jellyfinCaller, AccessTokenCaller<JellyfinAuthenticationServiceAppProvider> jellyfinAuthCaller, IConfiguration configuration)
        {
            JellyfinCaller = jellyfinCaller;
            Configuration = configuration;
            _accessTokenDelegate = async (string credential, string code) =>
            {
                IRestServiceCaller restServiceCaller = jellyfinAuthCaller;
                Dictionary<string, string> headerReplacements = new() { { "$Device", "Homehook" }, { "$DeviceId", "Homehook" } };
                CallResult<string> callResult = await restServiceCaller.PostRequestAsync<string>("Users/AuthenticateByName", content: $"{{ \"Username\": \"{credential}\", \"pw\": \"{code}\" }}", headerReplacements: headerReplacements);
                
                return JObject.Parse(callResult.Content).Value<string>("AccessToken") ?? string.Empty;
            };
        }

        public async Task<List<Media>> GetItems(JellyPhrase phrase, string device = "Homehook", string deviceId = "Homehook")
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
            CallResult<string> foldersCallResult = await JellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

            foreach (Item folder in JsonConvert.DeserializeObject<JellyfinItems>(foldersCallResult.Content)?.Items ?? Array.Empty<Item>())
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
                    CallResult<string> childFoldersCallResult = await JellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

                    foreach (Item childFolder in JsonConvert.DeserializeObject<JellyfinItems>(childFoldersCallResult.Content)?.Items ?? Array.Empty<Item>())
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
                OrderType.Watch => items.OrderBy(item => item.DateCreated),
                OrderType.Played => items.OrderByDescending(item => item.DateCreated),
                OrderType.Unplayed => items.OrderByDescending(item => item.DateCreated),
                OrderType.Continue => items.OrderByDescending(item => item.UserData?.LastPlayedDate ?? DateTime.MinValue),
                OrderType.Shuffle => items.OrderBy(_ => Guid.NewGuid()),
                OrderType.Ordered => items.OrderBy(item => item.AlbumArtist).ThenBy(item => item.Album).ThenBy(item => item.SeriesName).ThenBy(item => item.ParentIndexNumber).ThenBy(item => item.IndexNumber),
                OrderType.Shortest => items.OrderBy(item => item.RunTimeTicks),
                OrderType.Longest => items.OrderByDescending(item => item.RunTimeTicks),
                OrderType.Oldest => items.OrderBy(item => item.DateCreated),
                _ => items.OrderByDescending(item => item.DateCreated),
            };

            return ItemsToMediaQueue(items.Take(Configuration.GetValue<int>("Services:Jellyfin:MaximumQueueSize")), phrase);
        }

        public async Task<string> GetUserId(string userName, string device = "Homehook", string deviceId = "Homehook")
        {
            Dictionary<string, string> headerReplacements = new() { { "$Device", device }, { "$DeviceId", deviceId } };

            // Get UserId from username
            CallResult<string> usernameCallResult = await JellyfinCaller.GetRequestAsync<string>("users", credential: userName, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);
            IEnumerable<User>? users = JsonConvert.DeserializeObject<IEnumerable<User>>(usernameCallResult.Content);
            string? userId = users?.FirstOrDefault(user => user.Name.Equals(userName, StringComparison.InvariantCultureIgnoreCase))?.Id;

            if (string.IsNullOrWhiteSpace(userId))
                throw new KeyNotFoundException($"Jellyfin User \"{userName}\" not Found");
            return userId;
        }

        private async Task<IEnumerable<Item>> GetItems(JellyPhrase phrase, string? parentId, Dictionary<string, string> headerReplacements)
        {
            List<Item> returningItems = new();

            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "includeItemTypes","Audio,Video,Movie,Episode,Photo" }, { "fields", "DateCreated,Path,Height,Width,Overview,SeriesStudio,Studios" }, { "filters", $"IsNotFolder{(phrase.OrderType == OrderType.Continue ? ", IsResumable" : string.Empty)}" } };
            if (!string.IsNullOrWhiteSpace(parentId))
                queryParameters.Add("parentId", parentId);
            else
                queryParameters.Add("searchTerm", phrase.SearchTerm);

            CallResult<string> callResult = await JellyfinCaller.GetRequestAsync<string>($"Users/{phrase.UserId}/Items", queryParameters, credential: phrase.User, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);

            List<Task> recursiveTasks = new();
            foreach (Item item in JsonConvert.DeserializeObject<JellyfinItems>(callResult.Content)?.Items ?? Array.Empty<Item>())
            {
                if (!string.IsNullOrWhiteSpace(phrase.PathTerm) && (!item.Path?.Contains(phrase.PathTerm, StringComparison.InvariantCultureIgnoreCase) ?? true))
                    continue;

                if ((phrase.MediaType == MediaType.All || phrase.MediaType.ToString().Equals(item.MediaType, StringComparison.InvariantCultureIgnoreCase)))
                {
                    switch (phrase.OrderType)
                    {
                        case OrderType.Played:
                            if (item.UserData?.Played != null && (bool)item.UserData.Played)
                                returningItems.Add(item);
                            break;

                        case OrderType.Unplayed:
                        case OrderType.Watch:
                            if (item.UserData?.Played != null && !(bool)item.UserData.Played)
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

            await JellyfinCaller.PostRequestAsync<string>(route, content: JsonConvert.SerializeObject(progress), credential: userName, headerReplacements: headerReplacements, accessTokenDelegate: _accessTokenDelegate);
        }

        private List<Media> ItemsToMediaQueue(IEnumerable<Item> items, JellyPhrase phrase)
        {
            return items.Select((item, index) => 
            {
                MediaKind? mediaKind = GetMediaKind(item.MediaType, item.Type);
                if (!mediaKind.HasValue)
                    return null;
                else
                    return new Media
                    {
                        Id = item.Id,
                        Location = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Videos/{item.Id}/stream?static=true&api_key={phrase.UserId}",
                        MediaKind = (MediaKind)mediaKind,
                        Metadata = GetMetadata((MediaKind)mediaKind, item, phrase.UserId),
                        StartTime = (phrase.OrderType == OrderType.Continue || phrase.OrderType == OrderType.Unplayed || phrase.OrderType == OrderType.Watch) && item.UserData?.PlaybackPositionTicks != null ? Convert.ToInt32(Math.Round(Convert.ToDecimal(item.UserData.PlaybackPositionTicks / 10000000), 0, MidpointRounding.ToZero)) : 0,
                        Runtime = item.RunTimeTicks == null ? 0 : TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds  ,                      
                        Cache = true
                    };
            }
            ).Where(media => media != null).Cast<Media>().ToList();
        }

        private static MediaKind? GetMediaKind(string? mediaType, string? type)
        {
            return mediaType switch
            {
                ("Video") when (type == "Movie") => MediaKind.Movie,
                ("Video") when (type == "Episode") => MediaKind.SeriesEpisode,
                ("Video") => MediaKind.Video,
                ("Audio") => MediaKind.Song,
                ("Photo") => MediaKind.Photo,
                _ => null,
            };
        }

        private MediaMetadata GetMetadata(MediaKind mediaKind, Item item, string userId)
        {
            return mediaKind switch
            {
                MediaKind.SeriesEpisode => new SeriesEpisodeMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Path,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    Overview = item.Overview,
                    SeriesTitle = item.SeriesName ?? item.Path,
                    SeriesStudio = item.SeriesStudio,
                    SeasonNumber = item.ParentIndexNumber,
                    EpisodeNumber = item.IndexNumber,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaKind.Movie => new MovieMetadata
                {
                    Title = item.Name,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    Overview = item.Overview,
                    Studios = item.Studios == null ? null : string.Join(", ", item.Studios.Select(studio => studio.Name)),
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaKind.Song => new SongMetadata
                {
                    Title = item.Name,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    AlbumName = string.IsNullOrWhiteSpace(item.Album) ? item.Path : item.Album,
                    AlbumArtist = item.AlbumArtist,
                    Artist = item.Artists == null ? null : string.Join(", ", item.Artists),
                    DiscNumber = item.ParentIndexNumber,
                    TrackNumber = item.IndexNumber,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaKind.Photo => new PhotoMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Path,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaKind.Video => new MediaMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Path,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                _ => throw new NotImplementedException()
            };
        }
    }
}