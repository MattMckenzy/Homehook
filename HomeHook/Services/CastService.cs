using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models;
using HomeHook.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;

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

        public ConcurrentDictionary<string, DeviceService> DeviceServices { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

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
                    try
                    {
                        List<string> newDevicesAdded = new();
                        foreach (DeviceConfiguration deviceConfiguration in Configuration.GetSection("Services:HomeHook:Devices").Get<DeviceConfiguration[]>() ?? Array.Empty<DeviceConfiguration>())
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

                            if (DeviceServices.TryGetValue(deviceConfiguration.Name, out _))
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
                            hubConnection.Reconnected += async (string? message) => await DeviceConnectionReconnected(deviceConfiguration.Name, message);
                            hubConnection.Closed += async (Exception? exception) => await DeviceConnectionClosed(deviceConfiguration.Name, exception);

                            await hubConnection.StartAsync(cancellationToken);
                            Device device = await hubConnection.InvokeAsync<Device>("GetDevice", cancellationToken);

                            DeviceService deviceService = new(JellyfinService, SearchService)
                            {
                                Device = device,                                
                                HubConnection = hubConnection
                            };
                            if (DeviceServices.TryAdd(deviceConfiguration.Name, deviceService))
                            {
                                newDevicesAdded.Add(deviceConfiguration.Name);
                            }

                            hubConnection.On("UpdateDevice", async (Device device) =>
                                await deviceService.UpdateDevice(device));

                            hubConnection.On("UpdateCurrentTime", async (double currentTime) =>
                                await deviceService.UpdateCurrentTime(currentTime));

                            hubConnection.On("UpdateMediaItemCache", async (string mediaItemId, CacheStatus cacheStatus, double cacheRatio) =>
                                await deviceService.UpdateMediaItemCache(mediaItemId, cacheStatus, cacheRatio));
                        }

                        if (newDevicesAdded.Any())
                        {
                            await LoggingService.LogDebug("Refreshed receivers.", $"Refreshed devices and found {newDevicesAdded.Count} new devices ({string.Join(", ", newDevicesAdded)}).");
                            DeviceServicesUpdated?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    catch(Exception exception)
                    {
                        await LoggingService.LogDebug("Cast Service Error.", $"Error while connecting to devices: {string.Join("; ", exception.Message, exception.InnerException?.Message)}");
                    }
                    finally
                    {
                        RefreshDevicesCancellationTokenSource = new();
                        RefreshDevicesCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                    }

                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        private async Task DeviceConnectionReconnecting(string deviceName, Exception? exception)
        {
            await LoggingService.LogDebug($"{deviceName} reconnecting.", exception?.Message ?? "Connection reconnecting...");
        }

        private async Task DeviceConnectionReconnected(string deviceName, string? message)
        {
            await LoggingService.LogDebug($"{deviceName} reconnected.", message ?? "Succesfully reconnected.");
        }

        private async Task DeviceConnectionClosed(string deviceName, Exception? exception)
        {
            await LoggingService.LogDebug($"{deviceName} onnection closed.", exception?.Message ?? "Connection closed.");
            DeviceServices.TryRemove(deviceName, out _);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (DeviceService deviceService in DeviceServices.Values)
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
                await LoggingService.LogError("Jellyfin Session Start", "The given device name cannot be found!!");
                return (false, null);
            }
            else if (deviceService.HubConnection.State != HubConnectionState.Connected)
            {
                CancellationToken waitForConnectionCancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
                while (waitForConnectionCancellationToken.IsCancellationRequested &&
                    (deviceService.HubConnection.State == HubConnectionState.Reconnecting || deviceService.HubConnection.State == HubConnectionState.Connecting))
                    await Task.Delay(TimeSpan.FromSeconds(1));

                if (deviceService.HubConnection.State != HubConnectionState.Connected)
                {
                    await LoggingService.LogError("Jellyfin Session Start", $"The given device \"{deviceService.Device.Name}\" at \"{deviceService.Device.Address}\" is not connected, please verify its status and try again.");
                    return (false, null);
                }
            }

            return (true, deviceService);
        }

        #endregion

    }
}