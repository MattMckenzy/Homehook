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

        CancellationTokenSource RefreshDevicesCancellationTokenSource = new();

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

                            hubConnection.On<Device>("UpdateDevice", async (device) =>
                                await UpdateDevice(device));

                            await hubConnection.StartAsync(cancellationToken);
                            Device device = await hubConnection.InvokeAsync<Device>("GetDevice", cancellationToken);

                            if (DeviceConnections.TryAdd(deviceConfiguration.Name,
                                new DeviceConnection
                                {
                                    Device = device,
                                    HubConnection = hubConnection
                                }))                         
                                newDevicesAdded.Add(deviceConfiguration.Name);
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

        private async Task UpdateDevice(Device device)
        {
            if (DeviceConnections.TryGetValue(device.Name, out DeviceConnection? deviceConnection) &&
                deviceConnection != null) 
            {
                deviceConnection.Device = device;
                deviceConnection.InvokeDeviceUpdatedAsync();

                MediaItem? currentMedia = device.CurrentMedia;
                if (currentMedia == null)
                    return;

                switch (device.DeviceStatus)
                {
                    case DeviceStatus.Playing:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia, ProgressEvents.TimeUpdate), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Paused:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia, ProgressEvents.TimeUpdate), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Pausing:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia, ProgressEvents.Pause), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Unpausing:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia, ProgressEvents.Unpause), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Starting:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Finishing:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia, ProgressEvents.TimeUpdate), currentMedia.User, device.Name, ServiceName, device.Version);
                        break;
                    case DeviceStatus.Stopping:
                        await JellyfinService.UpdateProgress(GetProgress(device, currentMedia), currentMedia.User, device.Name, ServiceName, device.Version, true);
                        break;
                    case DeviceStatus.Stopped:
                    default:
                        break;
                    }
            }
        }

        private static Progress GetProgress(Device device, MediaItem media, ProgressEvents? progressEvent = null)
        {
            Progress returningProgress = new()
            {
                EventName = progressEvent,
                ItemId = media.Id,
                MediaSourceId = media.Id
            };

            if (device.DeviceStatus != DeviceStatus.Stopping || device.DeviceStatus != DeviceStatus.Stopped)
            {
                returningProgress.PositionTicks = (long)(device.DeviceStatus == DeviceStatus.Starting ? 0 : 
                    device.DeviceStatus == DeviceStatus.Finishing ? media.Runtime * 10000000f : 
                    device.CurrentTime * 10000000f);
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