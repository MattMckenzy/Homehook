using HomeHook.Common.Exceptions;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models.Jellyfin;
using HomeHook.Models.Language;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using MediaSource = HomeHook.Common.Models.MediaSource;

namespace HomeHook.Services
{
    public class DeviceService : IDisposable
    {
        #region Constants

        private const string ServiceName = "HomeCast";

        #endregion

        #region Injections

        private JellyfinService JellyfinService { get; }
        private SearchService SearchService { get; }

        #endregion

        #region Public Variables

        public required Device Device { get; set; }

        public required HubConnection HubConnection { get; set; }

        public DeviceEvent? DeviceUpdated;

        public delegate void DeviceEvent(object sender, Device device);

        public delegate void CurrentTimeEvent(object sender, double currentTime);

        public MediaItemCacheEvent? MediaItemCacheUpdated;

        public delegate void MediaItemCacheEvent(object sender, MediaItem mediaItem, CacheStatus cacheStatus, double cacheRatio);

        #endregion

        #region Private Variables

        private bool DisposedValue { get; set; }

        #endregion

        #region Constructor

        public DeviceService(JellyfinService jellyfinService, SearchService searchService)
        {
            JellyfinService = jellyfinService;
            SearchService = searchService;
        }

        #endregion

        #region Device Commands

        /// <summary>
        /// Enumerates through the media items found with the given language phrase.
        /// </summary>
        /// <param name="languagePhrase">The parsed language phrase.</param>
        /// <returns>Enumerates through the found MediaItems.</returns>
        /// <exception cref="ConfigurationException">Thrown if the application is missing mandatory configuration.</exception>
        /// <exception cref="ArgumentException">Thrown if the given phrase is missing crucial information.</exception>
        public async IAsyncEnumerable<MediaItem> GetItems(LanguagePhrase languagePhrase)
        {
            languagePhrase.Device = Device.Name;
            if (languagePhrase.MediaSource == MediaSource.Jellyfin)
            {
                string? userId = await JellyfinService.GetUserId(languagePhrase.User);
                if (string.IsNullOrWhiteSpace(userId))
                    throw new ConfigurationException($"No Jellyfin user found! - {languagePhrase.SearchTerm}, or the default user, returned no available Jellyfin user Ids.");
                
                foreach (MediaItem mediaItem in await JellyfinService.GetItems(languagePhrase, userId))
                {                    
                    yield return mediaItem;
                    await Task.Delay(200);
                }
            }
            else
                await foreach (MediaItem mediaItem in SearchService.Search(languagePhrase))
                    yield return mediaItem;
        }
        
        public async Task HubInvoke(Func<Task> invokeTask)
        {
            if (HubConnection.State != HubConnectionState.Connected)
            {
                CancellationToken waitForConnectionCancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
                while (!waitForConnectionCancellationToken.IsCancellationRequested &&
                    (HubConnection.State == HubConnectionState.Reconnecting || HubConnection.State == HubConnectionState.Connecting))
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            if (HubConnection.State == HubConnectionState.Connected)
            {
                try
                {
                    await invokeTask.Invoke();
                }
                catch (Exception _) 
                {
                    Device.StatusMessage = "Command failed";
                    DeviceUpdated?.Invoke(this, Device);
                }
            }
            else
            {
                Device.StatusMessage = "Connection failed";
                DeviceUpdated?.Invoke(this, Device);
            }
        }

        public async Task PlayMediaItem(string mediaItemId) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("PlayMediaItem", mediaItemId));

        public async Task AddMediaItems(List<MediaItem> mediaItems, bool launch = false, string? insertBeforeMediaItemId = null)
        {
            if (mediaItems.Any())
                await HubInvoke(async () => await HubConnection.InvokeAsync("AddMediaItems", mediaItems.ToArray(), launch, insertBeforeMediaItemId));
        }

        public async Task RemoveMediaItems(IEnumerable<string> mediaItemIds) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("RemoveMediaItems", mediaItemIds));

