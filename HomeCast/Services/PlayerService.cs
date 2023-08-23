using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace HomeCast.Services
{
    public class PlayerService : IDisposable
    {

        #region Injections

        private IHubContext<DeviceHub> DeviceHubContext { get; }
        private CachingService CachingService { get; }
        private LoggingService<PlayerService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Private Properties

        private Device Device { get; }
        private SemaphoreQueue DeviceLock { get; } = new(1);

        private DirectoryInfo DataDirectoryInfo { get; }
        private FileInfo DeviceFileInfo { get; }
        private bool VideoCapable { get; }
        private int CacheAheadCount { get; }
                
        private Process? Player { get; set; }
        private Process? Socket { get; set; }
        private bool IsPlayerReady { get; set; } = false;

        private string MPVSocketLocation { get; }
        private int ProcessTimeoutSeconds { get; }

        private ConcurrentDictionary<long, WaitingCommand> WaitingCommands { get; } = new();
        private long LatestRequestId = 0;
        private PeriodicTimer PeriodicTimer { get; } = new(TimeSpan.FromMilliseconds(500));
        private CancellationTokenSource PeriodicTimerCancellationTokenSource { get; } = new();
        private HashSet<string> IgnoredOutputs { get; } = new();
        private Dictionary<string, string?> EnvironmentVariables { get; } = new();

        private bool IsDisposed { get; set; }

        #endregion

        #region Contructor

        public PlayerService(IHubContext<DeviceHub> deviceHubContext, CachingService cachingService, LoggingService<PlayerService> loggingService, IConfiguration configuration)
        {
            DeviceHubContext = deviceHubContext;
            CachingService = cachingService;
            LoggingService = loggingService;
            Configuration = configuration;

            MPVSocketLocation = Configuration["Services:Player:SocketLocation"] ?? Path.Combine("/", "home", "homecast", "mpv-socket");
            ProcessTimeoutSeconds = 10;

            string? name = Configuration["Device:Name"];
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Please define a proper device name in the app settings!");

            string? address = Configuration["Device:Address"];
            if (string.IsNullOrWhiteSpace(address))
                throw new InvalidOperationException("Please define a proper device address in the app settings!");

            DataDirectoryInfo = new(Configuration["Services:Player:DataLocation"] ?? Path.Combine("/", "home", "homecast", "data"));
            if (!DataDirectoryInfo.Exists)
                DataDirectoryInfo.Create();
                        
            VideoCapable = Configuration.GetValue<bool>("Services:Player:VideoCapable");

            EnvironmentVariables =
                Configuration.GetSection("Services:Player:EnvironmentVariables")
                    .GetChildren()
                    .Where(section => !string.IsNullOrWhiteSpace(section.Key))
                    .ToDictionary(section => section.Key, section => section.Value);

            foreach (string ignoredOutput in Configuration.GetSection("Services:Player:IgnoredOutputs").Get<string[]>() ?? Array.Empty<string>())
                IgnoredOutputs.Add(ignoredOutput);

            CacheAheadCount = Configuration.GetValue<int>("Services:Caching:CacheAheadCount");

            CachingService.CachingUpdateCallback = CachingUpdate;

            DeviceFileInfo = new FileInfo(Path.Combine(DataDirectoryInfo.FullName, "device.json"));
            if (DeviceFileInfo.Exists && File.ReadAllText(DeviceFileInfo.FullName).TryParseJson<Device>(out Device? parsedDevice) && parsedDevice != null)
            {
                Device = parsedDevice;
                Device.Name = name;
                Device.Address = address;
                Device.Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
                Device.DeviceStatus = DeviceStatus.Ended;

                if (Device.CurrentMedia != null)
                    Device.CurrentMedia.StartTime = Device.CurrentTime;

                UpdateMediaItemsCacheStatus(Device.MediaQueue).GetAwaiter().GetResult();

                UpdateCachingQueue().GetAwaiter().GetResult();
            }
            else
                Device = new()
                {
                    Name = name,
                    Address = address,
                    Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0",
                };

            _ = Task.Run(async () =>
            {
                while (await PeriodicTimer.WaitForNextTickAsync())
                {
                    await DeviceLock.WaitAsync();

                    try
                    {
                        if (PeriodicTimerCancellationTokenSource.Token.IsCancellationRequested || Device.DeviceStatus != DeviceStatus.Playing)
                            continue;

                        await UpdateCurrentTime();
                    }
                    finally
                    {
                        SaveDeviceFile();
                        DeviceLock.Release();
                    }
                }
            });

            FileInfo socketFileInfo = new(MPVSocketLocation);
            FileSystemWatcher socketWatcher = new(socketFileInfo.Directory?.FullName ?? "/");
            socketWatcher.Created +=
                (object _, FileSystemEventArgs fileSystemEventArgs) =>
                {
                    if (fileSystemEventArgs.FullPath == MPVSocketLocation)
                        StartSocket();
                };
            socketWatcher.EnableRaisingEvents = true;
        }

        #endregion

        #region Device Commands

        public async Task<Device> GetDevice()
        {
            await DeviceLock.WaitAsync();

            try
            {
                return Device;
            }
            finally
            {
                SaveDeviceFile();
                DeviceLock.Release();
            }
        }

        public async Task PlayMediaItem(string mediaItemId)
        {
            if (Device.IsCommandAvailable(PlayerCommand.PlayMediaItem) &&
                Device.MediaQueue.Any(mediaItem => mediaItem.Id == mediaItemId))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    await StopMedia();
                    Device.CurrentMediaItemId = mediaItemId;
                    await PlayMedia();
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task AddMediaItems(List<MediaItem> mediaItems, bool launch = false, string? insertBeforeMediaItemId = null)
        {
            if (Device.IsCommandAvailable(PlayerCommand.AddMediaItems))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (launch)
                    {
                        await StopMedia();
                    }

                    Device.AddMediaItems(mediaItems, launch, insertBeforeMediaItemId);
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemsAddMethod, mediaItems, launch, insertBeforeMediaItemId);

                    await UpdateMediaItemsCacheStatus(mediaItems);

                    if (launch)
                    {
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, Device.CurrentMediaItemId);
                        await PlayMedia();
                    }
                    else
                        await UpdateCachingQueue();

                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task RemoveMediaItems(IEnumerable<string> mediaItemIds)
        {
            if (Device.IsCommandAvailable(PlayerCommand.RemoveMediaItems) &&
                mediaItemIds.Any())
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (mediaItemIds.Any(mediaItemId => Device.CurrentMedia?.Id == mediaItemId))
                    {
                        await StopMedia();
                    }

                    Device.RemoveMediaItems(mediaItemIds);

                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemsRemoveMethod, mediaItemIds);

                    await UpdateCachingQueue();
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task MoveMediaItemsUp(IEnumerable<string> mediaItemIds)
        {
            if (Device.IsCommandAvailable(PlayerCommand.MoveMediaItemsUp))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    Device.MoveUpMediaItems(mediaItemIds);

                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemsMoveUpMethod, mediaItemIds);

                    await UpdateCachingQueue();
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task MoveMediaItemsDown(IEnumerable<string> mediaItemIds)
        {
            if (Device.IsCommandAvailable(PlayerCommand.MoveMediaItemsDown))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    Device.MoveDownMediaItems(mediaItemIds);

                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemsMoveDownMethod, mediaItemIds);

                    await UpdateCachingQueue();
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task Play()
        {
            if (Device.IsCommandAvailable(PlayerCommand.Play))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (Device.DeviceStatus == DeviceStatus.Paused)
                    {
                        Device.DeviceStatus = DeviceStatus.Unpausing;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);

                        if (await SendCommandAsync(new string[] { "set", "pause", "no" }, true))
                        {
                            Device.DeviceStatus = DeviceStatus.Playing;
                            await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);                            
                        }
                        else
                        {
                            await SendStatusMessage("Player Error"); 
                            await Stop();
                        }
                    }
                    else
                    {
                        await PlayMedia();
                    }
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task Stop()
        {
            if (Device.IsCommandAvailable(PlayerCommand.Stop))
            {
                try
                {
                    await StopMedia();

                    Device.CurrentMediaItemId = null;
                    Device.MediaQueue.Clear();
                    Device.DeviceStatus = DeviceStatus.Stopped;

                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, null);
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemsClearMethod);
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);
                }
                finally
                {
                    SaveDeviceFile();
                }
            }
        }

        public async Task Pause()
        {
            if (Device.IsCommandAvailable(PlayerCommand.Pause))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    Device.DeviceStatus = DeviceStatus.Pausing;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);

                    if (await SendCommandAsync(new string[] { "set", "pause", "yes" }, true))
                    {
                        Device.DeviceStatus = DeviceStatus.Paused;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);
                    }
                    else
                    {
                        await SendStatusMessage("Player Error"); 
                        await Stop();
                    }
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task Next()
        {
            if (Device.IsCommandAvailable(PlayerCommand.Next))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    await StopMedia();
                    Device.CurrentMediaItemId = Device.MediaQueue.ElementAt(Math.Min(Device.MediaQueue.IndexOf(Device.CurrentMedia!) + 1, Device.MediaQueue.Count - 1)).Id;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, Device.CurrentMediaItemId);
                    await PlayMedia();                    
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task Previous()
        {
            if (Device.IsCommandAvailable(PlayerCommand.Previous))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    await StopMedia();
                    Device.CurrentMediaItemId = Device.MediaQueue.ElementAt(Math.Max(Device.MediaQueue.IndexOf(Device.CurrentMedia!) - 1, 0)).Id;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, Device.CurrentMediaItemId);
                    await PlayMedia();

                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task Seek(float timeToSeek)
        {
            if (Device.IsCommandAvailable(PlayerCommand.Seek))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (!await SendCommandAsync(new string[] { "seek", Math.Min(Math.Max(timeToSeek, 0), Device.CurrentMedia!.Runtime).ToString(), "absolute" }, true))
                    {
                        await SendStatusMessage("Player Error");
                        await Stop();
                    }
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task SeekRelative(float timeDifference)
        {
            if (Device.IsCommandAvailable(PlayerCommand.SeekRelative))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (!await SendCommandAsync(new string[] { "seek", timeDifference.ToString() }, true))
                    {
                        await SendStatusMessage("Player Error");
                        await Stop();
                    }             
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task ChangeRepeatMode(RepeatMode repeatMode)
        {
            if (Device.IsCommandAvailable(PlayerCommand.ChangeRepeatMode))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (Device.RepeatMode != RepeatMode.Shuffle && repeatMode == RepeatMode.Shuffle)
                    {
                        await StopMedia();

                        IEnumerable<string> mediaItemIds = Device.MediaQueue.Select(mediaItem => mediaItem.Id).OrderBy(_ => Guid.NewGuid());

                        Device.OrderMediaItems(mediaItemIds);
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaQueueOrderUpdateMethod, mediaItemIds);

                        Device.CurrentMediaItemId = Device.MediaQueue.First().Id;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, Device.CurrentMediaItemId);

                        await PlayMedia();
                    }

                    Device.RepeatMode = repeatMode;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.RepeatModeUpdateMethod, Device.RepeatMode);
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task SetPlaybackRate(float playbackRate)
        {
            if (Device.IsCommandAvailable(PlayerCommand.SetPlaybackRate))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (await SendCommandAsync(new string[] { "set", "speed", playbackRate.ToString() }, true))
                    {
                        Device.PlaybackRate = playbackRate;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.PlaybackRateUpdateMethod, Device.PlaybackRate);
                    }
                    else
                    {
                        await SendStatusMessage("Player Error");
                        await Stop();
                    }
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task SetVolume(float volume)
        {
            if (Device.IsCommandAvailable(PlayerCommand.SetVolume))
            {
                await DeviceLock.WaitAsync();

                try
                {                    
                    if (await SendCommandAsync(new string[] { "set", "volume", (Math.Min(Math.Max(volume, 0), 1) * 100).ToString() }, true))
                    {
                        Device.Volume = volume;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.VolumeUpdateMethod, Device.Volume);
                    }
                    else
                    {
                        await SendStatusMessage("Player Error");
                        await Stop();
                    }
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        public async Task ToggleMute()
        {
            if (Device.IsCommandAvailable(PlayerCommand.ToggleMute))
            {
                await DeviceLock.WaitAsync();

                try
                {
                    if (await SendCommandAsync(new string[] { "set", "mute", Device.IsMuted ? "yes" : "no" }, true))
                    {
                        Device.IsMuted = !Device.IsMuted;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.IsMutedUpdateMethod, Device.IsMuted);
                    }
                    else
                    {
                        await SendStatusMessage("Player Error");
                        await Stop();
                    }                    
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        #endregion

        #region Process Methods

        private void StartProcesses()
        {
            IsPlayerReady = false;
            Player?.Dispose();

            // TODO: Add HDMI-CEC support.
            // TODO: Create HTML GUI for local control.
            // TODO: Add EQ presets.
            // TODO: Add EQ bars control.

            /*
                
            # Audio filters:
            F1 show-text "F2: loudnorm | F3: dynaudnorm | F4: low Bass | F5: low Treble | F6: sinusoidal" 2000

            # loudnorm:
            F2 af toggle lavfi=[loudnorm=I=-16:TP=-3:LRA=4]

            # dynaudnorm:
            F3 af toggle lavfi=[dynaudnorm=g=5:f=250:r=0.9:p=0.5]

            # lowered bass:
            F4  af toggle "superequalizer=6b=2:7b=2:8b=2:9b=2:10b=2:11b=2:12b=2:13b=2:14b=2:15b=2:16b=2:17b=2:18b=2"

            # lowered treble:
            F5  af toggle "superequalizer=1b=2:2b=2:3b=2:4b=2:5b=2:6b=2:7b=2:8b=2:9b=2:10b=2:11b=2:12b=2"

            # sinusoidal:
            F6  af toggle "superequalizer=1b=2.0:2b=3.6:3b=3.8:4b=5.5:5b=6.0:6b=6.4:7b=6.6:8b=6.4:9b=6.0:10b=5.2:11b=4.0:12b=3.2:13b=3.0:14b=3.2:15b=3.8:16b=4.4:17b=5.2:18b=6.5"
                
            */

            List<string> playerArguments = new()
            {
                "--idle",
                "--force-seekable=yes",
                "--really-quiet",
                "--msg-level=all=warn",
                "--script-opts=ytdl_hook-ytdl_path=yt-dlp",
                "--af=superequalizer=1b=2.0:2b=3.6:3b=3.8:4b=5.5:5b=6.0:6b=6.4:7b=6.6:8b=6.4:9b=6.0:10b=5.2:11b=4.0:12b=3.2:13b=3.0:14b=3.2:15b=3.8:16b=4.4:17b=5.2:18b=6.5",
                $"--volume={Device.Volume * 100}",
                $"--mute={(Device.IsMuted ? "yes" : "no")}",
                $"--speed={Device.PlaybackRate}",
                $"--input-ipc-server={MPVSocketLocation}"
            };

            if (!VideoCapable)
                playerArguments.Add("--no-video");

            if (File.Exists(MPVSocketLocation))
                File.Delete(MPVSocketLocation);

            Player = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = string.Join(" ", playerArguments),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };

            foreach (KeyValuePair<string, string?> environmentVariable in EnvironmentVariables)
                Player.StartInfo.EnvironmentVariables.Add(environmentVariable.Key, environmentVariable.Value);

            Player.OutputDataReceived += Player_OutputDataReceived;
            Player.ErrorDataReceived += Player_ErrorDataReceived;
            Player.Exited += Player_Exited;

            Player.Start();

            Player.BeginOutputReadLine();
            Player.BeginErrorReadLine();
        }

        private void StartSocket()
        {
            IsPlayerReady = false;
            Socket?.Dispose();

            List<string> socketArguments = new()
            {
                "-",
                MPVSocketLocation,
            };

            Socket = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "socat",
                    Arguments = string.Join(" ", socketArguments),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };

            Socket.OutputDataReceived += Socket_OutputDataReceived;
            Socket.ErrorDataReceived += Socket_ErrorDataReceived;
            Socket.Exited += Socket_Exited;

            Socket.Start();
            Task.Delay(100).GetAwaiter().GetResult();

            Socket.BeginOutputReadLine();
            Socket.BeginErrorReadLine();

            if (!SendCommandAsync(new string[] { "disable_event", "all" }, true, noSocketCheck: true).GetAwaiter().GetResult() ||
            !SendCommandAsync(new string[] { "enable_event", "end-file" }, true, noSocketCheck: true).GetAwaiter().GetResult() ||
            !SendCommandAsync(new string[] { "enable_event", "seek" }, true, noSocketCheck: true).GetAwaiter().GetResult() ||
            !SendCommandAsync(new string[] { "enable_event", "playback-restart" }, true, noSocketCheck: true).GetAwaiter().GetResult())
            {
                SendStatusMessage("Player Error").GetAwaiter().GetResult();
                Stop().GetAwaiter().GetResult();
            }

            IsPlayerReady = true;
        }

        private void StopProcesses()
        {
            IsPlayerReady = false;

            Player?.Dispose();
            Player = null;

            Socket?.Dispose();
            Socket = null;
        }


        private async void Player_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (!IgnoredOutputs.Contains(dataReceivedEventArgs.Data ?? string.Empty))
                await LoggingService.LogWarning("Player Warning", dataReceivedEventArgs.Data ?? string.Empty);
        }

        private async void Player_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (!IgnoredOutputs.Contains(dataReceivedEventArgs.Data ?? string.Empty))
                await LoggingService.LogError("Player Error", dataReceivedEventArgs.Data ?? string.Empty);
        }

        private async void Player_Exited(object? sender, EventArgs eventArgs)
        {
            File.Delete(MPVSocketLocation);
            await LoggingService.LogError("Player Exited", string.Empty);
        }

        private async void Socket_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            string? socketOutput = dataReceivedEventArgs.Data;
            if (socketOutput != null && socketOutput.TryParseJson(out CommandResponse? commandResponse) && commandResponse != null &&
                WaitingCommands.TryGetValue(commandResponse.RequestId, out WaitingCommand? waitingCommand) && waitingCommand != null)
            {
                waitingCommand.Data = commandResponse.Data;
                waitingCommand.Success = commandResponse.Error.Equals("success", StringComparison.InvariantCultureIgnoreCase);
                waitingCommand.Error = commandResponse.Error;

                WaitingCommands.TryRemove(waitingCommand.RequestId, out _);
                waitingCommand.Callback.SetResult(waitingCommand);
            }
            else if (socketOutput != null && socketOutput.TryParseJson(out EventResponse? eventResponse) && eventResponse != null)
            {
                await DeviceLock.WaitAsync();

                try
                {
                    switch (eventResponse.Event)
                    {
                        case "end-file":
                            await LoggingService.LogDebug("Playback stopping.", string.Empty);

                            if (eventResponse.Reason == "eof")
                            {
                                Device.DeviceStatus = DeviceStatus.Finished;
                                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);

                                Device.CurrentTime = 0;
                                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentTimeUpdateMethod, Device.CurrentTime);

                                switch (Device.RepeatMode)
                                {
                                    case RepeatMode.One:
                                        await PlayMedia();
                                        break;
                                    case RepeatMode.All:
                                    case RepeatMode.Shuffle:
                                    case RepeatMode.Off:
                                        if (Device.CurrentMediaItemId == Device.MediaQueue.LastOrDefault()?.Id)
                                        {
                                            Device.CurrentMediaItemId = Device.MediaQueue.FirstOrDefault()?.Id;
                                            if (Device.RepeatMode == RepeatMode.Off)
                                            {
                                                Device.DeviceStatus = DeviceStatus.Ended;
                                                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);
                                            }
                                            else
                                                await PlayMedia();
                                        }
                                        else if (Device.CurrentMedia != null)
                                        {
                                            Device.CurrentMediaItemId = Device.MediaQueue.ElementAt(Math.Min(Device.MediaQueue.IndexOf(Device.CurrentMedia) + 1, Device.MediaQueue.Count - 1)).Id;
                                            await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentMediaItemIdUpdateMethod, Device.CurrentMediaItemId);
                                            await PlayMedia();
                                        }
                                        break;
                                }            
                            }
                            else
                            {
                                Device.DeviceStatus = DeviceStatus.Ended;
                                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);
                            }
                            break;

                        case "seek":
                            Device.DeviceStatus = DeviceStatus.Buffering;
                            await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);
                            break;

                        case "playback-restart":
                            Device.DeviceStatus = DeviceStatus.Playing;
                            await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);
                            await UpdateCurrentTime();
                            break;
                    }
                }
                finally
                {
                    SaveDeviceFile();
                    DeviceLock.Release();
                }
            }
        }

        private async void Socket_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (!IgnoredOutputs.Contains(dataReceivedEventArgs.Data ?? string.Empty))
                await LoggingService.LogError("Socket Error", dataReceivedEventArgs.Data ?? string.Empty);
        }

        private async void Socket_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogError("Socket Exited", string.Empty);
        }

        #endregion

        #region Private Methods

        // Return if command failed or not, and decide when called if processes should be failed or not
        private async Task<bool> SendCommandAsync(IEnumerable<object> commandSegments, bool waitForResponse = false, bool doLog = true, Func<object?, Task>? postProcessing = null, bool noSocketCheck = false)
        {
            long requestId = Interlocked.Increment(ref LatestRequestId);
            commandSegments = commandSegments.Select(commandSegment => commandSegment is string ? $"\"{commandSegment}\"" : (commandSegment.ToString() ?? string.Empty));
            string command = $"{{ command: [ {string.Join(", ", commandSegments)} ], request_id: {requestId} }}";

            if (!noSocketCheck && (Socket == null || !IsPlayerReady))
            {
                CancellationToken socketWaitToken = new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds)).Token;
                while (Socket == null || !IsPlayerReady)
                {
                    if (socketWaitToken.IsCancellationRequested)
                    {
                        await LoggingService.LogError("Socket Process Timeout", $"Command \"{command}\" could not be processed since process is not available.");
                        return false;
                    }

                    await Task.Delay(100);
                }
            }

            if (!waitForResponse)
            {
                await Socket!.StandardInput.WriteLineAsync(command);
            }
            else
            {
                WaitingCommand waitingCommand = new()
                {
                    RequestId = requestId,
                    Callback = new TaskCompletionSource<WaitingCommand>(TaskCreationOptions.RunContinuationsAsynchronously)
                };

                WaitingCommands.AddOrUpdate(requestId, (requestId) => waitingCommand, (requestId, oldWaitingCommand) => waitingCommand);

                await Socket!.StandardInput.WriteLineAsync(command);

                try
                {
                    WaitingCommand result = await waitingCommand.Callback.Task.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds)).Token);

                    if (!result.Success)
                    {
                        if (doLog)
                            await LoggingService.LogError("Socket Command Failure", $"Command \"{command}\" received \"{result.Error}\".");
                        return false;
                    }

                    if (doLog)
                        await LoggingService.LogDebug("Socket Command Response", $"Got \"{result.Data}\" for command \"{command}\".");
                    
                    if (postProcessing != null)
                        await postProcessing.Invoke(result.Data);
                }
                catch (TaskCanceledException)
                {
                    if (doLog)
                        await LoggingService.LogError("Socket Command Timeout", $"Command \"{command}\" timedout after {ProcessTimeoutSeconds} second(s).");
                    return false;
                }

            }

            return true;
        }

        private async Task UpdateCurrentTime()
        {
            await SendCommandAsync(new string[] { "get_property", "time-pos" }, true, false, async (object? currentTime) =>
            {
                if (currentTime != null)
                {
                    Device.CurrentTime = Convert.ToDouble(currentTime);
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentTimeUpdateMethod, Device.CurrentTime);
                }
            });
        }

        private async Task PlayMedia()
        {
            if (Device.CurrentMedia == null)
                return;

            StartProcesses();

            string? playLocation = string.Empty;

            // Check and use cache location.
            if (Device.CurrentMedia.CacheStatus != CacheStatus.Off)
            {
                (bool CacheDownloadQueued, CachingInformation CachingInFormation)  = await CachingService.TryGetOrQueueCacheItem(Device.CurrentMedia);
                if (CachingInFormation.CacheFileInfo != null)
                {
                    playLocation = CachingInFormation.CacheFileInfo.FullName;
                    
                    Device.CurrentMedia.CachedRatio = 1;
                    Device.CurrentMedia.CacheStatus = CacheStatus.Cached;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemCacheUpdateMethod, Device.CurrentMedia.Id, Device.CurrentMedia.CacheStatus, Device.CurrentMedia.CachedRatio);
                }
                else if (CacheDownloadQueued)
                    await SendStatusMessage("Caching media...");
                else
                {
                    await SendStatusMessage("Couldn't cache media!");

                    Device.CurrentMedia.CachedRatio = 0;
                    Device.CurrentMedia.CacheStatus = CacheStatus.Off;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemCacheUpdateMethod, Device.CurrentMedia.Id, Device.CurrentMedia.CacheStatus, Device.CurrentMedia.CachedRatio);
                }
            }

            // Check streaming location.
            if (string.IsNullOrWhiteSpace(playLocation))
            {
                UriCreationOptions options = new();
                if (Uri.TryCreate(Device.CurrentMedia.Location, in options, out Uri? parsedUri) && parsedUri != null)
                    playLocation = parsedUri.ToString();
                else
                    await SendStatusMessage("Invalid URI!");
            }

            // Play media if valid location was found.
            if (!string.IsNullOrWhiteSpace(playLocation))
            {
                Device.DeviceStatus = DeviceStatus.Starting;
                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);

                if (!await SendCommandAsync(new string[] { "loadfile", playLocation, "replace", $"start={Device.CurrentMedia.StartTime},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\"" }, true))
                {
                    await SendStatusMessage("Player Error");
                    await Stop();
                }
            }
            else
                await LoggingService.LogError("Player Service", $"Could not get a valid location from cache or URI: \"{Device.CurrentMedia.Location}\".");

            await UpdateCachingQueue();
        }

        private async Task StopMedia()
        {
            await LoggingService.LogDebug("Stopping Media", $"Stopped Media.");

            if (Device.IsMediaLoaded)
            {
                Device.DeviceStatus = DeviceStatus.Stopping;
                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.DeviceStatusUpdateMethod, Device.DeviceStatus);

                await SendCommandAsync(new string[] { "get_property", "time-pos" }, true, true, async (object? currentTime) =>
                {
                    if (currentTime != null)
                    {
                        Device.CurrentTime = 0;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.CurrentTimeUpdateMethod, Device.CurrentTime);
                        Device.CurrentMedia!.StartTime = Math.Max(Convert.ToDouble(currentTime) - 5, 0);
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.StartTimeUpdateMethod, Device.CurrentMedia!.StartTime);
                    }
                });

                await SendCommandAsync(new string[] { "stop" }, true);

                StopProcesses();
            }
        }

        private async Task UpdateMediaItemsCacheStatus(IEnumerable<MediaItem> mediaItems)
        {
            foreach (MediaItem mediaItem in mediaItems)
            {
                CachingInformation? cachingInformation = CachingService.UpdateMediaItemsCacheStatus(mediaItem);
                if (cachingInformation != null)
                {
                    mediaItem.CacheStatus = cachingInformation.CacheStatus;
                    mediaItem.CachedRatio = cachingInformation.CachedRatio;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemCacheUpdateMethod, mediaItem.Id, mediaItem.CacheStatus, mediaItem.CachedRatio);
                }
            }
        }

        private async Task UpdateCachingQueue()
        {
            if (Device.RepeatMode != RepeatMode.One && Device.CurrentMedia != null)
            {
                List<int> indicesToCache = new();
                int currentIndex = Device.MediaQueue.IndexOf(Device.CurrentMedia);
                for (int count = 0; count <= CacheAheadCount; count++)
                {
                    if (currentIndex >= Device.MediaQueue.Count && (Device.RepeatMode == RepeatMode.All || Device.RepeatMode == RepeatMode.Shuffle))
                        currentIndex = 0;

                    if (!indicesToCache.Contains(currentIndex))
                        indicesToCache.Add(currentIndex);

                    currentIndex++;
                }

                foreach (CachingInformation cachingInformation in await CachingService.UpdateCachingQueue(Device.MediaQueue
                    .Select((mediaItem, index) => (mediaItem, index))
                    .Where(queueItem => indicesToCache.Contains(queueItem.index))
                    .Select(queueItem => queueItem.mediaItem)))
                {
                    foreach(MediaItem mediaItem in Device.MediaQueue.Where(mediaItem => mediaItem.MediaId == cachingInformation.MediaId))
                    {
                        mediaItem.CacheStatus = cachingInformation.CacheStatus;
                        mediaItem.CachedRatio = cachingInformation.CachedRatio;
                        await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemCacheUpdateMethod, mediaItem.Id, mediaItem.CacheStatus, mediaItem.CachedRatio);
                    }
                }
            }
        }

        private async Task CachingUpdate(CachingInformation cachingUpdateEventArgs)
        {
            await DeviceLock.WaitAsync();
            
            try
            {
                // TODO: make sure pause=true works when cached is done on paused media.

                if (cachingUpdateEventArgs.CacheStatus == CacheStatus.Cached &&
                        cachingUpdateEventArgs.MediaId == Device.CurrentMedia?.MediaId &&
                        cachingUpdateEventArgs.CacheFileInfo != null &&
                        Device.CurrentMedia != null &&
                        Device.IsMediaLoaded)
                        if (!await SendCommandAsync(new string[]
                        {
                            "loadfile",
                            cachingUpdateEventArgs.CacheFileInfo.FullName,
                            "replace",
                            $"start={Device.CurrentTime}{(Device.DeviceStatus == DeviceStatus.Pausing || Device.DeviceStatus == DeviceStatus.Paused ? ",pause=yes" : string.Empty)},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\""
                        }, true))
                    {
                        await SendStatusMessage("Player Error");
                        await Stop();
                    }

                foreach(MediaItem mediaItem in Device.MediaQueue.Where(mediaItem => mediaItem.MediaId == cachingUpdateEventArgs.MediaId))
                {
                    mediaItem.CacheStatus = cachingUpdateEventArgs.CacheStatus;
                    mediaItem.CachedRatio = cachingUpdateEventArgs.CachedRatio;
                    await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.MediaItemCacheUpdateMethod, mediaItem.Id, cachingUpdateEventArgs.CacheStatus, cachingUpdateEventArgs.CachedRatio);
                }
            }
            finally
            {
                SaveDeviceFile();
                DeviceLock.Release();
            }
        }

        private async Task SendStatusMessage(string statusMessage)
        {
            Device.StatusMessage = statusMessage;
            await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.StatusMessageUpdateMethod, Device.StatusMessage);

            _ = Task.Run(async () => {
                await Task.Delay(3000);
                Device.StatusMessage = null;
                await DeviceHubContext.Clients.All.SendAsync(DeviceHubConstants.StatusMessageUpdateMethod, Device.StatusMessage);
            });            
        }

        private void SaveDeviceFile()
        {
            string deviceFileText = JsonConvert.SerializeObject(Device);
            _ = Task.Run(async () =>
            {
                try
                {
                    await File.WriteAllTextAsync(DeviceFileInfo.FullName, deviceFileText);
                }
                catch (Exception exception)
                {
                    await LoggingService.LogWarning("Saving Device File Problem", $"Problem while saving device file: \"{exception.Message}\".");
                }
            });
        }

        #endregion

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    PeriodicTimerCancellationTokenSource.Cancel();
                }

                Player?.Dispose();
                Player = null;
                Socket?.Dispose();
                Socket = null;
                IsDisposed = true;
            }
        }

        ~PlayerService()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
