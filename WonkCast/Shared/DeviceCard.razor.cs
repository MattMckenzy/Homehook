using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using WonkCast.Common.Models;
using WonkCast.Common.Services;
using WonkCast.Models;
using WonkCast.Models.Jellyfin;
using WonkCast.Services;

namespace WonkCast.Shared
{
    public partial class DeviceCard
    {
        [Inject]
        private LanguageService LanguageService { get; set; } = null!;

        [Inject]
        private JellyfinService JellyfinService { get; set; } = null!;

        [Inject]
        private LoggingService<DeviceCard> LoggingService { get; set; } = null!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = null!;

        [Parameter]
        public required DeviceConnection DeviceConnection { get; set; }

        private HubConnection HubConnection { get; set; } = null!;
        private Device Device { get; set; } = null!;
        private Media? Media { get; set; } = null;

        private ElementReference CardReference { get; set; }
        private ElementReference ProgressBar { get; set; }

        private bool IsEditingQueue { get; set; }
        private bool IsLoading = true;
       
        private readonly System.Timers.Timer Timer = new() { AutoReset = true, Interval = 1000 };

        protected override async Task OnInitializedAsync()
        {
            HubConnection = DeviceConnection.HubConnection;
            Device = DeviceConnection.Device;
            Media = Device.CurrentMedia;

            await base.OnInitializedAsync();

            Timer.Elapsed += async (_, _) => 
            {
                Device.CurrentTime = Device.CurrentTime++;
                await InvokeAsync(StateHasChanged);
            };

            DeviceConnection.DeviceUpdated += async (_, _) =>
            {
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

        private async Task UpdateStatus()
        {
            HubConnection = DeviceConnection.HubConnection;
            Device = DeviceConnection.Device;
            Media = Device.CurrentMedia;

            if (Device.DeviceStatus == DeviceStatus.Playing)
                Timer.Start();
            else
                Timer.Stop();

            if (Device.DeviceStatus == DeviceStatus.Stopped)
                IsEditingQueue = false;

            await InvokeAsync(StateHasChanged);
        }

        private static string GetIcon(MediaKind? mediaKind)
        {
            return mediaKind switch
            {
                MediaKind.Video => "video",
                MediaKind.Movie => "movie",
                MediaKind.SeriesEpisode => "television",
                MediaKind.Song => "music",
                MediaKind.Photo => "image-multiple",
                _ => "cast-off",
            };
        }

        private async Task<List<Media>> GetItems(string searchTerm)
        {
            JellyPhrase? phrase = await LanguageService.ParseJellyfinSimplePhrase(searchTerm);

            if (phrase == null)
            {
                await JSRuntime.InvokeVoidAsync("alert", $"Missing search content! Please specify a search term along with any optional filters.", "");
                return Array.Empty<Media>().ToList();
            }
            else
                await LoggingService.LogDebug("PostJellySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {searchTerm}", phrase);

            string? userId = await JellyfinService.GetUserId(phrase.User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                await JSRuntime.InvokeVoidAsync("alert", $"No user found! - {phrase.SearchTerm}, or the default user, returned no available user Ids.", "");
                return Array.Empty<Media>().ToList();
            }
                
            phrase.Device = Device.Name;
            List<Media> medias = await JellyfinService.GetItems(phrase, userId);
            
            if (!medias.Any())
                await JSRuntime.InvokeVoidAsync("alert", $"No media found! - {phrase.SearchTerm}, returned no media.", "");

            return medias;
        }

        #region Commands

        protected async Task SeekClick(MouseEventArgs mouseEventArgs)
        {
            if (Media == null) return;
            double width = await JSRuntime.InvokeAsync<double>("GetElementWidth", ProgressBar);
            double seekSeconds = mouseEventArgs.OffsetX / width * Media.Runtime;
            await HubConnection.InvokeAsync("Seek", seekSeconds);
        }

        protected async Task PlayPauseClick(MouseEventArgs _)
        {
            if (Device.DeviceStatus == DeviceStatus.Playing)
                await HubConnection.InvokeAsync("Pause");
            else
                await HubConnection.InvokeAsync("Play");
        }

        protected async Task StopClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Stop");

        protected async Task RewindClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Seek", Device.CurrentTime - 10);

        protected async Task FastForwardClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Seek", Device.CurrentTime + 10);

        protected async Task PreviousClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Previous");

        protected async Task NextClick(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("Next");

        protected async Task SetRepeatMode(RepeatMode repeatMode) =>
            await HubConnection.InvokeAsync("ChangeRepeatMode", repeatMode);

        protected async Task SetPlaybackRate(double playBackRate) =>
            await HubConnection.InvokeAsync("SetPlaybackRate", playBackRate);

        protected async Task SetVolume(ChangeEventArgs changeEventArgs) =>
            await HubConnection.InvokeAsync("SetVolume", float.Parse(changeEventArgs.Value?.ToString() ?? "0.5"));

        protected async Task ToggleMute(MouseEventArgs _) =>
            await HubConnection.InvokeAsync("ToggleMute");

        protected async Task ToggleEditingQueue(MouseEventArgs _)
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
                List<Media> medias = await GetItems(searchTerm);
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
            List<Media> medias = await GetItems(searchTerm);
            if (medias.Any())
                await HubConnection.InvokeAsync("InsertQueue", medias, insertBefore);
        }  

        public async Task RemoveItems(IEnumerable<int> itemIds) =>
            await HubConnection.InvokeAsync("RemoveQueue", itemIds);

        #endregion
    }
}