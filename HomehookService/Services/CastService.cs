using GoogleCast;
using GoogleCast.Models.Media;
using Homehook.Hubs;
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
        
        private bool _isRefreshingReceivers = false;
        private CancellationTokenSource findReceiverDelayCancellationTokenSource;

        public ObservableCollection<IReceiver> Receivers { get; set; } = new();

        public ObservableCollection<ReceiverService> ReceiverServices { get; } = new();

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
                        foreach (IReceiver newReceiver in await new DeviceLocator().FindReceiversAsync())
                        {
                            if (!Receivers.Any(receiver => receiver.FriendlyName.Equals(newReceiver.FriendlyName, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                Receivers.Add(newReceiver);
                                RegisterReceiverService(new(newReceiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));
                            }
                        }
                    }
                    finally
                    {
                        findReceiverDelayCancellationTokenSource = new();
                        findReceiverDelayCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(10));
                    }

                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // TODO: decide to stop session progress or let it continue.

            return Task.CompletedTask;
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

        public async Task RefreshReceiverServices(bool refreshReceivers = false)
        {
            await WaitForRefresh();

            try
            {
                _isRefreshingReceivers = true;

                if (refreshReceivers)
                {
                    findReceiverDelayCancellationTokenSource.Cancel();
                    while (findReceiverDelayCancellationTokenSource.IsCancellationRequested)
                        await Task.Delay(250);
                }
                else
                {
                    ReceiverService[] currentReceiverServices = ReceiverServices.ToArray();
                    ReceiverServices.Clear();
                    foreach (ReceiverService oldReceiverService in currentReceiverServices)
                    {
                        oldReceiverService.Disposed -= ReceiverDisposed;
                        oldReceiverService?.Dispose();
                    }

                    foreach(IReceiver receiver in Receivers)
                        RegisterReceiverService(new(receiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));                    
                }
            }
            finally
            {
                _isRefreshingReceivers = false;
            }
        }

        private void RegisterReceiverService(ReceiverService newReceiverService)
        {
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
            RegisterReceiverService(new(receiver, _configuration["Services:Google:ApplicationId"], _jellyfinService, _receiverHub, _loggingService));

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
                    await Task.Delay(5000);
                    await receiverService.InitializeQueueAsync(items);                    
                }
                else
                    await receiverService.InitializeQueueAsync(items);
            }
            else
                throw new KeyNotFoundException("The given receiver name cannot be found!");
        }
    }
}