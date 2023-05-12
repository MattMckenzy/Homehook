using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Reflection;

namespace HomeCast.Services
{
    public class PlayerService
    {
        #region Constants

        private const string DefaultVersion = "1.0.0";
        private const string MPlayerLocation = "mplayer";

        #endregion

        #region Injections

        private IHubContext<DeviceHub> DeviceHubContext { get; }
        private ScriptsProcessor ScriptsProcessor { get; }
        private LoggingService<PlayerService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Public Properties

        public Device Device { get; }

        #endregion

        #region Private Properties

        private Random Random { get; } = new(DateTime.Now.Ticks.GetHashCode());

        private Process? Player { get; set; }
        private bool IsPlayerActive { get { return Player != null && Player.Responding && !Player.HasExited; } }
        private List<string> Arguments { get; } = new() 
        { 
            "-slave",
            "-quiet",
            "-input nodefault-bindings",
            "-codecs-file scripts/codecs.conf",
            "-af scaletempo"
        };

        

        #endregion

        #region Contructor

        public PlayerService(IHubContext<DeviceHub> deviceHubContext, ScriptsProcessor scriptsProcessor, LoggingService<PlayerService> loggingService, IConfiguration configuration)
        {
            DeviceHubContext = deviceHubContext;
            ScriptsProcessor = scriptsProcessor;
            LoggingService = loggingService;
            Configuration = configuration;


            string? name = Configuration["Device:Name"];
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Please define a proper device name in the app settings!");

            string? address = Configuration["Device:Address"];
            if (string.IsNullOrWhiteSpace(address))
                throw new InvalidOperationException("Please define a proper device address in the app settings!");

            if (!Enum.TryParse(Configuration["Device:OS"], out OSType oSType))
                throw new InvalidOperationException("Please define a proper device OS in the app settings!");

            Device = new()
            {
                Name = name,
                Address = address,
                Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? DefaultVersion,
            };

            ScriptsProcessor.DeviceUpdate += UpdateDevice;

            bool videoCapable = Configuration.GetValue<bool>("Device:VideoCapable");

            if (!videoCapable)
                Arguments.Add("-novideo");
        }

        #endregion

        #region Public Methods

        public async Task UpdateClients()
        {
            await DeviceHubContext.Clients.All.SendAsync("UpdateDevice", Device);
        }
        
        public async Task PlayAsync()
        {
            if (IsPlayerActive && Device.DeviceStatus == DeviceStatus.Paused)
            {
                await SetDeviceStatus(DeviceStatus.Unpausing);

                await Player!.StandardInput.WriteLineAsync("pause");

                await SetDeviceStatus(DeviceStatus.Playing);
            }
            else if (Device.CurrentMediaIndex != null && Device.MediaQueue.Any())
            {
                await PlayMedia();
            }
        }

        public async Task StopAsync()
        {
            await SetDeviceStatus(DeviceStatus.Stopping);

            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), null);
            MediaQueueClear();

