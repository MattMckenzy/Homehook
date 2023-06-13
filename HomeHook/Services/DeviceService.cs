using HomeHook.Common.Exceptions;
using HomeHook.Common.Models;
using HomeHook.Models.Jellyfin;
using HomeHook.Models.Language;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

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

        // TODO: Seperate update events to minimize communication.

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
                    await Task.Delay(333);
                }
            }
            else
                await foreach (MediaItem mediaItem in SearchService.Search(languagePhrase))
                    yield return mediaItem;
        }

        public async Task PlayMediaItem(string mediaItemId) =>
            await HubConnection.InvokeAsync("PlayMediaItem", mediaItemId);

        public async Task AddMediaItems(List<MediaItem> mediaItems, bool launch = false, string? insertBeforeMediaItemId = null)
        {
            if (mediaItems.Any())
                await HubConnection.InvokeAsync("AddMediaItems", mediaItems.ToArray(), launch, insertBeforeMediaItemId);
        }

        public async Task RemoveMediaItems(IEnumerable<string> mediaItemIds) =>
            await HubConnection.InvokeAsync("RemoveMediaItems", mediaItemIds);

        public async Task MoveMediaItemsUp(IEnumerable<string> mediaItemIds) =>
            await HubConnection.InvokeAsync("MoveMediaItemsUp", mediaItemIds);

        public async Task MoveMediaItemsDown(IEnumerable<string> mediaItemIds) =>
            await HubConnection.InvokeAsync("MoveMediaItemsDown", mediaItemIds);

        public async Task Seek(double seekSeconds) =>
            await HubConnection.InvokeAsync("Seek", seekSeconds);
       
        public async Task SeekRelative(double relativeSeconds) =>
            await HubConnection.InvokeAsync("SeekRelative", relativeSeconds);
        
        public async Task PlayPause()
        {
            if (Device.DeviceStatus == DeviceStatus.Playing)
                await HubConnection.InvokeAsync("Pause");
            else
                await HubConnection.InvokeAsync("Play");
        }

        public async Task Stop() =>
            await HubConnection.InvokeAsync("Stop");

        public async Task Previous() =>
            await HubConnection.InvokeAsync("Previous");

        public async Task Next() =>
            await HubConnection.InvokeAsync("Next");

        public async Task SetRepeatMode(RepeatMode repeatMode) =>
            await HubConnection.InvokeAsync("ChangeRepeatMode", repeatMode);

        public async Task SetPlaybackRate(double playBackRate) =>
            await HubConnection.InvokeAsync("SetPlaybackRate", playBackRate);

        public async Task SetVolume(float volume) =>
            await HubConnection.InvokeAsync("SetVolume", volume);

        public async Task ToggleMute() =>
            await HubConnection.InvokeAsync("ToggleMute");

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
