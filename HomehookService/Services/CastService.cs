using GoogleCast;
using GoogleCast.Models;
using GoogleCast.Models.Media;
using Homehook.Hubs;
using Homehook.Models.Jellyfin;
using Homehook.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Homehook
{
    public class CastService : IHostedService
    {
        private readonly JellyfinService _jellyfinService;
        private readonly LoggingService<CastService> _loggingService;
        private readonly IHubContext<ReceiverHub> _receiverHub;
        private readonly IConfiguration _configuration;

        public ObservableCollection<ReceiverService> ReceiverServices { get; } = new();

        public CastService(JellyfinService jellyfinService, LoggingService<CastService> loggingService, IHubContext<ReceiverHub> receiverHub, IConfiguration configuration)
        {
            _jellyfinService = jellyfinService;
            _loggingService = loggingService;
            _configuration = configuration;
            _receiverHub = receiverHub;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (IReceiver receiver in await new DeviceLocator().FindReceiversAsync())
                ReceiverServices.Add(new ReceiverService(receiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));            
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // TODO: decide to stop session progress or let it continue.

            return Task.CompletedTask;
        }

        public async Task<ReceiverService> GetReceiverService(string receiverName)
        {
            ReceiverService returningReceiverService =
                ReceiverServices.FirstOrDefault(receiverServices => receiverServices.Receiver.FriendlyName.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase));

            if (returningReceiverService == null)
            {
                await RefreshReceivers();

                returningReceiverService =
                    ReceiverServices.FirstOrDefault(receiverServices => receiverServices.Receiver.FriendlyName.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase));
            }

            if (returningReceiverService == null)
                await _loggingService.LogError($"{receiverName} not found.", $"Requested receiver {receiverName} is not available. Please make sure device is connected and try again.");

            return returningReceiverService;
        }

        public async Task RefreshReceivers()
        {
            IEnumerable<IReceiver> newReceivers = await new DeviceLocator().FindReceiversAsync();

            foreach (IReceiver newReceiver in newReceivers)
            {
                ReceiverService oldReceiverService =
                    ReceiverServices.FirstOrDefault(receiverService => receiverService.Receiver.Id.Equals(newReceiver.Id, StringComparison.InvariantCultureIgnoreCase));

                if (oldReceiverService == null)
                    ReceiverServices.Add(new ReceiverService(newReceiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));
            }

            foreach (ReceiverService oldReceiverService in ReceiverServices.ToArray())
            {
                IReceiver newReceiver =
                    newReceivers.FirstOrDefault(receiver => receiver.Id.Equals(oldReceiverService.Receiver.Id, StringComparison.InvariantCultureIgnoreCase));

                if (newReceiver == null)
                    ReceiverServices.Remove(oldReceiverService);
            }
        }

        public async Task StartJellyfinSession(Phrase phrase, IEnumerable<Item> items)
        {
            ReceiverService receiverService = await GetReceiverService(phrase.Device);

            if (receiverService != null)
                await receiverService.InitializeQueueAsync(ItemsToQueueItems(items, phrase));
        }

        private IEnumerable<QueueItem> ItemsToQueueItems(IEnumerable<Item> items, Phrase phrase)
        {
            return items.Select((item, index) => new QueueItem
            {
                Autoplay = true,
                StartTime = item.UserData.PlaybackPositionTicks != null ? Convert.ToInt32(Math.Round(Convert.ToDecimal(item.UserData.PlaybackPositionTicks / 10000000),0, MidpointRounding.ToZero)) : 0,
                Media = new()
                {
                    ContentType = item.MediaType,
                    ContentId = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Videos/{item.Id}/stream?Static=true&api_key={phrase.UserId}",
                    Duration = item.RunTimeTicks == null ? null : TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds,
                    StreamType = StreamType.Buffered,
                    Metadata = GetMetadata(item, phrase.UserId),
                    CustomData = new Dictionary<string, string>() { { "Id", item.Id }, { "Username", phrase.User } }
                },
            });
        }

        private MediaMetadata GetMetadata(Item item, string userId)
        {
            return item.MediaType switch
            {
                "Video" => new MediaMetadata
                {
                    MetadataType = MetadataType.TvShow,
                    SeriesTitle = string.IsNullOrWhiteSpace(item.SeriesName) ? item.Path : item.SeriesName,
                    Subtitle = item.Name + (string.IsNullOrWhiteSpace(item.Overview) ? string.Empty : $" - {item.Overview}"),
                    Season = item.ParentIndexNumber,
                    Episode = item.IndexNumber,
                    OriginalAirDate = (item.PremiereDate == null ? item.DateCreated : item.PremiereDate).ToString(),
                    Images = new Image[] { new Image { Url = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}" } },
                },
                "Audio" => new MediaMetadata
                {
                    MetadataType = MetadataType.Music,
                    Title = item.Name,
                    AlbumName = string.IsNullOrWhiteSpace(item.Album) ? item.Path : item.Album,
                    AlbumArtist = item.AlbumArtist,
                    Artist = item.Artists == null ? null : string.Join(", ", item.Artists),
                    DiscNumber = item.ParentIndexNumber,
                    TrackNumber = item.IndexNumber,
                    ReleaseDate = item.ProductionYear != null ? new DateTime((int)item.ProductionYear, 12,31).ToString() : null,
                },
                "Photo" => new MediaMetadata
                {
                    MetadataType = MetadataType.Photo,
                    Title = $"{item.Name} ({item.Path})",
                    CreationDateTime = item.DateCreated.ToString(),
                },
                _ => new MediaMetadata
                {
                    MetadataType = MetadataType.Default,
                    Title = item.Name,
                    Subtitle = string.IsNullOrWhiteSpace(item.Overview) ? item.Path : item.Overview,
                    ReleaseDate = (item.PremiereDate == null ? item.DateCreated : item.PremiereDate).ToString(),
                    Images = new Image[] { new Image { Url = $"{_configuration["Services:Jellyfin:ServiceUri"]}/Items/{item.Id}/Images/Primary?api_key={userId}" } },
                }
            };
        }
    }
}