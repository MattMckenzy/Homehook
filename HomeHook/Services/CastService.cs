using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models;
using HomeHook.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Threading;

namespace HomeHook
{
    public class CastService : IHostedService
    {
        #region Injections

        private JellyfinService JellyfinService { get; }
        private SearchService SearchService { get; }
        private LoggingService<CastService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Public Properties

        public ConcurrentDictionary<string, DeviceService?> DeviceServices { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public event EventHandler? DeviceServicesUpdated;

        #endregion

        #region Private Variables

        private CancellationTokenSource RefreshDevicesCancellationTokenSource { get; set; } = new();

        #endregion

        #region Constructor

        public CastService(JellyfinService jellyfinService, SearchService searchService, LoggingService<CastService> loggingService, IConfiguration configuration)
        {
            JellyfinService = jellyfinService;
            SearchService = searchService;
            LoggingService = loggingService;
            Configuration = configuration;
        }

        #endregion

        #region Service Control Methods

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (DeviceConfiguration deviceConfiguration in Configuration.GetSection("Services:HomeHook:Devices").Get<DeviceConfiguration[]>() ?? Array.Empty<DeviceConfiguration>())
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(deviceConfiguration.Name) ||
                                deviceConfiguration.Name.Any(character => !char.IsLetter(character)))
                            {
                                await LoggingService.LogError("Invalid device name", $"The device name given: \"{deviceConfiguration.Name}\" at \"{deviceConfiguration.Address}\" is not valid! Give the device a unique name with only letters.");
                                continue;
                            }

                            if (!Uri.IsWellFormedUriString(deviceConfiguration.Address, UriKind.Absolute))
                            {
                                await LoggingService.LogError("Invalid address", $"The device address given: \"{deviceConfiguration.Address}\" with name \"{deviceConfiguration.Name}\" is not valid! Supply the device's valid, absolute host address.");
                                continue;
                            }

                            if (DeviceServices.TryGetValue(deviceConfiguration.Name, out DeviceService? deviceService) && deviceService != null)
                                continue;

                            HubConnection hubConnection = new HubConnectionBuilder()
                            .WithUrl(new UriBuilder(deviceConfiguration.Address)
                            { Path = "devicehub" }.Uri, options =>
                            {
                                options.AccessTokenProvider = () => Task.FromResult(deviceConfiguration.AccessToken);
                            })
                            .AddNewtonsoftJsonProtocol()
                            .WithAutomaticReconnect(new DeviceRetryPolicy<CastService>(deviceConfiguration, LoggingService))
                            .Build();

                            hubConnection.Reconnecting += async (Exception? exception) => await DeviceConnectionReconnecting(deviceConfiguration.Name, exception);
                            hubConnection.Reconnected += async (string? message) => await DeviceConnectionReconnected(deviceConfiguration.Name, message, hubConnection, cancellationToken);
                            hubConnection.Closed += async (Exception? exception) => await DeviceConnectionClosed(deviceConfiguration.Name, exception);

                            await hubConnection.StartAsync(cancellationToken);

