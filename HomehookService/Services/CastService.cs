using GoogleCast;
using GoogleCast.Models.Media;
using Homehook.Hubs;
using Homehook.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

        private bool _isRefreshingReceivers = false;
        private CancellationTokenSource findReceiverDelayCancellationTokenSource;

        public ObservableCollection<IReceiver> Receivers { get; set; } = new();

        public ObservableCollection<ReceiverService> ReceiverServices { get; } = new();

        private ConcurrentDictionary<string, IEnumerable<QueueItem>> _awaitingItems { get; } = new();

        public CastService(JellyfinService jellyfinService, LoggingService<CastService> loggingService, IHubContext<ReceiverHub> receiverHub, IConfiguration configuration)
        {
            _jellyfinService = jellyfinService;
            _loggingService = loggingService;
            _configuration = configuration;
            _receiverHub = receiverHub;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        List<string> newReceivers = new();
                        foreach (IReceiver newReceiver in await new DeviceLocator().FindReceiversAsync())
                        {
                            if (!Receivers.Any(receiver => receiver.FriendlyName.Equals(newReceiver.FriendlyName, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                Receivers.Add(newReceiver);
                                RegisterReceiverService(new(newReceiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));
                                newReceivers.Add(newReceiver.FriendlyName);
                            }
                        }
                        if (newReceivers.Any())
                            await _loggingService.LogDebug("Refreshed receivers.", $"Refreshed receivers and found {newReceivers.Count} new receivers ({string.Join(", ", newReceivers)}).");
                    }
                    finally
                    {
                        findReceiverDelayCancellationTokenSource = new();
                        findReceiverDelayCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
                    }

                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // TODO: decide to stop session progress or let it continue.
            await _loggingService.LogDebug("Cast Service stopping.", DateTime.Now.ToString());
        }

        public async Task<ReceiverService> GetReceiverService(string receiverName)
        {
            await WaitForRefresh();

            ReceiverService returningReceiverService =
                ReceiverServices.FirstOrDefault(receiverServices => receiverServices.Receiver.FriendlyName.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase));

            if (returningReceiverService == null)
                await _loggingService.LogError($"{receiverName} not found.", $"Requested receiver {receiverName} is not available. Please make sure device is connected and try again.");

            return returningReceiverService;
        }

        public async Task WaitForRefresh()
        {
            if (_isRefreshingReceivers)
            {
                while (_isRefreshingReceivers)
                    await Task.Delay(250);
                return;
            }
        }

        public async Task RefreshReceiverServices()
        {
            await WaitForRefresh();

            try
            {
                _isRefreshingReceivers = true;

                findReceiverDelayCancellationTokenSource.Cancel();
                while (findReceiverDelayCancellationTokenSource.IsCancellationRequested)
                    await Task.Delay(250);
            }
            finally
            {
                _isRefreshingReceivers = false;
            }
        }

        private void RegisterReceiverService(ReceiverService newReceiverService)
        {
            Debug.WriteLine($"Registering {newReceiverService.Receiver.FriendlyName}");
            newReceiverService.Disposed += ReceiverDisposed;
            ReceiverServices.Add(newReceiverService);
        }

        private async void ReceiverDisposed(object sender, EventArgs eventArgs)
        {
            _isRefreshingReceivers = true;

            IReceiver receiver = ((ReceiverService)sender).Receiver;
            ReceiverServices.Remove((ReceiverService)sender);
            ReceiverServices.Remove(null);
            await Task.Delay(1000);
            ReceiverService newReceiverService = new(receiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService);
            RegisterReceiverService(newReceiverService);

            if (_awaitingItems.TryRemove(receiver.FriendlyName, out IEnumerable<QueueItem> items))
            {
                await Task.Delay(1000);
                await newReceiverService.InitializeQueueAsync(items);
            }

            _isRefreshingReceivers = false;
        }

        public async Task StartJellyfinSession(string receiverName, IEnumerable<QueueItem> items)
        {
            ReceiverService receiverService = await GetReceiverService(receiverName);
            
            if (receiverService != null)
            {
                if (receiverService.IsDifferentApplicationPlaying)
                {
                    await receiverService.StopAsync();
                    _awaitingItems.AddOrUpdate(receiverName, items, (key, oldItems) => items);
                }
                else
                    await receiverService.InitializeQueueAsync(items);
            }
            else
                throw new KeyNotFoundException("The given receiver name cannot be found!");
        }
    }
}