using HomeHook.Common.Exceptions;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models.Language;
using HomeHook.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace HomeHook.Shared
{
    public partial class DeviceCard : IDisposable
    {
        #region Constants

        public const float TimerIntervalSeconds = 1f;

        #endregion

        #region Injections

        [Inject]
        private LanguageService LanguageService { get; set; } = null!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = null!;

        #endregion

        #region Parameters

        [Parameter]
        public required DeviceService DeviceService { get; set; }

        #endregion

        #region Private Variables

        private Device Device { get; set; } = null!;
        private MediaItem? Media { get; set; } = null;

        private ElementReference CardReference { get; set; }
        private ElementReference ProgressBar { get; set; }

        private bool IsEditingQueue { get; set; }
        private bool IsLoading = true;

        private readonly CancellationTokenSource PeriodicTimerCancellationTokenSource = new();
        private bool DisposedValue { get; set; }

        private Random Random { get; } = new(DateTime.Now.Ticks.GetHashCode());
        private List<string> BarStyles { get; } = new();

        private List<string> MediaIds { get; set; } = new();

        #endregion

        #region Lifecycle Overrides

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            Device = DeviceService.Device;
            Media = Device.CurrentMedia;

            for(int barNumber = 0 ; barNumber < 122; barNumber++)
            {
                BarStyles.Add($"left: {barNumber * 4}px; animation-duration: {Math.Round(400 + (Random.NextDouble() * 100))}ms");
            }

            DeviceService.DeviceUpdated += async (object sender, Device device) =>
            {
                Device = device;
                await UpdateStatus();
            };
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                IsLoading = false;
                await UpdateStatus();
            }

        }

        #endregion

        #region Private Methods

        private async Task UpdateStatus()
        {
            Device = DeviceService.Device;
            Media = Device.CurrentMedia;
             
            if (Device.DeviceStatus == DeviceStatus.Stopped)
                IsEditingQueue = false;

            MediaIds = Device.MediaQueue.Select(mediaItem => mediaItem.Id).Join(MediaIds, mediaId => mediaId, mediaId => mediaId, (mediaId, mediaInnerId) => mediaId).ToList();

            await InvokeAsync(StateHasChanged);
        }

        private static string GetIcon(MediaItemKind? mediaKind)
        {
            return mediaKind switch
            {
                MediaItemKind.Video => "video",
                MediaItemKind.Movie => "movie",
                MediaItemKind.SeriesEpisode => "television",
                MediaItemKind.MusicVideo => "music-box-outline",
                MediaItemKind.Song => "music",
                MediaItemKind.Photo => "image",
                _ => "cast-off",
            };
        }

        private async void MediaItemCheckedChanged(string mediaId, ChangeEventArgs changeEventArgs)
        {
            if (changeEventArgs.Value != null &&
                (bool)changeEventArgs.Value &&
                !MediaIds.Contains(mediaId))
                MediaIds.Add(mediaId);
            else if (changeEventArgs.Value != null &&
                !(bool)changeEventArgs.Value &&
                MediaIds.Contains(mediaId))
                MediaIds.Remove(mediaId);

            MediaIds = Device.MediaQueue.Select(mediaItem => mediaItem.Id).Join(MediaIds, mediaId => mediaId, mediaId => mediaId, (mediaId, mediaInnerId) => mediaId).ToList();
            await InvokeAsync(StateHasChanged);
        }

        private async Task SearchPlay(bool launch = true, string? insertBefore = null)
        {
            string searchTerm = await JSRuntime.InvokeAsync<string>("prompt", $"Please enter search term to {(launch ? "find" : "add")} items for {Device.Name}'s queue.", "");
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                try
                {
                    bool firstItem = true;

                    await foreach (MediaItem mediaItem in DeviceService.GetItems(await LanguageService.ParseSimplePhrase(searchTerm)))
                    {
                        if (firstItem && launch)
                            await DeviceService.LaunchQueue(new List<MediaItem> { mediaItem });
                        else
                            await DeviceService.AddItems(new List<MediaItem> { mediaItem }, insertBefore);

                        firstItem = false;
                    }

                    if (firstItem)
                        await JSRuntime.InvokeVoidAsync("alert", $"No media found! - {searchTerm}, returned no media.", "");
                }
                catch (ConfigurationException configurationException)
                {
                    await JSRuntime.InvokeVoidAsync("alert", $"Configuration Error! - {configurationException.Message}.", "");
                }
                catch (ArgumentException argumentException)
                {
                    await JSRuntime.InvokeVoidAsync("alert", $"Search term invalid! - {argumentException.Message}.", "");
                }
            }
        }

        #endregion

        #region Commands

        public async Task SeekClick(MouseEventArgs mouseEventArgs)
        {
            if (Media == null) 
                return;

            double width = await JSRuntime.InvokeAsync<double>("GetElementWidth", ProgressBar);
            double seekSeconds = mouseEventArgs.OffsetX / width * Media.Runtime;

            await DeviceService.Seek(seekSeconds);
        }

        public async Task PlayPauseClick(MouseEventArgs _) =>
            await DeviceService.PlayPause();

        public async Task StopClick(MouseEventArgs _) =>
            await DeviceService.Stop();

        public async Task RewindClick(MouseEventArgs _) =>
            await DeviceService.SeekRelative(-10);
        
        public async Task FastForwardClick(MouseEventArgs _) =>
            await DeviceService.SeekRelative(+10);
        
        public async Task PreviousClick(MouseEventArgs _) =>
            await DeviceService.Previous();

        public async Task NextClick(MouseEventArgs _) =>
            await DeviceService.Next();

        public async Task SetRepeatMode(RepeatMode repeatMode) =>
            await DeviceService.SetRepeatMode(repeatMode);

        public async Task SetPlaybackRate(double playBackRate) =>
            await DeviceService.SetPlaybackRate(playBackRate);

        public async Task SetVolume(ChangeEventArgs changeEventArgs) =>
            await DeviceService.SetVolume(float.Parse(changeEventArgs.Value?.ToString() ?? "0.5"));

        public async Task ToggleMute(MouseEventArgs _) =>
            await DeviceService.ToggleMute();

        public async Task ToggleEditingQueue(MouseEventArgs _)
        {
            IsEditingQueue = !IsEditingQueue;
            if (IsEditingQueue)
                await JSRuntime.InvokeVoidAsync("ChangeElementHeight", CardReference, 750);
            else
                await JSRuntime.InvokeVoidAsync("ChangeElementHeight", CardReference, 250);
        }

        public async Task LaunchQueue(MouseEventArgs _)
        {
            await SearchPlay();
        }

        public async Task PlayItem(string itemId) =>
            await DeviceService.PlayItem(itemId);

        public async Task UpItems(IEnumerable<string> itemIds) =>
            await DeviceService.UpItems(itemIds);

        public async Task DownItems(IEnumerable<string> itemIds) =>
            await DeviceService.DownItems(itemIds);

        public async Task AddItems(string? insertBefore = null) =>
            await SearchPlay(false, insertBefore);
        
        public async Task RemoveItems(IEnumerable<string> itemIds) =>
            await DeviceService.RemoveItems(itemIds);

        #endregion

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    PeriodicTimerCancellationTokenSource.Cancel();
                }

                DisposedValue = true;
            }
        }

        ~DeviceCard()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
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