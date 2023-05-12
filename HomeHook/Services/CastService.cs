using HomeHook.Common.Models;
using HomeHook.Common.Services;
using HomeHook.Models;
using HomeHook.Models.Jellyfin;
using HomeHook.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;

namespace HomeHook
{
    public class CastService : IHostedService
    {
        private const string ServiceName = "HomeCast";

        private JellyfinService JellyfinService { get; }
        private LoggingService<CastService> Logger { get; }
        private IConfiguration Configuration { get; }

        public ConcurrentDictionary<string, DeviceConnection> DeviceConnections { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);
        public event EventHandler? DeviceConnectionsUpdated;

        public CastService(JellyfinService jellyfinService, LoggingService<CastService> loggingService, IConfiguration configuration)
        {
            JellyfinService = jellyfinService;
            Logger = loggingService;
            Configuration = configuration;
        }

        private readonly List<CancellationTokenSource> PeriodicTimerCancellationTokenSources = new();
        private CancellationTokenSource RefreshDevicesCancellationTokenSource { get; set; } = new();

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
                                await Logger.LogError("Invalid device name", $"The device name given: \"{deviceConfiguration.Name}\" at \"{deviceConfiguration.Address}\" is not valid! Give the device a unique name with only letters.");
                                continue;
                            }

                            if (!Uri.IsWellFormedUriString(deviceConfiguration.Address, UriKind.Absolute))
                            {
                                await Logger.LogError("Invalid address", $"The device address given: \"{deviceConfiguration.Address}\" with name \"{deviceConfiguration.Name}\" is not valid! Supply the device's valid, absolute host address.");
                                continue;
                            }

                            if (DeviceConnections.TryGetValue(deviceConfiguration.Name, out _))
                                continue;

                            HubConnection hubConnection = new HubConnectionBuilder()
                            .WithUrl(new UriBuilder(deviceConfiguration.Address)
                            { Path = "devicehub" }.Uri, options =>
                            {
                                options.AccessTokenProvider = () => Task.FromResult(deviceConfiguration.AccessToken);
                            })
                            .ConfigureLogging(logging =>
                            {
                                // Log to the Output Window
                                logging.AddDebug();
                                // This will set ALL logging to Debug level
                                logging.SetMinimumLevel(LogLevel.Debug);
                            })
                            .AddNewtonsoftJsonProtocol()
                            .WithAutomaticReconnect(new DeviceRetryPolicy<CastService>(deviceConfiguration, Logger))
                            .Build();

                            await hubConnection.StartAsync(cancellationToken);
                            Device device = await hubConnection.InvokeAsync<Device>("GetDevice", cancellationToken);

                            DeviceConnection deviceConnection = new()
                            {
                                Device = device,
                                HubConnection = hubConnection
                            };
                            if (DeviceConnections.TryAdd(deviceConfiguration.Name, deviceConnection))
                            {
                                newDevicesAdded.Add(deviceConfiguration.Name);
                                StartDeviceTick(deviceConnection);
                            }

