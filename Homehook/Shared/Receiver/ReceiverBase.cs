using GoogleCast.Models.Media;
using HomehookApp.Extensions;
using HomehookApp.Models;
using HomehookCommon.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomehookApp.Components.Receiver
{
    public class ReceiverBase : ComponentBase, IDisposable
    {
        [Inject]
        public IJSRuntime JSRuntime { get; set; }

        [Inject]
        public IConfiguration Configuration { get; set; }

        [Parameter]
        public string Name { get; set; }

        protected ElementReference CardReceiverReference { get; set; }
        protected ElementReference ProgressBar { get; set; }

        protected ReceiverStatus _receiverStatus;
        protected HubConnection _receiverHub;

        protected string PlayerState { get; set; } = "Disconnected";
        protected bool IsMediaInitialized { get; set; }
        protected IEnumerable<QueueItem> Queue { get; set; } = Array.Empty<QueueItem>();
        protected bool IsEditingQueue { get; set; }
        protected string MediaTypeIconClass { get; set; } = "cast-off";
        protected string Title { get; set; }
        protected string Subtitle { get; set; }
        protected string ImageUrl { get; set; }
        protected TimeSpan Runtime { get; set; }
        protected TimeSpan CurrentTime { get; set; }
        protected double PlaybackRate { get; set; }
        protected float Volume { get; set; }
        protected bool IsMuted { get; set; }
        protected RepeatMode? Repeat { get; set; }

        private bool _istableInitialized = false;
        private IEnumerable<TableQueueItem> _currentTableQueueItems = Array.Empty<TableQueueItem>();
        private bool _showingMessage = false;
        private bool disposedValue;
        private readonly System.Timers.Timer _timer = new() { AutoReset = true, Interval = 1000 };

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                ReceiverStatus receiverStatus = await _receiverHub.InvokeAsync<ReceiverStatus>("GetStatus", Name);
                await UpdateStatus(receiverStatus);
                _currentTableQueueItems = GetTableQueueItems(receiverStatus);
                await JSRuntime.InvokeVoidAsync("InitializeTable", Name, DotNetObjectReference.Create(this), _currentTableQueueItems);
                _istableInitialized = true;
            }
        }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            _timer.Elapsed += async (_, _) => 
            { 
                CurrentTime = CurrentTime.Add(TimeSpan.FromSeconds(1));
                await InvokeAsync(StateHasChanged);
            };

            _receiverHub = new HubConnectionBuilder()
                .WithUrl(new UriBuilder(Configuration["Services:Homehook:ServiceUri"]) { Path = "receiverhub" }.Uri, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(Configuration["Services:Homehook:AccessToken"]);
                })
                .AddNewtonsoftJsonProtocol()
                .WithAutomaticReconnect()
                .Build();

            _receiverHub.On<string, ReceiverStatus>("ReceiveStatus", async (receiverName, receiverStatus) =>
            {
                if (Name.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase))
                {
                    while (_showingMessage)
                        await Task.Delay(500);

                    await UpdateStatus(receiverStatus);
                    await InvokeAsync(StateHasChanged);
                }
            });

            _receiverHub.On<string, string>("ReceiveMessage", async (receiverName, message) =>
            {
                if (Name.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase))
                {
                    _showingMessage = true;

                    PlayerState = "Error";
                    Title = message;
                    Subtitle = string.Empty;
                    IsMediaInitialized = false;          
                    
                    await InvokeAsync(StateHasChanged);

                    await Task.Delay(5000);
                    _showingMessage = false;
                }
            });

            await _receiverHub.StartAsync();
        }

        private async Task UpdateStatus(ReceiverStatus receiverStatus)
        {
            PlayerState = receiverStatus.CurrentMediaStatus?.PlayerState?.ToLower()?.FirstCharToUpper() ?? "Disconnected";
            IsMediaInitialized = receiverStatus.IsMediaInitialized;
            CurrentTime = TimeSpan.FromSeconds(receiverStatus.CurrentMediaStatus?.CurrentTime ?? 0);
            Runtime = TimeSpan.FromSeconds(receiverStatus.CurrentMediaInformation?.Duration ?? 0);
            PlaybackRate = receiverStatus.CurrentMediaStatus?.PlaybackRate ?? 1;
            Volume = receiverStatus.Volume;
            IsMuted = receiverStatus.IsMuted;
            Repeat = receiverStatus.CurrentMediaStatus?.RepeatMode;
            Queue = receiverStatus.Queue;

            MediaMetadata mediaMetadata = receiverStatus.CurrentMediaInformation?.Metadata;
            Title = GetTitle(mediaMetadata);
            Subtitle = GetSubtitle(mediaMetadata);

            switch (mediaMetadata?.MetadataType)
            {
                case MetadataType.Default:
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "folder-multiple-image";
                    break;
                case MetadataType.Movie:
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "movie";
                    break;
                case MetadataType.TvShow:
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "television";
                    break;
                case MetadataType.Music:
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "music";
                    break;
                case MetadataType.Photo:
                    ImageUrl = receiverStatus.CurrentMediaStatus?.Media?.ContentId;
                    MediaTypeIconClass = "image-multiple";
                    break;
                default:
                    MediaTypeIconClass = "cast-off";
                    break;
            }

            IEnumerable<TableQueueItem> newTableQueueItems = GetTableQueueItems(receiverStatus);

            if (_istableInitialized && 
                !(IsMediaInitialized && !newTableQueueItems.Any()) &&
                !Enumerable.SequenceEqual(newTableQueueItems, _currentTableQueueItems))
            {
                _currentTableQueueItems = newTableQueueItems;
                await JSRuntime.InvokeVoidAsync("UpdateTable", $"{Name}QueueTable", _currentTableQueueItems);
            }

            if (IsMediaInitialized && PlayerState.Equals("Playing", StringComparison.InvariantCultureIgnoreCase))
                _timer.Start();
            else
                _timer.Stop();

            if (PlayerState.Equals("Disconnected", StringComparison.InvariantCultureIgnoreCase))
                IsEditingQueue = false;
        }


        private IEnumerable<TableQueueItem> GetTableQueueItems(ReceiverStatus receiverStatus)
        {
            return Queue.Select(item => new TableQueueItem
            {
                OrderId = (int)(item.OrderId + 1),
                ItemId = (int)item.ItemId,
                Title = GetTitle(item.Media.Metadata),
                Subtitle = GetSubtitle(item.Media.Metadata),
                IsPlaying = item.ItemId == receiverStatus.CurrentMediaStatus?.CurrentItemId,
                Runtime = (double)(item.Media.Duration ?? 0)
            });
        }

        protected static string GetTitle(MediaMetadata mediaMetadata)
        {
            return (mediaMetadata?.MetadataType) switch
            {
                MetadataType.Default => mediaMetadata?.Title ?? string.Empty,
                MetadataType.Movie => mediaMetadata?.Title ?? string.Empty,
                MetadataType.TvShow => mediaMetadata?.Title ?? string.Empty,
                MetadataType.Music => $"{(mediaMetadata?.TrackNumber != null ? $"{mediaMetadata.TrackNumber}. " : string.Empty)}{mediaMetadata?.Title ?? string.Empty}",
                MetadataType.Photo => mediaMetadata?.Title ?? string.Empty,
                _ => string.Empty,
            };
        }

        protected static string GetSubtitle(MediaMetadata mediaMetadata)
        {
            return (mediaMetadata?.MetadataType) switch
            {
                MetadataType.Default => mediaMetadata?.Subtitle ?? string.Empty,
                MetadataType.Movie => mediaMetadata?.Subtitle ?? string.Empty,
                MetadataType.TvShow => mediaMetadata?.SeriesTitle ?? string.Empty,
                MetadataType.Music => $"{mediaMetadata?.AlbumName ?? string.Empty}{(mediaMetadata?.AlbumName == null && mediaMetadata?.AlbumArtist != null ? mediaMetadata.AlbumArtist : mediaMetadata?.AlbumArtist != null ? $" ({mediaMetadata.AlbumArtist})" : string.Empty)}",
                MetadataType.Photo => mediaMetadata?.Artist ?? string.Empty,
                _ => string.Empty,
            };
        }

        #region Commands

        protected async Task SeekClick(MouseEventArgs mouseEventArgs)
        {
            double width = await JSRuntime.InvokeAsync<double>("GetElementWidth", ProgressBar);
            double seekSeconds = mouseEventArgs.OffsetX / width * Runtime.TotalSeconds;
            await _receiverHub.InvokeAsync("Seek", Name, seekSeconds);
        }

        protected async Task PlayPauseClick(MouseEventArgs _)
        {
            if (PlayerState.Equals("Playing", StringComparison.InvariantCultureIgnoreCase))
                await _receiverHub.InvokeAsync("Pause", Name);
            else
                await _receiverHub.InvokeAsync("Play", Name);
        }

        protected async Task StopClick(MouseEventArgs _) =>
            await _receiverHub.InvokeAsync("Stop", Name);

        protected async Task RewindClick(MouseEventArgs _) =>
            await _receiverHub.InvokeAsync("Seek", Name, CurrentTime.TotalSeconds - 10);

        protected async Task FastForwardClick(MouseEventArgs _) =>
            await _receiverHub.InvokeAsync("Seek", Name, CurrentTime.TotalSeconds + 10);

        protected async Task PreviousClick(MouseEventArgs _) =>
            await _receiverHub.InvokeAsync("Previous", Name);

        protected async Task NextClick(MouseEventArgs _) =>
            await _receiverHub.InvokeAsync("Next", Name);

        protected async Task SetRepeatMode(RepeatMode repeatMode) =>
            await _receiverHub.InvokeAsync("ChangeRepeatMode", Name, repeatMode);

        protected async Task SetPlaybackRate(double playBackRate) =>
            await _receiverHub.InvokeAsync("SetPlaybackRate", Name, playBackRate);

        protected async Task SetVolume(ChangeEventArgs changeEventArgs) =>
            await _receiverHub.InvokeAsync("SetVolume", Name, float.Parse(changeEventArgs.Value.ToString()));

        protected async Task ToggleMute(MouseEventArgs _) =>
            await _receiverHub.InvokeAsync("ToggleMute", Name);

        protected async Task ToggleEditingQueue(MouseEventArgs _)
        {
            IsEditingQueue = !IsEditingQueue;
            if (IsEditingQueue)
                await JSRuntime.InvokeVoidAsync("ChangeElementHeight", CardReceiverReference, 750);
            else
                await JSRuntime.InvokeVoidAsync("ChangeElementHeight", CardReceiverReference, 250);
        }

        public async Task LaunchQueue(MouseEventArgs _)
        {
            string searchTerm = await JSRuntime.InvokeAsync<string>("prompt", $"Please enter Jellyfin search term to find items for {Name}'s queue.", "");
            if (!string.IsNullOrWhiteSpace(searchTerm))
                await _receiverHub.InvokeAsync("LaunchQueue", Name, searchTerm);
        }

        [JSInvokable]
        public async Task PlayItem(int itemId) =>
            await _receiverHub.InvokeAsync("ChangeCurrentMedia", Name, itemId);

        [JSInvokable]
        public async Task UpItems(IEnumerable<int> itemIds) =>
            await _receiverHub.InvokeAsync("UpQueue", Name, itemIds);

        [JSInvokable]
        public async Task DownItems(IEnumerable<int> itemIds) =>
            await _receiverHub.InvokeAsync("DownQueue", Name, itemIds);

        [JSInvokable]
        public async Task AddItems(string searchTerm, int? insertBefore = null) =>
            await _receiverHub.InvokeAsync("InsertQueue", Name, searchTerm, insertBefore);  

        [JSInvokable]
        public async Task RemoveItems(IEnumerable<int> itemIds) =>
            await _receiverHub.InvokeAsync("RemoveQueue", Name, itemIds);

        #endregion

        #region IDisposable Implementation

        protected async virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    await _receiverHub.DisposeAsync();
                    _timer.Dispose();
                }

                Queue = null;

                disposedValue = true;
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