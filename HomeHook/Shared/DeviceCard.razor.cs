using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models.Jellyfin;
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
        private JellyfinService JellyfinService { get; set; } = null!;

        [Inject]
        private LoggingService<DeviceCard> LoggingService { get; set; } = null!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = null!;

        #endregion

        #region Parameters

        [Parameter]
        public required DeviceService DeviceService { get; set; }

        #endregion

        #region Private Variables

        private HubConnection HubConnection { get; set; } = null!;
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

            HubConnection = DeviceService.HubConnection;
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
            HubConnection = DeviceService.HubConnection;
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

        private async Task<List<MediaItem>> GetItems(string searchTerm)
        {
            JellyPhrase? phrase = await LanguageService.ParseJellyfinSimplePhrase(searchTerm);

            if (phrase == null)
            {
                await JSRuntime.InvokeVoidAsync("alert", $"Missing search content! Please specify a search term along with any optional filters.", "");
                return Array.Empty<MediaItem>().ToList();
            }
            else
                await LoggingService.LogDebug("PostJellySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {searchTerm}", phrase);

            string? userId = await JellyfinService.GetUserId(phrase.User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                await JSRuntime.InvokeVoidAsync("alert", $"No user found! - {phrase.SearchTerm}, or the default user, returned no available user Ids.", "");
                return Array.Empty<MediaItem>().ToList();
            }
                
            phrase.Device = Device.Name;
            List<MediaItem> medias = await JellyfinService.GetItems(phrase, userId);
            
            if (!medias.Any())
                await JSRuntime.InvokeVoidAsync("alert", $"No media found! - {phrase.SearchTerm}, returned no media.", "");

            return medias;
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
            await HubConnection.InvokeAsync("Stop");

        public async Task RewindClick(MouseEventArgs _)
        {
            DeviceService.CurrentTime = Math.Max(DeviceService.CurrentTime - 10, 0);
            await HubConnection.InvokeAsync("SeekRelative", -10);
        }

        public async Task FastForwardClick(MouseEventArgs _)
        {
            DeviceService.CurrentTime = Math.Min(DeviceService.CurrentTime + 10, (double?)Media?.Runtime ?? 0);
            await HubConnection.InvokeAsync("SeekRelative", +10);
        }

        public async Task PreviousClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Previous");

        public async Task NextClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Next");

        public async Task SetRepeatMode(RepeatMode repeatMode) =>
            await HubConnection.InvokeAsync("ChangeRepeatMode", repeatMode);

        public async Task SetPlaybackRate(double playBackRate) =>
            await HubConnection.InvokeAsync("SetPlaybackRate", playBackRate);

        public async Task SetVolume(ChangeEventArgs changeEventArgs) =>
            await HubConnection.InvokeAsync("SetVolume", float.Parse(changeEventArgs.Value?.ToString() ?? "0.5"));

        public async Task ToggleMute(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("ToggleMute");

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
            string searchTerm = await JSRuntime.InvokeAsync<string>("prompt", $"Please enter Jellyfin search term to find items for {Device.Name}'s queue.", "");
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                List<MediaItem> medias = await GetItems(searchTerm);
                if (medias.Any())
                    await HubConnection.InvokeAsync("LaunchQueue", medias);
            }    
        }

        public async Task PlayItem(int itemId) =>
            await HubConnection.InvokeAsync("ChangeCurrentMedia", itemId);

        public async Task UpItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("UpQueue", itemIds);

        public async Task DownItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("DownQueue", itemIds);

        public async Task AddItems(string searchTerm, int? insertBefore = null)
        {
            List<MediaItem> medias = await GetItems(searchTerm);
            if (medias.Any())
                await HubConnection.InvokeAsync("InsertQueue", medias, insertBefore);
        }  

        public async Task RemoveItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("RemoveQueue", itemIds);

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