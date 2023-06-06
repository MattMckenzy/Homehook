using HomeHook.Common.Exceptions;
using HomeHook.Common.Models;
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

        private async void UpdateMediaItemsSelection(IEnumerable<MediaItem> mediaItems, bool IsSelected)
        {
            List<int> mediaItemIndices = new();
            foreach (MediaItem mediaItem in mediaItems)
            {
                if (IsSelected)
                    mediaItem.IsSelected = true;
                else
                    mediaItem.IsSelected = false;

                mediaItemIndices.Add(Device.MediaQueue.IndexOf(mediaItem));
            }
            
            if (mediaItemIndices.Any())
            {
                await DeviceService.UpdateMediaItemsSelection(mediaItemIndices, IsSelected);
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task SearchAddMediaItems(bool launch = false, bool insertBeforeSelectedMediaItem = false)
        {
            string searchTerm = await JSRuntime.InvokeAsync<string>("prompt", $"Please enter search term to add items for {Device.Name}'s queue.", "");
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                try
                {
                    bool addedItem = false;
                    await foreach (MediaItem mediaItem in DeviceService.GetItems(await LanguageService.ParseSimplePhrase(searchTerm)))
                    {     
                        await DeviceService.AddMediaItems(new List<MediaItem> { mediaItem }, !addedItem && launch, insertBeforeSelectedMediaItem);
                        addedItem = true;
                    }

                    if (!addedItem)
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

        private async Task PlaySelectedMediaItem() =>
            await DeviceService.PlaySelectedMediaItem();

        private async Task RemoveSelectedMediaItems() =>
            await DeviceService.RemoveSelectedMediaItems();

        private async Task MoveSelectedMediaItemsUp() =>
            await DeviceService.MoveSelectedMediaItemsUp();

        private async Task MoveSelectedMediaItemsDown() =>
            await DeviceService.MoveSelectedMediaItemsDown();

        private async Task SeekClick(MouseEventArgs mouseEventArgs)
        {
            if (Media == null) 
                return;

            double width = await JSRuntime.InvokeAsync<double>("GetElementWidth", ProgressBar);
            double seekSeconds = mouseEventArgs.OffsetX / width * Media.Runtime;

            await DeviceService.Seek(seekSeconds);
        }

        private async Task PlayPauseClick(MouseEventArgs _) =>
            await DeviceService.PlayPause();

        private async Task StopClick(MouseEventArgs _) =>
            await DeviceService.Stop();

        private async Task RewindClick(MouseEventArgs _) =>
            await DeviceService.SeekRelative(-10);

        private async Task FastForwardClick(MouseEventArgs _) =>
            await DeviceService.SeekRelative(+10);

        private async Task PreviousClick(MouseEventArgs _) =>
            await DeviceService.Previous();

        private async Task NextClick(MouseEventArgs _) =>
            await DeviceService.Next();

        private async Task SetRepeatMode(RepeatMode repeatMode) =>
            await DeviceService.SetRepeatMode(repeatMode);

        private async Task SetPlaybackRate(double playBackRate) =>
            await DeviceService.SetPlaybackRate(playBackRate);

        private async Task SetVolume(ChangeEventArgs changeEventArgs) =>
            await DeviceService.SetVolume(float.Parse(changeEventArgs.Value?.ToString() ?? "0.5"));

        private async Task ToggleMute(MouseEventArgs _) =>
            await DeviceService.ToggleMute();

        private async Task ToggleEditingQueue(MouseEventArgs _)
        {
            IsEditingQueue = !IsEditingQueue;
            if (IsEditingQueue)
                await JSRuntime.InvokeVoidAsync("ChangeElementHeight", CardReference, 750);
            else
                await JSRuntime.InvokeVoidAsync("ChangeElementHeight", CardReference, 250);
        }

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