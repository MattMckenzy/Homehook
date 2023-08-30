using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using System.Collections.Concurrent;
using System.Runtime;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace HomeCast.Services
{
    public class CachingService : IDisposable
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

            CacheDirectoryInfo = new(Configuration["Services:Caching:CacheLocation"] ?? Path.Combine("/", "home", "homecast", "cache"));
            if (!CacheDirectoryInfo.Exists)
                CacheDirectoryInfo.Create();

            TempDirectoryInfo = new(Path.Combine("/", "home", "homecast", "tmp"));
            if (!TempDirectoryInfo.Exists)
                TempDirectoryInfo.Create();

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
                RestrictFilenames = true,
                OutputFolder = TempDirectoryInfo.FullName
            };

            OptionSet = new()
            {
                Paths = TempDirectoryInfo.FullName,
                NoCacheDir = true,
                RemoveCacheDir = true,
                EmbedSubs = true,
                SubLangs = "all",
                AddHeader = $"User-Agent:{Configuration["Services:Caching:UserAgent"] ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36"}"
            };

            _ = Task.Run(async () =>
            {
                while (await PeriodicTimer.WaitForNextTickAsync())
                {
                    if (CachingUpdateCallback != null && CurrentCachingInformation != null)
                        await CachingUpdateCallback.InvokeAsync(CurrentCachingInformation);
                }
            });
        }

        #endregion

        #region Private Properties

        private DirectoryInfo CacheDirectoryInfo { get; }
        private DirectoryInfo TempDirectoryInfo { get; }
        
        private long CacheSizeBytes { get; }
        private double CacheAlgorithmRatio { get; }
        private CacheFormat CacheFormat { get; }

        private Dictionary<string, FileInfo> CacheFileInfos { get; } = new();

        private ConcurrentQueue<(FileInfo CacheFileInfo, MediaItem MediaItem)> CachingQueue { get; } = new();
        private MediaItem? CurrentCachingMediaItem = null;
        private CachingInformation? CurrentCachingInformation = null;
        private CancellationTokenSource CachingCancellationTokenSource = new();
        private SemaphoreQueue CachingLock { get; } = new(1);

        private PeriodicTimer PeriodicTimer { get; } = new(TimeSpan.FromMilliseconds(500));

        private YoutubeDL YoutubeDL { get; } = new();
        private OptionSet OptionSet { get; } = new();

        private bool DisposedValue { get; set; }

        #endregion

        #region CacheService Implementation

        public static Func<CachingInformation, Task>? CachingUpdateCallback { get; set; }

        public async Task<(bool, CachingInformation)> TryGetOrQueueCacheItem(MediaItem mediaItem)
        {
            if (CacheFileInfos.TryGetValue($"{mediaItem.MediaId}-{CacheFormat}", out FileInfo? cacheFileInfo) && cacheFileInfo != null)
                return (false, new CachingInformation { CacheFileInfo = cacheFileInfo, MediaId = mediaItem.MediaId, CachedRatio = 1, CacheStatus = CacheStatus.Cached });
            else if (CurrentCachingInformation != null && CurrentCachingInformation.MediaId == mediaItem.MediaId)
                return (true, CurrentCachingInformation);
            else if (CachingQueue.Any(queueItem => $"{queueItem.MediaItem.MediaId}-{CacheFormat}" == $"{mediaItem.MediaId}-{CacheFormat}"))
                return (true, new CachingInformation { CacheFileInfo = null, MediaId = mediaItem.MediaId, CachedRatio = 0, CacheStatus = CacheStatus.Queued });
            else
                return await QueueCacheItem(mediaItem);  
        }

        public CachingInformation? UpdateMediaItemsCacheStatus(MediaItem mediaItem)
        {
            if (mediaItem.CacheStatus != CacheStatus.Off && CacheFileInfos.TryGetValue($"{mediaItem.MediaId}-{CacheFormat}", out FileInfo? cacheFileInfo) &&
                cacheFileInfo != null &&
                CachingUpdateCallback != null)
                return new CachingInformation
                {
                    CacheFileInfo = null,
                    MediaId = mediaItem.MediaId,
                    CacheStatus = CacheStatus.Cached,
                    CachedRatio = 1
                };
            else return null;
        }

        public async Task<IEnumerable<CachingInformation>> UpdateCachingQueue(IEnumerable<MediaItem> mediaItems)
        {
            if (mediaItems.Any() &&
                CurrentCachingInformation != null &&
                mediaItems.FirstOrDefault(mediaItem => mediaItem.CacheStatus != CacheStatus.Cached)?.MediaId != CurrentCachingInformation.MediaId)
            {
                CancellationTokenSource currentCancellationTokenSource = CachingCancellationTokenSource;
                CachingCancellationTokenSource = new();
                currentCancellationTokenSource.Cancel();
            }

            List<CachingInformation> returningStatuses = new();
            foreach(MediaItem mediaItem in CachingQueue.Where(cachingQueueItem => !mediaItems.Contains(cachingQueueItem.MediaItem)).Select(cachingQueueItem => cachingQueueItem.MediaItem))
                if ((!CacheFileInfos.TryGetValue($"{mediaItem.MediaId}-{CacheFormat}", out FileInfo? cacheFileInfo) || cacheFileInfo == null) && CachingUpdateCallback != null)
                    returningStatuses.Add(
                        new CachingInformation
                        {
                            CacheFileInfo = null,
                            MediaId = mediaItem.MediaId,
                            CacheStatus = CacheStatus.Uncached,
                            CachedRatio = 0
                        });            

            CachingQueue.Clear();

            foreach (MediaItem mediaItem in mediaItems)
            {
                if (mediaItem.CacheStatus != CacheStatus.Off)
                {
                    (bool ItemQueued, CachingInformation CachingInformation) = await TryGetOrQueueCacheItem(mediaItem);
                    if (ItemQueued)
                        returningStatuses.Add(CachingInformation);
                }
            }

            return returningStatuses;
        }

        #endregion

        #region Private Methods

        private async Task<bool> TryRunCacheDeletionAlgorithm(long neededBytes)
        {
            neededBytes += CachingQueue.Sum(queueItem => queueItem.MediaItem.Size) + CurrentCachingMediaItem?.Size ?? 0;

            DriveInfo driveInfo = new(CacheDirectoryInfo.Root.FullName);
            if (CacheSizeBytes < neededBytes)
            {
                await LoggingService.LogWarning("Caching Service", $"Configured cache size not enough to cache item. Missing {(neededBytes - CacheSizeBytes).GetBytesReadable()}.");
                return false;
            }
            else if (CacheSizeBytes - await Task.Run(() => CacheDirectoryInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)) > neededBytes &&
                driveInfo.AvailableFreeSpace > neededBytes)
                return true;
            else
            {
                DateTime minimumLastAccessed = CacheFileInfos.Values.Min(cacheFileInfo => cacheFileInfo.LastAccessTime);
                DateTime maximumLastAccessed = CacheFileInfos.Values.Max(cacheFileInfo => cacheFileInfo.LastAccessTime);
                long lastAccessedDifference = (maximumLastAccessed - minimumLastAccessed).Ticks;

                long smallestSize = CacheFileInfos.Values.Min(cacheFileInfo => cacheFileInfo.Length);
                long largestSize = CacheFileInfos.Values.Max(cacheFileInfo => cacheFileInfo.Length);
                long sizeDifference = largestSize - smallestSize;

                Dictionary<FileInfo, double> scoredCacheFileInfos = new();
                foreach (FileInfo cacheFileInfo in CacheFileInfos.Values)
                    scoredCacheFileInfos.Add(cacheFileInfo, Math.Round(Math.Exp(
                        (((maximumLastAccessed - minimumLastAccessed).Ticks / lastAccessedDifference) * 100 * (1 - CacheAlgorithmRatio)) +
                        (((maximumLastAccessed - minimumLastAccessed).Ticks / lastAccessedDifference) * 100 * CacheAlgorithmRatio))));

                List<FileInfo> orderedCacheFileInfos = scoredCacheFileInfos.OrderByDescending(scoredCacheItem => scoredCacheItem.Value).Select(scoredCacheItem => scoredCacheItem.Key).ToList();
                while (CacheSizeBytes - await Task.Run(() => CacheDirectoryInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)) < neededBytes ||
                    driveInfo.AvailableFreeSpace < neededBytes)
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

                        orderedCacheFileInfos.Remove(deletingCacheFileInfo);
                        CacheFileInfos.Remove(cacheKey);
                        deletingCacheFileInfo.Delete();
                    }
                }

                return true;
            }
        }

        private async Task<(bool ItemQueued, CachingInformation CacheInformation)> QueueCacheItem(MediaItem mediaItem)
        {
            if (!await TryRunCacheDeletionAlgorithm(mediaItem.Size))
            {
                await LoggingService.LogWarning("Caching Service", $"Could not get enough available space to cache \"{mediaItem.Metadata.Title}\".");
                return (false, new CachingInformation
                {
                    CacheFileInfo = null,
                    MediaId = mediaItem.MediaId,
                    CacheStatus = CacheStatus.Uncached,
                    CachedRatio = 0
                });
            }

            string cacheKey = $"{mediaItem.MediaId}-{CacheFormat}";
            FileInfo cacheFileInfo = new(Path.Combine(CacheDirectoryInfo.FullName, $"{cacheKey}{(mediaItem.Container.StartsWith(".") ? string.Empty : ".")}{mediaItem.Container}"));

            CachingQueue.Enqueue((cacheFileInfo, mediaItem));

            _ = Task.Run(ProcessDownloadQueue);            

            return (true, new CachingInformation
            {
                CacheFileInfo = null,
                MediaId = mediaItem.MediaId,
                CacheStatus = CacheStatus.Queued,
                CachedRatio = 0
            });
        }

        private async Task ProcessDownloadQueue()
        {
            await CachingLock.WaitAsync();

            try
            {
                if (CachingQueue.TryDequeue(out (FileInfo CacheFileInfo, MediaItem MediaItem) queueItem))
                {
                    CurrentCachingMediaItem = queueItem.MediaItem;
                    CurrentCachingInformation = new CachingInformation
                    {
                        CacheFileInfo = null,
                        MediaId = queueItem.MediaItem.MediaId,
                        CacheStatus = CacheStatus.Caching,
                        CachedRatio = 0
                    };

                    MediaItem mediaItem = queueItem.MediaItem;
                    FileInfo cacheFileInfo = queueItem.CacheFileInfo;
                    string cacheKey = $"{mediaItem.MediaId}-{CacheFormat}";

                    try
                    {
                        await LoggingService.LogDebug("Caching Service", $"Caching \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");

                        Progress<DownloadProgress> downloadProgress = new(downloadProgress =>
                        {
                            CurrentCachingInformation.CachedRatio = downloadProgress.Progress;
                        });

                        OptionSet optionSet = (OptionSet)OptionSet.Clone();                        
                        optionSet.Output = Path.Combine(TempDirectoryInfo.FullName, cacheFileInfo.Name);

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

                            CurrentCachingInformation.CacheFileInfo = cacheFileInfo;
                            CurrentCachingInformation.CacheStatus = CacheStatus.Cached;
                            CurrentCachingInformation.CachedRatio = 1;
                            if (CachingUpdateCallback != null)
                                await CachingUpdateCallback.InvokeAsync(CurrentCachingInformation);
                        }
                        else
                        {
                            CurrentCachingInformation.CacheStatus = CacheStatus.Uncached;
                            CurrentCachingInformation.CachedRatio = 0;
                            if (CachingUpdateCallback != null)
                                await CachingUpdateCallback.InvokeAsync(CurrentCachingInformation);

                            await LoggingService.LogError("Caching Service", $"Failed to cache \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\": {(runResult != null ? string.Join("; ", runResult.ErrorOutput) : "N/A")}.");
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        CurrentCachingInformation.CacheStatus = CacheStatus.Uncached;
                        CurrentCachingInformation.CachedRatio = 0;
                        if (CachingUpdateCallback != null)
                            await CachingUpdateCallback.InvokeAsync(CurrentCachingInformation);

                        await LoggingService.LogDebug("Caching Service", $"Canceled caching of \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\".");
                    }
                    catch (Exception exception)
                    {
                        CurrentCachingInformation.CacheStatus = CacheStatus.Uncached;
                        CurrentCachingInformation.CachedRatio = 0;
                        if (CachingUpdateCallback != null)
                            await CachingUpdateCallback.InvokeAsync(CurrentCachingInformation);

                        await LoggingService.LogError("Caching Service", $"Error during caching of \"{mediaItem.Metadata.Title}\" from \"{mediaItem.Location}\": {exception.Message}");
                    }
                    finally
                    {
                        CurrentCachingMediaItem = null;
                        CurrentCachingInformation = null;
                    }
                }
            }
            finally
            {
                CachingLock.Release();
            }
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