            await StopMedia();
        }

        public async Task PauseAsync()
        {
            if (IsPlayerActive && Device.DeviceStatus == DeviceStatus.Playing)
            {
                await SetDeviceStatus(DeviceStatus.Pausing);

                await Player!.StandardInput.WriteLineAsync("pause");

                await SetDeviceStatus(DeviceStatus.Paused);
            }
        }

        public async Task NextAsync()
        {
            if (Device.CurrentMediaIndex != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Min((int)Device.CurrentMediaIndex + 1, Device.MediaQueue.Count - 1));
                await PlayMedia();
            }
        }

        public async Task PreviousAsync()
        {
            if (Device.CurrentMediaIndex != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Max((int)Device.CurrentMediaIndex - 1, 0));
                await PlayMedia();
            }
        }

        public async Task SeekAsync(float timeToSeek)
        {
            if (IsPlayerActive)
            {
                await Player!.StandardInput.WriteLineAsync($"seek {Math.Min(Math.Max(timeToSeek, 0), Device.CurrentMedia!.Runtime)} 2");
            }
        }

        public async Task SeekRelativeAsync(float timeDifference)
        {
            if (IsPlayerActive)
            {
                await Player!.StandardInput.WriteLineAsync($"seek {timeDifference} 0");
            }
        }

        public async Task ChangeCurrentMediaAsync(int mediaId)
        {
            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), mediaId);
            await PlayMedia();
        }

        public async Task ChangeRepeatModeAsync(RepeatMode repeatMode)
        {
            if (Device.RepeatMode != RepeatMode.Shuffle && repeatMode == RepeatMode.Shuffle)
            {
                UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.OrderBy(_ => Random.Next()).ToList());
                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);

                await PlayMedia();
            }

            UpdateDeviceProperty(nameof(Device.RepeatMode), repeatMode);
            await UpdateClients();
        }

        public async Task SetPlaybackRateAsync(float playbackRate)
        {
            UpdateDeviceProperty(nameof(Device.PlaybackRate), playbackRate);
            if (IsPlayerActive)
                await Player!.StandardInput.WriteLineAsync($"speed_set {Device.PlaybackRate}");

            await UpdateClients();
        }

        public async Task LaunchQueue(List<MediaItem> mediaItems)
        {
            await StopAsync();

            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0); 
            UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);

            await PlayMedia();
        }

        public async Task InsertQueueAsync(List<MediaItem> mediaItems, int? insertBefore)
        {
            if (insertBefore == null || insertBefore < 0 || insertBefore > (Device.MediaQueue.Count - 1))
                MediaQueueAddRange(mediaItems);
            else
                MediaQueueInsertRange(mediaItems, (int)insertBefore);

            await UpdateClients();
        }

        public async Task RemoveQueueAsync(IEnumerable<int> itemIds)
        {
            foreach(int itemId in itemIds.OrderByDescending(itemId => itemId))
                MediaQueueRemoveAt(itemId);

            await UpdateClients();
        }

        public async Task UpQueueAsync(IEnumerable<int> itemIds)
        {
            foreach (int itemId in itemIds.OrderByDescending(itemId => itemId))
                UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.MoveUp(itemId));

            await UpdateClients();
        }

        public async Task DownQueueAsync(IEnumerable<int> itemIds)
        {
            foreach (int itemId in itemIds.OrderBy(itemId => itemId))
                UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.MoveDown(itemId));

            await UpdateClients();
        }

        public async Task SetVolumeAsync(float volume)
        {
            UpdateDeviceProperty(nameof(Device.Volume), Math.Min(Math.Max(volume, 0), 1));
            if (IsPlayerActive)
                await Player!.StandardInput.WriteLineAsync($"volume {(int)(Device.Volume * 100)} 1");

            await UpdateClients();
        }

        public async Task ToggleMutedAsync()
        {
            UpdateDeviceProperty(nameof(Device.IsMuted), !Device.IsMuted);
            if (IsPlayerActive)
                await Player!.StandardInput.WriteLineAsync($"volume {(Device.IsMuted ? 0 : (int)(Device.Volume * 100))} 1");

            await UpdateClients();
        }

        #endregion

        #region Private Methods

        private async Task PlayMedia()
        {
            await StopMedia();

            UriCreationOptions options = new();
            if (Uri.TryCreate(Device.CurrentMedia?.Location, in options, out Uri? parsedUri) && parsedUri != null)
            {
                await SetDeviceStatus(DeviceStatus.Starting);

                List<string> arguments = Arguments.ToList();
                arguments.Add($"-volume {(Device.IsMuted ? 0 : (int)(Device.Volume * 100))} 1");
                arguments.Add($"-speed {Device.PlaybackRate}");
                arguments.Add($"-ss {Device.CurrentMedia.StartTime}");
                arguments.Add(parsedUri.ToString());

                Player = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = MPlayerLocation,
                        Arguments = string.Join(" ", arguments),
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                    EnableRaisingEvents = true                    
                };

                Player.OutputDataReceived += Player_OutputDataReceived;
                Player.ErrorDataReceived += Player_ErrorDataReceived;
                Player.Exited += Player_Exited;

                Player.Start();

                Player.BeginOutputReadLine();
                Player.BeginErrorReadLine();
            }
            else
            {
                UpdateDeviceProperty(nameof(Device.StatusMessage), "URI not found!");
                await UpdateClients();

                await LoggingService.LogError("Media URI Not found", $"The given URI for the media Item was not valid: \"{Device.CurrentMedia?.Location}\"");
            }
        }

        private async void Player_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {           
            if (dataReceivedEventArgs.Data != null)
            {
                await LoggingService.LogDebug("Player Standard Output", dataReceivedEventArgs.Data);

                string startingKey = "Starting playback...";
                if (Device.DeviceStatus != DeviceStatus.Playing && dataReceivedEventArgs.Data.Equals(startingKey))
                    await SetDeviceStatus(DeviceStatus.Playing);
            }
        }

        private void Player_Exited(object? sender, EventArgs eventArgs)
        {
            _ = Task.Run(async () =>
            {
                int exitCode = (sender as Process)?.ExitCode ?? 1;

                if (exitCode == 0)
                {
                    await SetDeviceStatus(DeviceStatus.Finishing);
                    await LoggingService.LogDebug("Player Exited", "Media finished.");

                    switch (Device.RepeatMode)
                    {
                        case RepeatMode.Off:
                            if (Device.CurrentMediaIndex == Device.MediaQueue.Count - 1)
                            {
                                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                                await SetDeviceStatus(DeviceStatus.Stopped);
                            }
                            else
                                await NextAsync();
                            break;
                        case RepeatMode.One:
                            await PlayAsync();
                            break;
                        case RepeatMode.All:
                        case RepeatMode.Shuffle:
                            if (Device.CurrentMediaIndex == Device.MediaQueue.Count - 1)
                            {
                                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                                await PlayAsync();
                            }
                            else
                                await NextAsync();
                            break;
                    }
                }
                else
                {
                    await SetDeviceStatus(DeviceStatus.Stopped);
                    await LoggingService.LogDebug("Player Stopped", string.Empty);
                }                  
            });
        }

        private void Player_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            _ = LoggingService.LogDebug("Player Standard Error", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async Task StopMedia()
        {
            if (IsPlayerActive)
                await Player!.StandardInput.WriteLineAsync("quit 1");
            else
                await SetDeviceStatus(DeviceStatus.Stopped);

        }

        private async Task SetDeviceStatus(DeviceStatus deviceStatus)
        {
            UpdateDeviceProperty(nameof(Device.DeviceStatus), deviceStatus);
            await UpdateClients();
        }

        private void UpdateDevice(object? sender, DeviceUpdateEventArgs deviceUpdateEventArgs) 
        {
            UpdateDeviceProperty(deviceUpdateEventArgs.Property, deviceUpdateEventArgs.Value);
        }

        private void UpdateDeviceProperty(string propertyName, object? value) 
        {
            lock (Device)
            {
                typeof(Device).GetProperty(propertyName)?.SetValue(Device, value);
            }
        }

        private void MediaQueueClear()
        {
            lock (Device)
            {
                Device.MediaQueue.Clear();
            }
        }

        private void MediaQueueAddRange(IEnumerable<MediaItem> mediaItems)
        {
            lock (Device)
            {
                Device.MediaQueue.AddRange(mediaItems);
            }
        }

        private void MediaQueueInsertRange(IEnumerable<MediaItem> mediaItems, int insertAtId)
        {
            lock (Device)
            {
                Device.MediaQueue.InsertRange(insertAtId, mediaItems);
            }
        }

        private void MediaQueueRemoveAt(int mediaId)
        {
            lock (Device)
            {
                Device.MediaQueue.RemoveAt(mediaId);
            }
        }

        #endregion
    }
}
