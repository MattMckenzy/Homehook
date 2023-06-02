using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models.Language;
using System.Globalization;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace HomeHook.Services
{
    public class SearchService
    {
        private IConfiguration Configuration { get; }
        private LoggingService<SearchService> LoggingService { get; }

        public SearchService(IConfiguration configuration, LoggingService<SearchService> loggingService)
        {
            Configuration = configuration;
            LoggingService = loggingService;

            YoutubeDL = new YoutubeDL
            {
                YoutubeDLPath = "yt-dlp"
            };

            OptionSet = new()
            {
                AddHeader = $"User-Agent:{Configuration["Services:Search:UserAgent"] ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36"}"
            };
        }

        private YoutubeDL YoutubeDL { get; } = new();
        private OptionSet OptionSet { get; } = new();

        public async IAsyncEnumerable<MediaItem> Search(LanguagePhrase phrase)
        {
            int maxSearchResultCount = Configuration.GetValue<int?>("Services:Search:MaxSearchResultCount") ?? 10;
            await foreach(SearchResult searchResult in YoutubeDL.Search(phrase.SearchTerm, maxSearchResultCount, phrase.OrderType == OrderType.Newest, OptionSet))
            {
                if (searchResult.Id == null)
                {
                    await LoggingService.LogWarning("Search Parse Failure", "Could not parse ID from search result.", searchResult);
                    continue;
                }
                else if (searchResult.LocationUrl == null)
                {
                    await LoggingService.LogWarning("Search Parse Failure", "Could not parse location from search result.", searchResult);
                    continue;
                }
                else if (searchResult.Container == null)
                {
                    await LoggingService.LogWarning("Search Parse Failure", "Could not parse container from search result.", searchResult);
                    continue;
                }
                else if (searchResult.Title == null)
                {
                    await LoggingService.LogWarning("Search Parse Failure", "Could not parse title from search result.", searchResult);
                    continue;
                }
                else if (searchResult.Runtime == null)
                {
                    await LoggingService.LogWarning("Search Parse Failure", "Could not parse runtime from search result.", searchResult);
                    continue;
                }

                List<string> subtitleStrings = new();
                if (searchResult.Channel != null)
                    subtitleStrings.Add(searchResult.Channel);
                if (searchResult.LikeCount != null)
                    subtitleStrings.Add($"{(long)searchResult.LikeCount:N0}👍");

                yield return new MediaItem
                {
                    Id = searchResult.Id,
                    Location = searchResult.LocationUrl,
                    MediaSource = phrase.MediaSource,
                    Container = searchResult.Container,
                    Runtime = (double)searchResult.Runtime,
                    MediaItemKind = MediaItemKind.Video,
                    Size = searchResult.FileSize ?? 0,
                    Cache = phrase.PlaybackMethod == PlaybackMethod.Cached,
                    User = phrase.User,
                    StartTime = 0,
                    Metadata = new MediaMetadata
                    {
                        Title = searchResult.Title,
                        ThumbnailUri = searchResult.ThumbnailUrl,
                        Subtitle = string.Join(" - ", subtitleStrings),
                        CreationDate = searchResult.CreationDate,
                    }
                };
            }
        }
    }
}
