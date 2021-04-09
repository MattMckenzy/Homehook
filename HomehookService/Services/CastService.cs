using GoogleCast;
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
                RegisterReceiverService(new (receiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));            
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
                    RegisterReceiverService(new(newReceiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));                
            }

            foreach (ReceiverService oldReceiverService in ReceiverServices.ToArray())
            {
                IReceiver newReceiver =
                    newReceivers.FirstOrDefault(receiver => receiver.Id.Equals(oldReceiverService.Receiver.Id, StringComparison.InvariantCultureIgnoreCase));

                if (newReceiver == null)
                    ReceiverServices.Remove(oldReceiverService);
            }
        }

        private void RegisterReceiverService(ReceiverService newReceiverService)
        {
            newReceiverService.Disposed += async (object sender, EventArgs eventArgs) =>
            {
                ReceiverServices.Remove((ReceiverService)sender);
                Thread.Sleep(1000);
                await RefreshReceivers();
            };
            ReceiverServices.Add(newReceiverService);
        }

        public async Task StartJellyfinSession(Phrase phrase, IEnumerable<QueueItem> items)
        {
            ReceiverService receiverService = await GetReceiverService(phrase.Device);

            if (receiverService != null)
                await receiverService.InitializeQueueAsync(items);
        }      
    }
}