                            hubConnection.On<Device>("UpdateDevice", (device) =>
                                UpdateDevice(device));
                        }

                        if (newDevicesAdded.Any())
                        {
                            await Logger.LogDebug("Refreshed receivers.", $"Refreshed devices and found {newDevicesAdded.Count} new devices ({string.Join(", ", newDevicesAdded)}).");
                            DeviceConnectionsUpdated?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    catch(Exception exception)
                    {
                        await Logger.LogDebug("Cast Service Error.", $"Error while connecting to devices: {string.Join("; ", exception.Message, exception.InnerException?.Message)}");
                    }
                    finally
                    {
                        RefreshDevicesCancellationTokenSource = new();
                        RefreshDevicesCancellationTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));
                    }

                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (CancellationTokenSource periodicTimerCancellationTokenSource in PeriodicTimerCancellationTokenSources)
                periodicTimerCancellationTokenSource.Cancel();

            foreach (HubConnection hubConnection in DeviceConnections.Values.Select(deviceConnection => deviceConnection.HubConnection))
                await hubConnection.DisposeAsync();

            DeviceConnections.Clear();

            await Logger.LogDebug("Cast Service stopping.", DateTime.Now.ToString());
        }

        public async Task<(bool, DeviceConnection?)> TryGetDevice(string deviceName)
        {
            if (!DeviceConnections.TryGetValue(deviceName, out DeviceConnection? deviceConnection) || deviceConnection == null)
            {
                await Logger.LogError("Jellyfin Session Start", "The given device name cannot be found!!");
                return (false, null);
            }
            else if (deviceConnection.HubConnection.State != HubConnectionState.Connected)
            {
                CancellationToken waitForConnectionCancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
                while (waitForConnectionCancellationToken.IsCancellationRequested &&
                    (deviceConnection.HubConnection.State == HubConnectionState.Reconnecting || deviceConnection.HubConnection.State == HubConnectionState.Connecting))
                    await Task.Delay(TimeSpan.FromSeconds(1));

                if (deviceConnection.HubConnection.State != HubConnectionState.Connected)
                {
                    await Logger.LogError("Jellyfin Session Start", $"The given device \"{deviceConnection.Device.Name}\" at \"{deviceConnection.Device.Address}\" is not connected, please verify its status and try again.");
                    return (false, null);
                }
            }

            return (true, deviceConnection);
        }

        public async Task StartJellyfinSession(string deviceName, List<MediaItem> items)
        {
            if (!items.Any())
                await Logger.LogError("Jellyfin Session Start", "There are no items to initialize!");

            (bool gotDevice, DeviceConnection? deviceConnection) = await TryGetDevice(deviceName);
            if (gotDevice && deviceConnection != null)
            {
                _ = Task.Run(async () =>
                {
                    await deviceConnection.HubConnection.InvokeAsync("LaunchQueue", items);

                    await Logger.LogDebug("Started Jellyfin Session", $"Succesfully initialized media on device: \"{deviceConnection.Device.Name}\"");
                });
            }
        }

        private async void StartDeviceTick(DeviceConnection deviceConnection)
        {
            CancellationTokenSource PeriodicTimerCancellationTokenSource = new();
            PeriodicTimerCancellationTokenSources.Add(PeriodicTimerCancellationTokenSource);
            PeriodicTimer periodicTimer = new(TimeSpan.FromSeconds(1));
            while (await periodicTimer.WaitForNextTickAsync(PeriodicTimerCancellationTokenSource.Token))
            {
                if (deviceConnection.Device.DeviceStatus == DeviceStatus.Playing)
                {
                    double newTime = deviceConnection.CurrentTime +deviceConnection.Device.PlaybackRate;
                    if (newTime <= deviceConnection.Device.CurrentMedia?.Runtime)
                    {
                        deviceConnection.CurrentTime = newTime;
                        deviceConnection.InvokeDeviceUpdatedAsync();
                    }
                }
            }
        }

        private void UpdateDevice(Device device)
        {
            if (DeviceConnections.TryGetValue(device.Name, out DeviceConnection? deviceConnection) &&
                deviceConnection != null) 
            {
                deviceConnection.Device = device;

                MediaItem? currentMedia = device.CurrentMedia;
                if (currentMedia == null)
                    return;

                switch (device.DeviceStatus)
                {
                    case DeviceStatus.Playing:
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia, ProgressEvents.TimeUpdate), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Paused:
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia, ProgressEvents.TimeUpdate), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Pausing:
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia, ProgressEvents.Pause), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Unpausing:
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia, ProgressEvents.Unpause), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Starting:
                        deviceConnection.CurrentTime = 0;
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Finishing:
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia, ProgressEvents.TimeUpdate), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Stopping:
                        deviceConnection.CurrentTime = 0;
                        _ = JellyfinService.UpdateProgress(GetProgress(deviceConnection, currentMedia), currentMedia.User, device.Name, ServiceName, device.Version, true);
                        break;
                    case DeviceStatus.Stopped:
                        deviceConnection.CurrentTime = 0;
                        break;
                    default:
                        break;
                }

                deviceConnection.InvokeDeviceUpdatedAsync();
            }
        }

        private static Progress GetProgress(DeviceConnection deviceConnection, MediaItem media, ProgressEvents? progressEvent = null)
        {
            Progress returningProgress = new()
            {
                EventName = progressEvent,
                ItemId = media.Id,
                MediaSourceId = media.Id
            };

            Device device = deviceConnection.Device;
            if (device.DeviceStatus != DeviceStatus.Stopping || device.DeviceStatus != DeviceStatus.Stopped)
            {
                returningProgress.PositionTicks = (long)(device.DeviceStatus == DeviceStatus.Starting ? 0 : 
                    device.DeviceStatus == DeviceStatus.Finishing ? media.Runtime * 10000000f :
                    deviceConnection.CurrentTime * 10000000f);
                returningProgress.VolumeLevel = Convert.ToInt32(device.Volume * 100);
                returningProgress.IsMuted = device.IsMuted;
                returningProgress.IsPaused = device.DeviceStatus == DeviceStatus.Pausing || device.DeviceStatus == DeviceStatus.Paused;
                returningProgress.PlaybackRate = device.PlaybackRate;
                returningProgress.PlayMethod = PlayMethod.DirectPlay;                
            }

            return returningProgress;
        }
    }
}