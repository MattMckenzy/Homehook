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

        private ElementReference CardReference { get; set; }
        private ElementReference ProgressBar { get; set; }

        private bool IsEditingQueue { get; set; }
        private bool IsLoading = true;

        private readonly CancellationTokenSource PeriodicTimerCancellationTokenSource = new();
        private bool DisposedValue { get; set; }

        private Random Random { get; } = new(DateTime.Now.Ticks.GetHashCode());
        private List<string> BarStyles { get; } = new();

        private List<string> SelectedMediaItemIds { get; set; } = new();

        private Dictionary<string, ProgressCircle> ProgressCircles { get; } = new();

        #endregion

        #region Lifecycle Overrides

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            for(int barNumber = 0 ; barNumber < 122; barNumber++)
            {
                BarStyles.Add($"left: {barNumber * 4}px; animation-duration: {Math.Round(400 + (Random.NextDouble() * 100))}ms");
            }

            DeviceService.DeviceUpdated += async (object sender, Device device) =>
                await UpdateStatus();

            DeviceService.MediaItemCacheUpdated += async (object sender, MediaItem mediaItem, CacheStatus cacheStatus, double cacheRatio) =>
            {
                if (ProgressCircles.TryGetValue(mediaItem.Id, out ProgressCircle? progressCircle) && progressCircle != null)
                    await progressCircle.UpdateCacheStatus(cacheStatus, cacheRatio);

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
            if (DeviceService.Device.DeviceStatus == PlayerStatus.Stopped)
                IsEditingQueue = false;

            await InvokeAsync(StateHasChanged);
        }

        private async Task ToggleMediaSelection()
        {
            if (DeviceService.Device.MediaQueue.All(mediaItem => SelectedMediaItemIds.Contains(mediaItem.Id)))
                SelectedMediaItemIds.Clear();
            else
                SelectedMediaItemIds = DeviceService.Device.MediaQueue.Select(mediaItem => mediaItem.Id).ToList();

            await InvokeAsync(StateHasChanged);
        }

        private async Task ToggleMediaItemSelection(MediaItem mediaItem)
        {
            if (SelectedMediaItemIds.Contains(mediaItem.Id))
                SelectedMediaItemIds.Remove(mediaItem.Id);
            else
                SelectedMediaItemIds.Add(mediaItem.Id);

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

        private async Task SearchAddMediaItems(bool launch = false, bool insertBeforeMediaItem = false)
        {
            string searchTerm = await JSRuntime.InvokeAsync<string>("prompt", $"Please enter search term to add items for {DeviceService.Device.Name}'s queue.", "");
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                try
                {
                    string? insertBeforeMediaItemId = insertBeforeMediaItem ? DeviceService.Device.MediaQueue.FirstOrDefault(mediaItem => SelectedMediaItemIds.Contains(mediaItem.Id))?.Id : null;
                    bool addedItem = false;
                    await foreach (MediaItem mediaItem in DeviceService.GetItems(await LanguageService.ParseSimplePhrase(searchTerm)))
                    {     
                        await DeviceService.AddMediaItems(new List<MediaItem> { mediaItem }, !addedItem && launch, insertBeforeMediaItemId);
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

        private async Task PlayMediaItem()
        {
            string? mediaItemId = DeviceService.Device.MediaQueue.FirstOrDefault(mediaItem => SelectedMediaItemIds.Contains(mediaItem.Id))?.Id;
            if (mediaItemId != null)
                await DeviceService.PlayMediaItem(mediaItemId);
        }

        private async Task RemoveMediaItems()
        {
            IEnumerable<string> mediaItemIds = DeviceService.Device.MediaQueue.Where(mediaItem => SelectedMediaItemIds.Contains(mediaItem.Id)).Select(mediaItem => mediaItem.Id);
            if (mediaItemIds.Any())
                await DeviceService.RemoveMediaItems(mediaItemIds);
        }

        private async Task MoveMediaItemsUp()
        {
            IEnumerable<string> mediaItemIds = DeviceService.Device.MediaQueue.Where(mediaItem => SelectedMediaItemIds.Contains(mediaItem.Id)).Select(mediaItem => mediaItem.Id);
            if (mediaItemIds.Any())
                await DeviceService.MoveMediaItemsUp(mediaItemIds);
        }

        private async Task MoveMediaItemsDown()
        {
            IEnumerable<string> mediaItemIds = DeviceService.Device.MediaQueue.Where(mediaItem => SelectedMediaItemIds.Contains(mediaItem.Id)).Select(mediaItem => mediaItem.Id);
            if (mediaItemIds.Any())
                await DeviceService.MoveMediaItemsDown(mediaItemIds);
        }

        private async Task SeekClick(MouseEventArgs mouseEventArgs)
        {
            if (DeviceService.Device.CurrentMedia == null) 
                return;

            double width = await JSRuntime.InvokeAsync<double>("GetElementWidth", ProgressBar);
            double seekSeconds = mouseEventArgs.OffsetX / width * DeviceService.Device.CurrentMedia.Runtime;

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