                            await AddOrUpdateDeviceService(deviceConfiguration.Name, hubConnection, cancellationToken);
                        }
                        catch (Exception exception)
                        {
                            await LoggingService.LogDebug("Cast Service Error.", $"Error while connecting to device \"{deviceConfiguration.Name}\": {string.Join("; ", exception.Message, exception.InnerException?.Message)}");

                            if (deviceConfiguration.Name != null)
                                await AddOrUpdateDeviceService(deviceConfiguration.Name);
                        }
                    }

                    RefreshDevicesCancellationTokenSource = new();
                    RefreshDevicesCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        private async Task DeviceConnectionReconnecting(string deviceName, Exception? exception)
        {
            await LoggingService.LogDebug($"{deviceName} reconnecting.", exception?.Message ?? "Connection reconnecting...");            
            await AddOrUpdateDeviceService(deviceName);
        }

        private async Task DeviceConnectionReconnected(string deviceName, string? message, HubConnection hubConnection, CancellationToken cancellationToken)
        {
            await LoggingService.LogDebug($"{deviceName} reconnected.", message ?? "Succesfully reconnected.");
            await AddOrUpdateDeviceService(deviceName, hubConnection, cancellationToken);
        }

        private async Task DeviceConnectionClosed(string deviceName, Exception? exception)
        {
            await LoggingService.LogDebug($"{deviceName} connection closed.", exception?.Message ?? "Connection closed.");
            await AddOrUpdateDeviceService(deviceName);
        }

        public async Task AddOrUpdateDeviceService(string deviceName, HubConnection? hubConnection = null, CancellationToken cancellationToken = default)
        {
            if (hubConnection == null || cancellationToken == default) 
            {
                DeviceServices.AddOrUpdate(deviceName,
                    (_) => {
                        return null;
                    },
                    (name, oldDeviceService) =>
                    {
                        oldDeviceService?.Dispose();
                        return null;
                    });
            }
            else
            {
                DeviceServices.AddOrUpdate(deviceName,
                    (_) => {
                        return CreateDeviceService(hubConnection, cancellationToken).GetAwaiter().GetResult();
                    },
                    (name, oldDeviceService) =>
                    {
                        oldDeviceService?.Dispose();
                        return CreateDeviceService(hubConnection, cancellationToken).GetAwaiter().GetResult();
                    });
            }

            await LoggingService.LogDebug("New device!", $"Found and registered new device: {deviceName}.");
            DeviceServicesUpdated?.Invoke(this, EventArgs.Empty);
        }

        private async Task<DeviceService> CreateDeviceService(HubConnection hubConnection, CancellationToken cancellationToken)
        {
            Device device = await hubConnection.InvokeAsync<Device>("GetDevice", cancellationToken);

            DeviceService deviceService = new(JellyfinService, SearchService)
            {
                Device = device,
                HubConnection = hubConnection
            };

            RegisterCallbacks(hubConnection, deviceService);

            return deviceService;
        }

        private static void RegisterCallbacks(HubConnection hubConnection, DeviceService deviceService)
        {
            hubConnection.On(DeviceHubConstants.DeviceStatusUpdateMethod, async (DeviceStatus deviceStatus) =>
                await deviceService.UpdateDeviceStatus(deviceStatus));

            hubConnection.On(DeviceHubConstants.StatusMessageUpdateMethod, async (string? statusMessage) =>
                await deviceService.UpdateStatusMessage(statusMessage));

            hubConnection.On(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, async (string? currentMediaItemId) =>
                await deviceService.UpdateCurrentMediaItemId(currentMediaItemId));

            hubConnection.On(DeviceHubConstants.CurrentTimeUpdateMethod, async (double currentTime) =>
                await deviceService.UpdateCurrentTime(currentTime));

            hubConnection.On(DeviceHubConstants.StartTimeUpdateMethod, async (double startTime) =>
                await deviceService.UpdateStartTime(startTime));

            hubConnection.On(DeviceHubConstants.RepeatModeUpdateMethod, async (RepeatMode repeatMode) =>
                await deviceService.UpdateRepeatMode(repeatMode));

            hubConnection.On(DeviceHubConstants.VolumeUpdateMethod, async (float volume) =>
                await deviceService.UpdateVolume(volume));

            hubConnection.On(DeviceHubConstants.IsMutedUpdateMethod, async (bool isMuted) =>
                await deviceService.UpdateIsMuted(isMuted));

            hubConnection.On(DeviceHubConstants.PlaybackRateUpdateMethod, async (float playbackRate) =>
                await deviceService.UpdatePlaybackRate(playbackRate));

            hubConnection.On(DeviceHubConstants.MediaItemsAddMethod, async (List<MediaItem> mediaItems, bool launch, string? insertBeforeMediaItemId) =>
                await deviceService.UpdateAdddedMediaItems(mediaItems, launch, insertBeforeMediaItemId));

            hubConnection.On(DeviceHubConstants.MediaItemsRemoveMethod, async (IEnumerable<string> mediaItemIds) =>
                await deviceService.UpdateRemovedMediaItems(mediaItemIds));

            hubConnection.On(DeviceHubConstants.MediaItemsMoveUpMethod, async (IEnumerable<string> mediaItemIds) =>
                await deviceService.MoveUpMediaItems(mediaItemIds));

            hubConnection.On(DeviceHubConstants.MediaItemsMoveDownMethod, async (IEnumerable<string> mediaItemIds) =>
                await deviceService.MoveDownMediaItems(mediaItemIds));

            hubConnection.On(DeviceHubConstants.MediaItemsClearMethod, async () =>
                await deviceService.ClearMediaItems());

            hubConnection.On(DeviceHubConstants.MediaQueueOrderUpdateMethod, async (IEnumerable<string> mediaItemIds) =>
                await deviceService.OrderMediaQueue(mediaItemIds));

            hubConnection.On(DeviceHubConstants.MediaItemCacheUpdateMethod, async (string mediaItemId, CacheStatus cacheStatus, double cacheRatio) =>
                await deviceService.UpdateMediaItemCache(mediaItemId, cacheStatus, cacheRatio));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (DeviceService deviceService in DeviceServices.Values.Where(deviceService => deviceService != null).Cast<DeviceService>())
                deviceService.Dispose();

            DeviceServices.Clear();

            await LoggingService.LogDebug("Cast Service stopping.", DateTime.Now.ToString());
        }

        #endregion

        #region Device Control Methods

        public async Task<(bool, DeviceService?)> TryGetDeviceService(string deviceName)
        {
            if (!DeviceServices.TryGetValue(deviceName, out DeviceService? deviceService) || deviceService == null)
            {
                await LoggingService.LogError("Device not found!", $"The given device name \"{deviceName}\" cannot be found!!");
                return (false, null);
            }
            else if (deviceService.HubConnection.State != HubConnectionState.Connected)
            {
                CancellationToken waitForConnectionCancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
                while (!waitForConnectionCancellationToken.IsCancellationRequested &&
                    (deviceService.HubConnection.State == HubConnectionState.Reconnecting || deviceService.HubConnection.State == HubConnectionState.Connecting))
                    await Task.Delay(TimeSpan.FromSeconds(1));

                if (deviceService.HubConnection.State != HubConnectionState.Connected)
                {
                    await LoggingService.LogError("Device not connected.", $"The given device \"{deviceService.Device.Name}\" at \"{deviceService.Device.Address}\" is not connected, please verify its status and try again.");
                    return (false, null);
                }
            }

            return (true, deviceService);
        }

        #endregion
    }
}