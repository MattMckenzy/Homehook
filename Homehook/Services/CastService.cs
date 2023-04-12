using Homehook.Models;
using Homehook.Models.Jellyfin;
using Homehook.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using WonkCast.Common.Models;

namespace Homehook
{
    public class CastService : IHostedService
    {
        private const string ServiceName = "Homehook";

        private JellyfinService JellyfinService { get; }
        private LoggingService<CastService> Logger { get; }
        private IConfiguration Configuration { get; }

        public ConcurrentDictionary<string, DeviceConnection> DeviceConnections { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

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
                        int newDevices = 0;
                        foreach (DeviceConfiguration deviceConfiguration in Configuration.GetSection("Devices").Get<DeviceConfiguration[]>() ?? Array.Empty<DeviceConfiguration>())
                        {
                            if (string.IsNullOrWhiteSpace(deviceConfiguration.Name) ||
                                deviceConfiguration.Name.Any(character => !char.IsLetter(character)))
                            {
                                await Logger.LogError("Invalid device name", $"The device name given: \"{deviceConfiguration.Name}\" at \"{deviceConfiguration.Address}\" is not valid! Give the device a unique name with only letters.");
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(deviceConfiguration.Address) ||
                                Uri.CheckHostName(deviceConfiguration.Address) == UriHostNameType.Unknown)
                            {
                                await Logger.LogError("Invalid address", $"The device address given: \"{deviceConfiguration.Address}\" with name \"{deviceConfiguration.Name}\" is not valid! Supply the device's valid host address.");
                                continue;
                            }

                            HubConnection hubConnection = new HubConnectionBuilder()
                                .WithUrl(new UriBuilder(deviceConfiguration.Address)
                                { Path = "devicehub" }.Uri, options =>
                                {
                                    options.AccessTokenProvider = () => Task.FromResult(deviceConfiguration.AccessToken);
                                })
                                .AddJsonProtocol()
                                .WithAutomaticReconnect(new DeviceRetryPolicy<CastService>(deviceConfiguration, Logger))
                                .Build();

                            hubConnection.On<Device>("UpdateDevice", (device) =>
                                UpdateDevice(device, hubConnection));

                            await hubConnection.StartAsync();

                            DeviceConnections.AddOrUpdate(deviceConfiguration.Name, (string key) =>
                            {
                                newDevices++;
                                return (
                                    new DeviceConnection
                                    {
                                        Device = new Device
                                        {
                                            Name = key,
                                            Address = deviceConfiguration.Address,
                                        },
                                        HubConnection = hubConnection
                                    });
                            },
                                (string currentKey, DeviceConnection currentDeviceConnection) => currentDeviceConnection);
                        }

                        if (DeviceConnections.Any())
                            await Logger.LogDebug("Refreshed receivers.", $"Refreshed devices and found {DeviceConnections.Count} new devices ({string.Join(", ", newDevices)}).");
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

        public async Task StartJellyfinSession(string deviceName, List<Media> items)
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

        private async Task UpdateDevice(Device device, HubConnection hubConnection)
        {
            DeviceConnections.AddOrUpdate(device.Name,
                (string key) => new DeviceConnection { Device = device, HubConnection = hubConnection },
                (string currentKey, DeviceConnection currentDeviceConnection) => new DeviceConnection { Device = device, HubConnection = currentDeviceConnection.HubConnection });

            switch (device.DeviceStatus)
            {
                case DeviceStatus.Stopped:
                    await JellyfinService.UpdateProgress(GetProgress(device), device.User, device.Name, ServiceName, true);
                    break;
                case DeviceStatus.Playing:
                    await JellyfinService.UpdateProgress(GetProgress(device, ProgressEvents.TimeUpdate), device.User, device.Name, ServiceName);
                    break;
                case DeviceStatus.Paused:
                    await JellyfinService.UpdateProgress(GetProgress(device, ProgressEvents.TimeUpdate), device.User, device.Name, ServiceName);
                    break;
                case DeviceStatus.Pausing:
                    await JellyfinService.UpdateProgress(GetProgress(device, ProgressEvents.Pause), device.User, device.Name, ServiceName);
                    break;
                case DeviceStatus.Unpausing:
                    await JellyfinService.UpdateProgress(GetProgress(device, ProgressEvents.Unpause), device.User, device.Name, ServiceName);
                    break;
                case DeviceStatus.StartingMedia:
                    await JellyfinService.UpdateProgress(GetProgress(device, ProgressEvents.TimeUpdate), device.User, device.Name, ServiceName);
                    break;
                case DeviceStatus.FinishingMedia:
                    await JellyfinService.UpdateProgress(GetProgress(device, ProgressEvents.TimeUpdate), device.User, device.Name, ServiceName);
                    break;
                default:
                    break;
            }
        }

        private Progress GetProgress(Device device, ProgressEvents? progressEvent = null)
        {
            return new Progress
            {
                EventName = progressEvent,
                ItemId = mediaId,
                MediaSourceId = mediaId,
                PositionTicks = device != null ? Convert.ToInt64(device. * 10000000) : null,
                VolumeLevel = Convert.ToInt32(device.Volume * 100),
                IsMuted = device.IsMuted,
                IsPaused = device.DeviceStatus == DeviceStatus.Pausing || device.DeviceStatus == DeviceStatus.Paused,
                PlaybackRate = device.PlaybackSpeed,
                PlayMethod = PlayMethod.DirectPlay
            };
        }
    }
}