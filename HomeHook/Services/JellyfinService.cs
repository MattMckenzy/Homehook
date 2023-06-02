using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models.Jellyfin;
using HomeHook.Models.Language;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace HomeHook.Services
{
    public class JellyfinService
    {
        private string DefaultDeviceName { get; set; } = "HomeHook";
        private string DefaultServiceName { get; } = "HomeHook";
        private string DefaultVersionString { get; set; } = "2.0.0";

        private IRestServiceCaller JellyfinCaller { get; }
        private IConfiguration Configuration { get; }

        private Func<string, string, Task<string>> AccessTokenDelegate { get; }


        public JellyfinService(AccessTokenCaller<JellyfinServiceAppProvider> jellyfinCaller, AccessTokenCaller<JellyfinAuthenticationServiceAppProvider> jellyfinAuthCaller, IConfiguration configuration)
        {
            DefaultDeviceName = Dns.GetHostName();
            DefaultVersionString = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? DefaultVersionString;

            JellyfinCaller = jellyfinCaller;
            Configuration = configuration;
            AccessTokenDelegate = async (string credential, string code) =>
            {
                IRestServiceCaller restServiceCaller = jellyfinAuthCaller;
                Dictionary<string, string> headerReplacements = new() { { "$Device", DefaultDeviceName }, { "$Service", DefaultServiceName }, { "$Version", DefaultVersionString } };
                CallResult<string> callResult = await restServiceCaller.PostRequestAsync<string>("Users/AuthenticateByName", content: $"{{ \"Username\": \"{credential}\", \"pw\": \"{code}\" }}", headerReplacements: headerReplacements);

                return callResult.Content != null ? JObject.Parse(callResult.Content).Value<string>("AccessToken") ?? string.Empty : string.Empty;
            };
        }

        public async Task<List<MediaItem>> GetItems(LanguagePhrase phrase, string userId)
        {
            ConcurrentBag<Item> returningItems = new();
            List<Task> recursiveTasks = new();
            
            Dictionary<string, string> headerReplacements = new() { { "$Device", DefaultDeviceName }, { "$Service", DefaultServiceName }, { "$Version", DefaultVersionString } };

            // Search for media matching search terms and add them to returning list.
            recursiveTasks.Add(Task.Run(async () =>
            {
                foreach (Item item in await GetItems(phrase, userId, null, headerReplacements))    
                {
                    returningItems.Add(item);
                }
            }));

            // Search for and retrieve media from folders matching search terms.
            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "Path" }, { "filters", "IsFolder" }, { "searchTerm", phrase.SearchTerm } };
            CallResult<string> foldersCallResult = await JellyfinCaller.GetRequestAsync<string>($"Users/{userId}/Items", phrase.User, AccessTokenDelegate, headerReplacements, queryParameters);

            foreach (Item folder in JsonConvert.DeserializeObject<JellyfinItems>(foldersCallResult.Content ?? string.Empty)?.Items ?? Array.Empty<Item>())
            {
                if (!string.IsNullOrWhiteSpace(phrase.PathTerm) && (!folder.Path?.Contains(phrase.PathTerm, StringComparison.InvariantCultureIgnoreCase) ?? true))
                    continue;

                // Retrieve media for matching parent folder items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    foreach (Item childItem in await GetItems(phrase, userId, folder.Id, headerReplacements))
                        returningItems.Add(childItem);
                }));

                // Retrieve media in all child folders that contain items.
                recursiveTasks.Add(Task.Run(async () =>
                {
                    Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "fields", "Path" }, { "filters", "IsFolder" }, { "parentId", folder.Id } };
                    CallResult<string> childFoldersCallResult = await JellyfinCaller.GetRequestAsync<string>($"Users/{userId}/Items", phrase.User, AccessTokenDelegate, headerReplacements, queryParameters);

                    foreach (Item childFolder in JsonConvert.DeserializeObject<JellyfinItems>(childFoldersCallResult.Content ?? string.Empty)?.Items ?? Array.Empty<Item>())
                    {
                        recursiveTasks.Add(Task.Run(async () =>
                        {
                            foreach (Item childItem in await GetItems(phrase, userId, childFolder.Id, headerReplacements))
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

            return ItemsToMediaQueue(items.Take(Configuration.GetValue<int>("Services:Jellyfin:MaximumQueueSize")), phrase, userId);
        }

        public async Task<string?> GetUserId(string userName)
        {
            Dictionary<string, string> headerReplacements = new() { { "$Device", DefaultDeviceName }, { "$Service", DefaultServiceName }, { "$Version", DefaultVersionString } };

            // Get UserId from username
            CallResult<string> usernameCallResult = await JellyfinCaller.GetRequestAsync<string>("users", userName, AccessTokenDelegate, headerReplacements);
            IEnumerable<User>? users = JsonConvert.DeserializeObject<IEnumerable<User>>(usernameCallResult.Content ?? string.Empty);
            return users?.FirstOrDefault(user => user.Name.Equals(userName, StringComparison.InvariantCultureIgnoreCase))?.Id;
        }

        private async Task<IEnumerable<Item>> GetItems(LanguagePhrase phrase, string userId, string? parentId, Dictionary<string, string> headerReplacements)
        {
            List<Item> returningItems = new();

            Dictionary<string, string> queryParameters = new() { { "recursive", "true" }, { "includeItemTypes","Audio,Video,Movie,Episode,Photo,MusicVideo" }, { "fields", "DateCreated,Path,Height,Width,Overview,SeriesStudio,Studios" }, { "filters", $"IsNotFolder{(phrase.OrderType == OrderType.Continue ? ", IsResumable" : string.Empty)}" } };
            if (!string.IsNullOrWhiteSpace(parentId))
                queryParameters.Add("parentId", parentId);
            else
                queryParameters.Add("searchTerm", phrase.SearchTerm!);

            CallResult<string> callResult = await JellyfinCaller.GetRequestAsync<string>($"Users/{userId}/Items", phrase.User, AccessTokenDelegate, headerReplacements, queryParameters);

            List<Task> recursiveTasks = new();
            foreach (Item item in JsonConvert.DeserializeObject<JellyfinItems>(callResult.Content ?? string.Empty)?.Items ?? Array.Empty<Item>())
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
    
        public async Task UpdateProgress(Progress? progress, string? userName, string? device = null, string? service = null, string? version = null, bool isStopped = false)
        {
            if (progress == null || userName == null)
                return;

            string route;
            if (progress.EventName == null && isStopped)
                route = "Sessions/Playing/Stopped";
            else if (progress.EventName == null)
                route = "Sessions/Playing";
            else
                route = "Sessions/Playing/Progress";

            Dictionary<string, string> headerReplacements = new() { { "$Device", device ?? DefaultDeviceName }, { "$Service", service ?? DefaultServiceName }, { "$Version", version ?? DefaultVersionString } };

            await JellyfinCaller.PostRequestAsync<string>(route, userName, AccessTokenDelegate, headerReplacements, content: JsonConvert.SerializeObject(progress));
        }

        public async Task MarkPlayed(string? userName, string? itemId, string? device = null, string? service = null, string? version = null)
        {
            if (userName == null || itemId == null)
                return;

            string? userId = await GetUserId(userName);

            if (userId == null)
                return;

            string route = $"Users/{userId}/PlayedItems/{itemId}";

            Dictionary<string, string> headerReplacements = new() { { "$Device", device ?? DefaultDeviceName }, { "$Service", service ?? DefaultServiceName }, { "$Version", version ?? DefaultVersionString } };
            Dictionary<string, string> queryParameters = new() { { "datePlayed", DateTime.UtcNow.ToString("s") + "Z" } };

            await JellyfinCaller.PostRequestAsync<string>(route, userName, AccessTokenDelegate, headerReplacements, queryParameters);
        }

        private List<MediaItem> ItemsToMediaQueue(IEnumerable<Item> items, LanguagePhrase phrase, string userId)
        {
            return items.Select((item, index) => 
            {
                MediaItemKind? mediaKind = GetMediaKind(item.MediaType, item.Type);
                if (!mediaKind.HasValue)
                    return null;
                else
                    return new MediaItem
                    {
                        Id = item.Id,
                        Location = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Videos/{item.Id}/stream?static=true&api_key={userId}",
                        MediaSource = phrase.MediaSource,
                        Container = Path.GetExtension(item.Path ?? ".unknown"),
                        Size = item.Size ?? 0,
                        MediaItemKind = (MediaItemKind)mediaKind,
                        Metadata = GetMetadata((MediaItemKind)mediaKind, item, userId),
                        StartTime = (phrase.OrderType == OrderType.Continue || phrase.OrderType == OrderType.Unplayed || phrase.OrderType == OrderType.Watch) && item.UserData?.PlaybackPositionTicks != null ? Convert.ToInt32(Math.Round(Convert.ToDecimal(item.UserData.PlaybackPositionTicks / 10000000), 0, MidpointRounding.ToZero)) : 0,
                        Runtime = (float)(item.RunTimeTicks == null ? 0 : TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds),
                        User = phrase.User,
                        Cache = phrase.PlaybackMethod == PlaybackMethod.Cached
                    };
            }
            ).Where(media => media != null).Cast<MediaItem>().ToList();
        }

        private static MediaItemKind? GetMediaKind(string? mediaType, string? type)
        {
            return mediaType switch
            {
                ("Video") when (type == "Movie") => MediaItemKind.Movie,
                ("Video") when (type == "Episode") => MediaItemKind.SeriesEpisode,
                ("Video") when (type == "MusicVideo") => MediaItemKind.MusicVideo,
                ("Video") => MediaItemKind.Video,
                ("Audio") => MediaItemKind.Song,
                ("Photo") => MediaItemKind.Photo,
                _ => null,
            };
        }

        private MediaMetadata GetMetadata(MediaItemKind mediaKind, Item item, string userId)
        {
            return mediaKind switch
            {
                MediaItemKind.SeriesEpisode => new SeriesEpisodeMetadata
                {
                    Title = item.Name,
                    Subtitle = $"{item.SeriesName}{(item.ParentIndexNumber != null && item.IndexNumber != null ? $" - S{item.ParentIndexNumber}E{item.IndexNumber}" : string.Empty)}",
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    Overview = item.Overview,
                    SeriesTitle = item.SeriesName ?? item.Path,
                    SeriesStudio = item.SeriesStudio,
                    SeasonNumber = item.ParentIndexNumber,
                    EpisodeNumber = item.IndexNumber,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaItemKind.Movie => new MovieMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Studios == null ? null : string.Join(", ", item.Studios.Select(studio => studio.Name)),
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    Overview = item.Overview,
                    Studios = item.Studios == null ? null : string.Join(", ", item.Studios.Select(studio => studio.Name)),
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaItemKind.MusicVideo => new MediaMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Path,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaItemKind.Song => new SongMetadata
                {
                    Title = $"{(item.IndexNumber != null ? $"{item.IndexNumber}. " : string.Empty)}{item.Name}",
                    Subtitle = $"{item.AlbumArtist ?? string.Empty}{(item.Album != null ? $"'s {item.Album}" : string.Empty)}",
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    AlbumName = string.IsNullOrWhiteSpace(item.Album) ? item.Path : item.Album,
                    AlbumArtist = item.AlbumArtist,
                    Artist = item.Artists == null ? null : string.Join(", ", item.Artists),
                    DiscNumber = item.ParentIndexNumber,
                    TrackNumber = item.IndexNumber,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaItemKind.Photo => new PhotoMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Path,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                MediaItemKind.Video => new MediaMetadata
                {
                    Title = item.Name,
                    Subtitle = item.Path,
                    CreationDate = item.PremiereDate ?? item.DateCreated,
                    ThumbnailUri = $"{Configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}"
                },
                _ => throw new NotImplementedException()
            };;
        }
    }
}