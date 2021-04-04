using GoogleCast.Models.Media;
using HomehookApp.Extensions;
using HomehookCommon.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HomehookApp.Components.Receiver
{
    public class ReceiverBase : ComponentBase
    {
        [Inject]
        public IJSRuntime JSRuntime { get; set; } 

        [Parameter]
        public string Name { get; set; }

        protected ElementReference ProgressBar { get; set; }

        protected ReceiverStatus _receiverStatus;
        protected HubConnection _receiverHub;
        
        protected string PlayerState { get; set; }
        protected bool IsMediaInitialized { get; set; }
        protected string MediaTypeIconClass { get; set; }
        protected string Title { get; set; }
        protected string Subtitle { get; set; }
        protected string ImageUrl { get; set; }
        protected TimeSpan Runtime { get; set; }
        protected TimeSpan CurrentTime  { get; set; }
        protected float Volume { get; set; }
        protected bool IsMuted { get; set; }
        protected RepeatMode? Repeat { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            _receiverHub = new HubConnectionBuilder()
                .WithUrl(new Uri("http://localhost:5000/receiverhub"))
                .WithAutomaticReconnect()
                .Build();

            _receiverHub.On<string, ReceiverStatus>("ReceiveStatus", async (receiverName, receiverStatus) =>
            {
                if (Name.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase))
                {
                    UpdateStatus(receiverStatus);
                    await InvokeAsync(StateHasChanged);
                }              
            });

            await _receiverHub.StartAsync();

            UpdateStatus(await _receiverHub.InvokeAsync<ReceiverStatus>("GetStatus", Name));
        }

        private void UpdateStatus(ReceiverStatus receiverStatus)
        {
            PlayerState = receiverStatus.CurrentMediaStatus?.PlayerState?.ToLower()?.FirstCharToUpper() ?? "Disconnected";
            IsMediaInitialized = receiverStatus.IsMediaInitialized;
            CurrentTime = TimeSpan.FromSeconds(receiverStatus.CurrentMediaStatus?.CurrentTime ?? 0);
            Runtime = TimeSpan.FromSeconds(receiverStatus.CurrentMediaInformation?.Duration ?? 0);
            Volume = receiverStatus.Volume;
            IsMuted = receiverStatus.IsMuted;
            Repeat = receiverStatus.CurrentMediaStatus?.RepeatMode;

            MediaMetadata mediaMetadata = receiverStatus.CurrentMediaInformation?.Metadata;
            switch (mediaMetadata?.MetadataType)
            {
                case MetadataType.Default:
                    Title = mediaMetadata?.Title ?? string.Empty;
                    Subtitle = mediaMetadata?.Subtitle ?? string.Empty;
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "folder-multiple-image";
                    break;
                case MetadataType.Movie: 
                    Title = mediaMetadata?.Title ?? string.Empty;
                    Subtitle = mediaMetadata?.Subtitle ?? string.Empty;
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "movie";
                    break;
                case MetadataType.TvShow:
                    Title = mediaMetadata?.SeriesTitle ?? string.Empty;
                    Subtitle = mediaMetadata?.Subtitle ?? string.Empty;
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "television";
                    break;
                case MetadataType.Music:
                    Title = $"{(mediaMetadata?.TrackNumber != null ? $"{mediaMetadata.TrackNumber}. " : string.Empty )}{mediaMetadata?.Title ?? string.Empty}";
                    Subtitle = $"{mediaMetadata?.AlbumName ?? string.Empty}{(mediaMetadata?.AlbumName == null && mediaMetadata?.AlbumArtist != null ? mediaMetadata.AlbumArtist : mediaMetadata?.AlbumArtist != null ? $" ({mediaMetadata.AlbumArtist})" : string.Empty)}";
                    ImageUrl = mediaMetadata?.Images?.FirstOrDefault()?.Url;
                    MediaTypeIconClass = "music";
                    break;
                case MetadataType.Photo:
                    Title = mediaMetadata?.Title ?? string.Empty;
                    Subtitle = mediaMetadata?.Artist ?? string.Empty;
                    ImageUrl = receiverStatus.CurrentMediaStatus?.Media?.ContentId;
                    MediaTypeIconClass = "image-multiple";
                    break;
                default:
                    Title = string.Empty;
                    Subtitle = string.Empty;
                    MediaTypeIconClass = "cast-off";
                    break;
            }

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
            if (PlayerState.Equals("Paused", StringComparison.InvariantCultureIgnoreCase) || PlayerState.Equals("Stopped", StringComparison.InvariantCultureIgnoreCase))
                await _receiverHub.InvokeAsync("Play", Name);
            else
                await _receiverHub.InvokeAsync("Pause", Name);
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

        #endregion
    }
}
