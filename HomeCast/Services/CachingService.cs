using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace HomeCast.Services
{
    public partial class CachingService : IDisposable
    {
        #region Injections

        private LoggingService<CachingService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Constructor

        public CachingService(LoggingService<CachingService> loggingService, IConfiguration configuration)
        {
            LoggingService = loggingService;
            Configuration = configuration;

            CacheDirectoryInfo = new(Configuration["Services:Caching:CacheLocation"] ?? Path.Combine("home", "homecast", "cache"));
            if (!CacheDirectoryInfo.Exists)
                CacheDirectoryInfo.Create();

            foreach (FileInfo cacheItemFileInfo in CacheDirectoryInfo.GetFiles())
            {
                IEnumerable<string> fileNameSplit = Path.GetFileNameWithoutExtension(cacheItemFileInfo.Name).Split("-", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                if (fileNameSplit.Count() == 2 && Enum.TryParse(fileNameSplit.Last(), out CacheFormat cacheFormat))
                {
                    CacheItems.Add(Path.GetFileNameWithoutExtension(cacheItemFileInfo.Name),
                        new() { CacheFileInfo = cacheItemFileInfo, CacheFormat = cacheFormat });
                }
            }

            CacheSizeBytes = Configuration.GetValue<long?>("Services:Caching:CacheSizeBytes") ?? 10737418240;

            CacheAlgorithmRatio = Configuration.GetValue<double?>("Services:Caching:CacheAlgorithmRatio") ?? 0.5d;

            YoutubeDL = new YoutubeDL
            {
                YoutubeDLPath = "yt-dlp",
                FFmpegPath = "ffmpeg",
                RestrictFilenames = true
            };

            OptionSet = new()
            {
                NoCacheDir = true,
                RemoveCacheDir = true,
                EmbedSubs = true,
                SubLangs = "all",
                AddHeader = $"User-Agent:{Configuration["Services:Caching:UserAgent"] ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36"}"
            };
        }

        #endregion

        #region Private Properties

        private DirectoryInfo CacheDirectoryInfo { get; }

        private long CacheSizeBytes { get; }
        private double CacheAlgorithmRatio { get; }

        private Dictionary<string, CacheItem> CacheItems { get; } = new();

        private YoutubeDL YoutubeDL { get; } = new();
        private OptionSet OptionSet { get; } = new();

        private bool DisposedValue { get; set; }

        #endregion

        #region CacheService Implementation

        public event EventHandler<CachingFinishedEventArgs>? CachingFinished;

        public async Task<CacheItem?> GetCacheItem(MediaItem mediaItem)
        {
            if (CacheItems.TryGetValue($"{mediaItem.Id}-{mediaItem.CacheFormat}", out CacheItem? cacheItem) && cacheItem != null)
                return cacheItem;
            else if (await TryRunCacheDeletionAlgorithm(mediaItem.Size))
            {
                await DownloadCacheItem(mediaItem);
                return null;
            }
            else
            {
                await LoggingService.LogWarning("Caching Service", $"Could not get enough available space to cache \"{mediaItem.Metadata.Title}\".");
                return null;
            }
        }

        private async Task<bool> TryRunCacheDeletionAlgorithm(long neededBytes)
        {
            DriveInfo driveInfo = new(CacheDirectoryInfo.Root.FullName);
            if (driveInfo.AvailableFreeSpace > neededBytes)
                return true;
            else if (CacheSizeBytes > neededBytes)
            {
                await LoggingService.LogWarning("Caching Service", $"Configured cache size not enough to cache item. Missing {(neededBytes - CacheSizeBytes).GetBytesReadable()}.");
                return false;
            }
            else
            {
                DateTime minimumLastAccessed = CacheItems.Min(cacheItem => cacheItem.Value.CacheFileInfo.LastAccessTime);
                DateTime maximumLastAccessed = CacheItems.Max(cacheItem => cacheItem.Value.CacheFileInfo.LastAccessTime);
                long lastAccessedDifference = (maximumLastAccessed - minimumLastAccessed).Ticks;

                long smallestSize = CacheItems.Min(cacheItem => cacheItem.Value.CacheFileInfo.Length);
                long largestSize = CacheItems.Max(cacheItem => cacheItem.Value.CacheFileInfo.Length);
                long sizeDifference = largestSize - smallestSize;

                Dictionary<CacheItem, int> scoredCacheItems = new();
                foreach (CacheItem cacheItem in CacheItems.Values)
                    scoredCacheItems.Add(cacheItem, Convert.ToInt32(Math.Round(Math.Exp(
                        (((maximumLastAccessed - minimumLastAccessed).Ticks / lastAccessedDifference) * 100 * (1 - CacheAlgorithmRatio)) +
                        (((maximumLastAccessed - minimumLastAccessed).Ticks / lastAccessedDifference) * 100 * CacheAlgorithmRatio)))));

                IEnumerable<CacheItem> orderedCacheItems = scoredCacheItems.OrderByDescending(scoredCacheItem => scoredCacheItem.Value).Select(scoredCacheItem => scoredCacheItem.Key);
                while (driveInfo.AvailableFreeSpace > neededBytes)
                {
                    CacheItem? deletingCacheItem = orderedCacheItems.FirstOrDefault();

                    if (deletingCacheItem == null)
                    {
                        await LoggingService.LogWarning("Caching Service", $"Remaining disk space not enough to cache item. Missing {(neededBytes - driveInfo.AvailableFreeSpace).GetBytesReadable()}.");
                        return false;
                    }
                    else
                        await DeleteCacheItem(deletingCacheItem);
                }

                return true;
            }
        }

        internal void CancelCaching(MediaItem mediaItem)
        {
            if (CacheItems.TryGetValue($"{mediaItem.Id}-{mediaItem.CacheFormat}", out CacheItem? cacheItem) && cacheItem != null)
                cacheItem.CacheCancellationTokenSource.Cancel();
        }

        private async Task DownloadCacheItem(MediaItem mediaItem)
        {
            if (mediaItem.CacheFormat == null)
                return;

            CacheItem newCacheItem = new()
            {
                CacheFileInfo = new FileInfo(Path.Combine(CacheDirectoryInfo.FullName, $"{mediaItem.Id}-{mediaItem.CacheFormat}{(mediaItem.Container.StartsWith(".") ? string.Empty : ".")}{mediaItem.Container}")),
                CacheFormat = (CacheFormat)mediaItem.CacheFormat
            };

            Progress<DownloadProgress> downloadProgress = new(downloadProgress =>
            {
                mediaItem.CachedRatio = downloadProgress.Progress;
            });

            await LoggingService.LogDebug("Caching Service", $"Caching \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");

            _ = Task.Run(async () =>
            {
                try
                {
                    RunResult<string>? runResult = null;

                    OptionSet optionSet = (OptionSet)OptionSet.Clone();
                    optionSet.Output = Path.Combine("yt-dlp", newCacheItem.CacheFileInfo.Name);

                    if (mediaItem.CacheFormat == CacheFormat.Audio)
                        runResult = await YoutubeDL.RunAudioDownload(
                            mediaItem.Location,
                            progress: downloadProgress,
                            ct: newCacheItem.CacheCancellationTokenSource.Token,
                            overrideOptions: optionSet);
                    else if (mediaItem.CacheFormat == CacheFormat.Video)
                        runResult = await YoutubeDL.RunVideoDownload(
                            mediaItem.Location,
                            progress: downloadProgress,
                            ct: newCacheItem.CacheCancellationTokenSource.Token,
                            overrideOptions: optionSet);

                    if (runResult != null && runResult.Success && !string.IsNullOrWhiteSpace(runResult.Data))
                    {
                        File.Move(runResult.Data, newCacheItem.CacheFileInfo.FullName);

                        await LoggingService.LogDebug("Caching Service", $"Succesfully cached \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");

                        mediaItem.CachedRatio = 1;

                        CachingFinished?.Invoke(this,
                            new CachingFinishedEventArgs
                            {
                                CacheItem = newCacheItem,
                                MediaItem = mediaItem
                            });
                    }
                    else
                    {
                        mediaItem.CachedRatio = null;

                        await LoggingService.LogError("Caching Service", $"Failed to cache \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\": {(runResult != null ? string.Join("; ", runResult.ErrorOutput) : "N/A")}.");
                    }
                }
                catch (TaskCanceledException)
                {
                    mediaItem.CachedRatio = null;

                    await LoggingService.LogDebug("Caching Service", $"Canceled caching of \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");
                }
                catch (Exception exception)
                {
                    mediaItem.CachedRatio = null;

                    await LoggingService.LogError("Caching Service", $"Error during caching of \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\": {exception.Message}");
                }
            });

            CacheItems.Add($"{mediaItem.Id}-{mediaItem.CacheFormat}", newCacheItem);
        }

        private async Task DeleteCacheItem(CacheItem cacheItem)
        {
            string key = Path.GetFileNameWithoutExtension(cacheItem.CacheFileInfo.Name);
            await LoggingService.LogDebug("Caching Service", $"Deleting cache item \"{key}\".");

            cacheItem.CacheFileInfo.Delete();
            CacheItems.Remove(key);
        }

        #endregion

        #region IDIsposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    foreach (CacheItem cacheItem in CacheItems.Values)
                        cacheItem.CacheCancellationTokenSource?.Cancel();

                    CacheItems.Clear();
                }

                DisposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}