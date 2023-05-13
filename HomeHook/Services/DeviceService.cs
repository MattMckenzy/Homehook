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

        public double CurrentTime { get; set; }

        public DeviceEvent? DeviceUpdated;

        public delegate void DeviceEvent(object sender, Device device);

        #endregion

        #region Private Variables

        private CancellationTokenSource PeriodicTimerCancellationTokenSource { get; } = new();
        private bool DisposedValue { get; set; }

        #endregion

        #region Constructor

        public DeviceService(JellyfinService jellyfinService, LoggingService<DeviceService> loggingService)
        {
            JellyfinService = jellyfinService;
            LoggingService = loggingService;

            StartDeviceTick();
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

        public async Task Seek(double seekSeconds)
        {
            CurrentTime = Math.Max(Math.Min(seekSeconds, Device.CurrentMedia?.Runtime ?? 0), 0);
            await HubConnection.InvokeAsync("Seek", seekSeconds);
        }

        public async Task SeekRelative(double relativeSeconds)
        {
            CurrentTime = Math.Max(Math.Min(CurrentTime + relativeSeconds, Device.CurrentMedia?.Runtime ?? 0), 0);
            await HubConnection.InvokeAsync("SeekRelative", relativeSeconds);
        }

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

        private async void StartDeviceTick()
        {
            PeriodicTimer periodicTimer = new(TimeSpan.FromSeconds(1));
            while (await periodicTimer.WaitForNextTickAsync(PeriodicTimerCancellationTokenSource.Token))
            {
                if (Device.DeviceStatus == DeviceStatus.Playing)
                {
                    double newTime = CurrentTime + Device.PlaybackRate;
                    if (newTime <= Device.CurrentMedia?.Runtime)
                    {
                        CurrentTime = newTime;
                        if (Math.Round(CurrentTime) % 10 == 0)
                            await UpdateDevice();

                        DeviceUpdated?.Invoke(this, Device);
                    }
                }
            }
        }

        public async Task UpdateDevice(Device? device = null)
        {
            if (device != null)
                Device = device;

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
                    CurrentTime = Device.CurrentMedia?.StartTime ?? 0;
                    await JellyfinService.UpdateProgress(GetProgress(), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                    break;
                case DeviceStatus.Finishing:
                    CurrentTime = Device.CurrentMedia?.Runtime ?? CurrentTime;
                    await JellyfinService.UpdateProgress(GetProgress(ProgressEvents.TimeUpdate), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version);
                    break;
                case DeviceStatus.Stopping:
                    // TODO: update start time when stopping an item, propagate changes to device.
                    await JellyfinService.UpdateProgress(GetProgress(), Device.CurrentMedia?.User, Device.Name, ServiceName, Device.Version, true);
                    break;
                case DeviceStatus.Stopped:
                default:
                    break;
            }
        }

        private Progress? GetProgress(ProgressEvents? progressEvent = null)
        {
            if (Device.CurrentMedia == null)
                return null;

            Progress returningProgress = new()
            {
                EventName = progressEvent,
                ItemId = Device.CurrentMedia.Id,
                MediaSourceId = Device.CurrentMedia.Id,
                PositionTicks = (long)(CurrentTime * 10000000d),
                VolumeLevel = Convert.ToInt32(Device.Volume * 100),
                IsMuted = Device.IsMuted,
                IsPaused = Device.DeviceStatus == DeviceStatus.Pausing || Device.DeviceStatus == DeviceStatus.Paused,
                PlaybackRate = Device.PlaybackRate,
                PlayMethod = PlayMethod.DirectPlay
            };

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
                    PeriodicTimerCancellationTokenSource.Cancel();
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
