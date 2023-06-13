using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using System.Collections.Concurrent;
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

                if (fileNameSplit.Count() == 2 && Enum.TryParse(fileNameSplit.Last(), out CacheFormat _))
                {
                    string cacheKey = Path.GetFileNameWithoutExtension(cacheItemFileInfo.Name);
                    CacheFileInfos.Add(cacheKey, cacheItemFileInfo);
                }
            }

            CacheSizeBytes = Configuration.GetValue<long?>("Services:Caching:CacheSizeBytes") ?? 10737418240;

            CacheAlgorithmRatio = Configuration.GetValue<double?>("Services:Caching:CacheAlgorithmRatio") ?? 0.5d;

            CacheFormat = Configuration.GetValue<bool>("Services:Player:VideoCapable") ? CacheFormat.Video : CacheFormat.Audio;

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
        private CacheFormat CacheFormat { get; }

        private Dictionary<string, FileInfo> CacheFileInfos { get; } = new();
        private ConcurrentQueue<(FileInfo CacheFileInfo, MediaItem MediaItem)> CachingQueue { get; } = new();
        private bool IsProcessingCacheQueue = false;
        private CancellationTokenSource CachingCancellationTokenSource = new();

        private YoutubeDL YoutubeDL { get; } = new();
        private OptionSet OptionSet { get; } = new();

        private bool DisposedValue { get; set; }

        #endregion

        #region CacheService Implementation

        public event EventHandler<CachingUpdateEventArgs>? CachingUpdate;

        public async Task<(bool, FileInfo?)> TryGetOrQueueCacheItem(MediaItem mediaItem)
        {
            if (CacheFileInfos.TryGetValue($"{mediaItem.MediaId}-{CacheFormat}", out FileInfo? cacheFileInfo) && cacheFileInfo != null)
                return (false, cacheFileInfo);
            else if (CachingQueue.Any(queueItem => $"{queueItem.MediaItem.MediaId}-{CacheFormat}" == $"{mediaItem.MediaId}-{CacheFormat}"))
                return (true, null);
            else
                return (await QueueCacheItem(mediaItem), null);  
        }

        public void CancelCaching()
        {
            CachingCancellationTokenSource.Cancel();
            CachingCancellationTokenSource = new();
        }

        public void ClearCachingQueue()
        {
            foreach (MediaItem mediaItem in CachingQueue.Select(queueItem => queueItem.MediaItem))
            {
                CachingUpdate?.Invoke(this,
                    new CachingUpdateEventArgs
                    {
                        CacheFileInfo = null,
                        MediaItemId = mediaItem.Id,
                        CacheStatus = CacheStatus.Uncached,
                        CacheRatio = 0
                    });
            }
            CachingQueue.Clear();
        }

        public void UpdateMediaQueueCacheStatus(IEnumerable<MediaItem> mediaItems)
        {
            foreach (MediaItem mediaItem in mediaItems)
                if (mediaItem.CacheStatus != CacheStatus.Off && CacheFileInfos.TryGetValue($"{mediaItem.MediaId}-{CacheFormat}", out FileInfo? cacheFileInfo) && cacheFileInfo != null)
                    CachingUpdate?.Invoke(this,
                        new CachingUpdateEventArgs
                        {
                            CacheFileInfo = null,
                            MediaItemId = mediaItem.Id,
                            CacheStatus = CacheStatus.Cached,
                            CacheRatio = 1
                        });
        }

        #endregion

        #region Private Methods

        private async Task<bool> TryRunCacheDeletionAlgorithm(long neededBytes)
        {
            neededBytes += CachingQueue.Sum(queueItem => queueItem.MediaItem.Size);

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
                DateTime minimumLastAccessed = CacheFileInfos.Values.Min(cacheFileInfo => cacheFileInfo.LastAccessTime);
                DateTime maximumLastAccessed = CacheFileInfos.Values.Max(cacheFileInfo => cacheFileInfo.LastAccessTime);
                long lastAccessedDifference = (maximumLastAccessed - minimumLastAccessed).Ticks;

                long smallestSize = CacheFileInfos.Values.Min(cacheFileInfo => cacheFileInfo.Length);
                long largestSize = CacheFileInfos.Values.Max(cacheFileInfo => cacheFileInfo.Length);
                long sizeDifference = largestSize - smallestSize;

                Dictionary<FileInfo, int> scoredCacheFileInfos = new();
                foreach (FileInfo cacheFileInfo in CacheFileInfos.Values)
                    scoredCacheFileInfos.Add(cacheFileInfo, Convert.ToInt32(Math.Round(Math.Exp(
                        (((maximumLastAccessed - minimumLastAccessed).Ticks / lastAccessedDifference) * 100 * (1 - CacheAlgorithmRatio)) +
                        (((maximumLastAccessed - minimumLastAccessed).Ticks / lastAccessedDifference) * 100 * CacheAlgorithmRatio)))));

                IEnumerable<FileInfo> orderedCacheFileInfos = scoredCacheFileInfos.OrderByDescending(scoredCacheItem => scoredCacheItem.Value).Select(scoredCacheItem => scoredCacheItem.Key);
                while (driveInfo.AvailableFreeSpace < neededBytes)
                {
                    FileInfo? deletingCacheFileInfo = orderedCacheFileInfos.FirstOrDefault();

                    if (deletingCacheFileInfo == null)
                    {
                        await LoggingService.LogWarning("Caching Service", $"Remaining disk space not enough to cache item. Missing {(neededBytes - driveInfo.AvailableFreeSpace).GetBytesReadable()}.");
                        return false;
                    }
                    else
                    {
                        string cacheKey = Path.GetFileNameWithoutExtension(deletingCacheFileInfo.Name);
                        await LoggingService.LogDebug("Caching Service", $"Deleting cache item \"{cacheKey}\".");

                        CacheFileInfos.Remove(cacheKey);
                        deletingCacheFileInfo.Delete();
                    }
                }

                return true;
            }
        }

        private async Task<bool> QueueCacheItem(MediaItem mediaItem)
        {
            if (!await TryRunCacheDeletionAlgorithm(mediaItem.Size))
            {
                await LoggingService.LogWarning("Caching Service", $"Could not get enough available space to cache \"{mediaItem.Metadata.Title}\".");
                return false;
            }

            string cacheKey = $"{mediaItem.MediaId}-{CacheFormat}";
            FileInfo cacheFileInfo = new(Path.Combine(CacheDirectoryInfo.FullName, $"{cacheKey}{(mediaItem.Container.StartsWith(".") ? string.Empty : ".")}{mediaItem.Container}"));

            CachingUpdate?.Invoke(this,
                new CachingUpdateEventArgs
                {
                    CacheFileInfo = null,
                    MediaItemId = mediaItem.Id,
                    CacheStatus = CacheStatus.Queued,
                    CacheRatio = 0
                });

            CachingQueue.Enqueue((cacheFileInfo, mediaItem));

            _ = Task.Run(ProcessDownloadQueue);            

            return true;
        }

        private async Task ProcessDownloadQueue()
        {
            if (IsProcessingCacheQueue)
                return;

            IsProcessingCacheQueue = true;

            while (CachingQueue.TryPeek(out (FileInfo CacheFileInfo, MediaItem MediaItem) queueItem))
            {
                MediaItem mediaItem = queueItem.MediaItem;
                FileInfo cacheFileInfo = queueItem.CacheFileInfo;
                string cacheKey = $"{mediaItem.MediaId}-{CacheFormat}";

                try
                {
                    await LoggingService.LogDebug("Caching Service", $"Caching \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");

                    Progress<DownloadProgress> downloadProgress = new(downloadProgress =>
                    {
                        CachingUpdate?.Invoke(this,
                            new CachingUpdateEventArgs
                            {
                                CacheFileInfo = null,
                                MediaItemId = mediaItem.Id,
                                CacheStatus = CacheStatus.Caching,
                                CacheRatio = downloadProgress.Progress
                            });
                    });

                    OptionSet optionSet = (OptionSet)OptionSet.Clone();
                    optionSet.Output = Path.Combine("yt-dlp", cacheFileInfo.Name);

                    RunResult<string>? runResult = null;
                    if (CacheFormat == CacheFormat.Audio)
                        runResult = await YoutubeDL.RunAudioDownload(
                            mediaItem.Location,
                            progress: downloadProgress,
                            ct: CachingCancellationTokenSource.Token,
                            overrideOptions: optionSet);
                    else if (CacheFormat == CacheFormat.Video)
                    {
                        optionSet.SubLangs = "all";
                        runResult = await YoutubeDL.RunVideoDownload(
                            mediaItem.Location,
                            progress: downloadProgress,
                            ct: CachingCancellationTokenSource.Token,
                            overrideOptions: optionSet);
                    }

                    if (runResult != null && runResult.Success && !string.IsNullOrWhiteSpace(runResult.Data))
                    {
                        File.Move(runResult.Data, cacheFileInfo.FullName);

                        await LoggingService.LogDebug("Caching Service", $"Succesfully cached \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");

                        CacheFileInfos.Add(cacheKey, cacheFileInfo);

                        CachingUpdate?.Invoke(this,
                            new CachingUpdateEventArgs
                            {
                                CacheFileInfo = cacheFileInfo,
                                MediaItemId = mediaItem.Id,
                                CacheStatus = CacheStatus.Cached,
                                CacheRatio = 1
                            });
                    }
                    else
                    {
                        CachingUpdate?.Invoke(this,
                            new CachingUpdateEventArgs
                            {
                                CacheFileInfo = null,
                                MediaItemId = mediaItem.Id,
                                CacheStatus = CacheStatus.Uncached,
                                CacheRatio = 0
                            });

                        await LoggingService.LogError("Caching Service", $"Failed to cache \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\": {(runResult != null ? string.Join("; ", runResult.ErrorOutput) : "N/A")}.");
                    }
                }
                catch (TaskCanceledException)
                {
                    CachingUpdate?.Invoke(this,
                            new CachingUpdateEventArgs
                            {
                                CacheFileInfo = null,
                                MediaItemId = mediaItem.Id,
                                CacheStatus = CacheStatus.Uncached,
                                CacheRatio = 0
                            });

                    await LoggingService.LogDebug("Caching Service", $"Canceled caching of \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");
                }
                catch (Exception exception)
                {
                    CachingUpdate?.Invoke(this,
                            new CachingUpdateEventArgs
                            {
                                CacheFileInfo = null,
                                MediaItemId = mediaItem.Id,
                                CacheStatus = CacheStatus.Uncached,
                                CacheRatio = 0
                            });

                    await LoggingService.LogError("Caching Service", $"Error during caching of \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\": {exception.Message}");
                }
                finally
                {
                    CachingQueue.TryDequeue(out (FileInfo CacheFileInfo, MediaItem MediaItem) _);
                }
            }        

            IsProcessingCacheQueue = false;
        }

        #endregion

        #region IDIsposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    CachingQueue.Clear();
                    CachingCancellationTokenSource.Cancel();
                    CacheFileInfos.Clear();
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