        public async Task MoveMediaItemsUp(IEnumerable<string> mediaItemIds) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("MoveMediaItemsUp", mediaItemIds));

        public async Task MoveMediaItemsDown(IEnumerable<string> mediaItemIds) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("MoveMediaItemsDown", mediaItemIds));

        public async Task Seek(double seekSeconds) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("Seek", seekSeconds));
       
        public async Task SeekRelative(double relativeSeconds) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("SeekRelative", relativeSeconds));
        
        public async Task PlayPause()
        {
            if (Device.DeviceStatus == DeviceStatus.Playing)
                await HubInvoke(async () => await HubConnection.InvokeAsync("Pause"));
            else
                await HubInvoke(async () => await HubConnection.InvokeAsync("Play"));
        }

        public async Task Stop() =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("Stop"));

        public async Task Previous() =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("Previous"));

        public async Task Next() =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("Next"));

        public async Task SetRepeatMode(RepeatMode repeatMode) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("ChangeRepeatMode", repeatMode));

        public async Task SetPlaybackRate(double playBackRate) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("SetPlaybackRate", playBackRate));

        public async Task SetVolume(float volume) =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("SetVolume", volume));

        public async Task ToggleMute() =>
            await HubInvoke(async () => await HubConnection.InvokeAsync("ToggleMute"));

        #endregion

        #region Private Helpers

        public async Task UpdateDevice(Device device)
        {
            Device = device;
            DeviceUpdated?.Invoke(this, Device);
           
            if (Device.CurrentMedia?.MediaSource == MediaSource.Jellyfin)
            {
                switch (Device.DeviceStatus)
                {
                    case DeviceStatus.Playing:
                        await JellyfinService.UpdateProgress(GetProgress(ProgressEvents.TimeUpdate), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                        break;
                    case DeviceStatus.Paused:
                        await JellyfinService.UpdateProgress(GetProgress(ProgressEvents.TimeUpdate), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                        break;
                    case DeviceStatus.Pausing:
                        await JellyfinService.UpdateProgress(GetProgress(ProgressEvents.Pause), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                        break;
                    case DeviceStatus.Unpausing:
                        await JellyfinService.UpdateProgress(GetProgress(ProgressEvents.Unpause), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                        break;
                    case DeviceStatus.Starting:
                        await JellyfinService.UpdateProgress(GetProgress(), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                        break;
                    case DeviceStatus.Finished:
                        await JellyfinService.MarkPlayed(Device.CurrentMedia?.User, Device.CurrentMedia?.MediaId);
                        break;
                    case DeviceStatus.Stopping:
                        await JellyfinService.UpdateProgress(GetProgress(finished: null), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version, true);
                        break;
                    case DeviceStatus.Buffering:
                    case DeviceStatus.Ended:
                    case DeviceStatus.Stopped:
                    default:
                        break;
                }
            }
        }     

        private Progress? GetProgress(ProgressEvents? progressEvent = null, bool? finished = false)
        {
            if (Device.CurrentMedia == null)
                return null;

            Progress returningProgress = new()
            {
                EventName = progressEvent,
                ItemId = Device.CurrentMedia.MediaId,
                MediaSourceId = Device.CurrentMedia.MediaId,
                VolumeLevel = Convert.ToInt32(Device.Volume * 100),
                IsMuted = Device.IsMuted,
                IsPaused = Device.DeviceStatus == DeviceStatus.Paused,
                PlaybackRate = Device.PlaybackRate,
                PlayMethod = PlayMethod.DirectPlay
            };

            if (finished != null)
                returningProgress.PositionTicks = (long)((finished == true ? Device.CurrentMedia.Runtime : Device.CurrentTime) * 10000000d);

            return returningProgress;
        }

        #endregion

        #region Device Callbacks

        public Task UpdateDeviceStatus(DeviceStatus deviceStatus)
        {
            Device.DeviceStatus = deviceStatus;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateStatusMessage(string? statusMessage)
        {
            Device.StatusMessage = statusMessage;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateCurrentMediaItemId(string? currentMediaItemId)
        {
            Device.CurrentMediaItemId = currentMediaItemId;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public async Task UpdateCurrentTime(double currentTime)
        {
            Device.CurrentTime = currentTime;
            DeviceUpdated?.Invoke(this, Device);

            if (Device.CurrentMedia?.MediaSource == MediaSource.Jellyfin)
            {
                switch (Device.DeviceStatus)
                {
                    case DeviceStatus.Playing:
                        if (Math.Round(Device.CurrentTime) % 5 == 0)
                            await JellyfinService.UpdateProgress(GetProgress(ProgressEvents.TimeUpdate), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                        break;
                    default:
                        break;
                }
            }
        }

        public Task UpdateStartTime(double startTime)
        {
            if (Device.CurrentMedia != null)
                Device.CurrentMedia.StartTime = startTime;

            return Task.CompletedTask;
        }

        public Task UpdateRepeatMode(RepeatMode repeatMode)
        {
            Device.RepeatMode = repeatMode;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateVolume(float volume)
        {
            Device.Volume = volume;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateIsMuted(bool isMuted)
        {
            Device.IsMuted = isMuted;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdatePlaybackRate(float playbackRate)
        {
            Device.PlaybackRate = playbackRate;
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateAdddedMediaItems(List<MediaItem> mediaItems, bool launch, string? insertBeforeMediaItemId)
        {
            Device.AddMediaItems(mediaItems, launch, insertBeforeMediaItemId);
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateRemovedMediaItems(IEnumerable<string> mediaItemIds)
        {
            Device.RemoveMediaItems(mediaItemIds); 
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task MoveUpMediaItems(IEnumerable<string> mediaItemIds)
        {
            Device.MoveUpMediaItems(mediaItemIds);
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task MoveDownMediaItems(IEnumerable<string> mediaItemIds)
        {
            Device.MoveDownMediaItems(mediaItemIds);
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task ClearMediaItems()
        {
            Device.MediaQueue.Clear();
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task OrderMediaQueue(IEnumerable<string> mediaItemIds)
        {
            Device.OrderMediaItems(mediaItemIds);
            DeviceUpdated?.Invoke(this, Device);

            return Task.CompletedTask;
        }

        public Task UpdateMediaItemCache(string mediaItemId, CacheStatus cacheStatus, double cacheRatio)
        {
            MediaItem? mediaItem = Device.MediaQueue.FirstOrDefault(mediaItem => mediaItem.Id == mediaItemId);

            if (mediaItem != null)
            {
                mediaItem.CacheStatus = cacheStatus;
                mediaItem.CachedRatio = cacheRatio;

                MediaItemCacheUpdated?.Invoke(this, mediaItem, cacheStatus, cacheRatio);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region IDisposable Implementation

        protected virtual async void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    await HubConnection.DisposeAsync();
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
