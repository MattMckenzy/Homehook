using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models.Jellyfin;
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

        private LoggingService<DeviceService> LoggingService { get; }
        private JellyfinService JellyfinService { get; }

        #endregion

        #region Public Variables

        public required Device Device { get; set; }
        public required HubConnection HubConnection { get; set; }

        public DeviceEvent? DeviceUpdated;

        public delegate void DeviceEvent(object sender, Device device);

        #endregion

        #region Private Variables

        private bool DisposedValue { get; set; }

        #endregion

        #region Constructor

        public DeviceService(JellyfinService jellyfinService, LoggingService<DeviceService> loggingService)
        {
            JellyfinService = jellyfinService;
            LoggingService = loggingService;
        }

        #endregion

        #region Device Commands

        public async Task StartJellyfinSession(List<MediaItem> items)
        {
            if (!items.Any())
                await LoggingService.LogError("Jellyfin Session Start", "There are no items to initialize!");

            _ = Task.Run(async () =>
            {
                await HubConnection.InvokeAsync("LaunchQueue", items);

                await LoggingService.LogDebug("Started Jellyfin Session", $"Succesfully initialized media on device: \"{Device.Name}\"");
            });
        }

        public async Task LaunchQueue(List<MediaItem> medias)
        {
            if (medias.Any())
                await HubConnection.InvokeAsync("LaunchQueue", medias);
        }

        public async Task PlayItem(int itemId) =>
            await HubConnection.InvokeAsync("ChangeCurrentMedia", itemId);

        public async Task UpItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("UpQueue", itemIds);

        public async Task DownItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("DownQueue", itemIds);

        public async Task AddItems(List<MediaItem> medias, int? insertBefore = null)
        {
            if (medias.Any())
                await HubConnection.InvokeAsync("InsertQueue", medias, insertBefore);
        }

        public async Task RemoveItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("RemoveQueue", itemIds);

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

        public async Task UpdateDevice(Device? device = null)
        {
            if (device != null)
            {
                Device = device;
                DeviceUpdated?.Invoke(this, Device);
            }

            switch (Device.DeviceStatus)
            {
                case DeviceStatus.Playing:
                    if (Math.Round(Device.CurrentTime) % 5 == 0)
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
                case DeviceStatus.Finishing:
                    await JellyfinService.MarkPlayed(Device.CurrentMedia?.User, Device.CurrentMedia?.Id);
                    break;
                case DeviceStatus.Stopping:
                    await JellyfinService.UpdateProgress(GetProgress(finished: null), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version, true);
                    break;
                case DeviceStatus.Stopped:
                default:
                    break;
            }
        }

        private Progress? GetProgress(ProgressEvents? progressEvent = null, bool? finished = false)
        {
            if (Device.CurrentMedia == null)
                return null;

            Progress returningProgress = new()
            {
                EventName = progressEvent,
                ItemId = Device.CurrentMedia.Id,
                MediaSourceId = Device.CurrentMedia.Id,
                VolumeLevel = Convert.ToInt32(Device.Volume * 100),
                IsMuted = Device.IsMuted,
                IsPaused = Device.DeviceStatus == DeviceStatus.Pausing || Device.DeviceStatus == DeviceStatus.Paused,
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
