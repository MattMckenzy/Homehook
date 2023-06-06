using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace HomeCast.Services
{
    public class PlayerService : IDisposable
    {
        #region Injections

        private IHubContext<DeviceHub> DeviceHubContext { get; }
        private CachingService CachingService { get; }
        private ScriptsProcessor ScriptsProcessor { get; }
        private LoggingService<PlayerService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        #endregion

        #region Private Properties

        private Device Device { get; }

        private static readonly SemaphoreSlim DeviceLock = new(1, 1);

        private string MPVSocketLocation { get; }
        private int ProcessTimeoutSeconds { get; }

        private Random Random { get; } = new(DateTime.Now.Ticks.GetHashCode());

        private Process? Player { get; set; }
        private Process? Socket { get; set; }

        private bool IsMediaLoaded { get { return new DeviceStatus[] { DeviceStatus.Finished, DeviceStatus.Stopping, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) == false; } }

        private bool DisposedValue { get; set; }

        private PeriodicTimer PeriodicTimer { get; } = new(TimeSpan.FromSeconds(1));
        private CancellationTokenSource PeriodicTimerCancellationTokenSource { get; } = new();
        
        private CacheItem? CacheItem { get; set; }

        private ConcurrentDictionary<long, WaitingCommand> WaitingCommands { get; } = new();
        private long LatestRequestId = 0;

        private HashSet<string> IgnoredOutputs { get; } = new();

        #endregion

        #region Contructor

        public PlayerService(IHubContext<DeviceHub> deviceHubContext, CachingService cachingService, ScriptsProcessor scriptsProcessor, LoggingService<PlayerService> loggingService, IConfiguration configuration)
        {
            DeviceHubContext = deviceHubContext;
            CachingService = cachingService;
            ScriptsProcessor = scriptsProcessor;
            LoggingService = loggingService;
            Configuration = configuration;

            MPVSocketLocation = Path.Combine("/", "tmp", "mpv-socket");
            ProcessTimeoutSeconds = 10;

            string? name = Configuration["Device:Name"];
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Please define a proper device name in the app settings!");

            string? address = Configuration["Device:Address"];
            if (string.IsNullOrWhiteSpace(address))
                throw new InvalidOperationException("Please define a proper device address in the app settings!");

            if (!Enum.TryParse(Configuration["Device:OS"], out OSType oSType))
                throw new InvalidOperationException("Please define a proper device OS in the app settings!");

            foreach(string ignoredOutput in Configuration.GetSection("Services:Player:IgnoredOutputs").Get<string[]>() ?? Array.Empty<string>())
                IgnoredOutputs.Add(ignoredOutput);

            Device = new()
            {
                Name = name,
                Address = address,
                Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0",
            };

            ScriptsProcessor.DeviceUpdate += 
                (object? sender, DeviceUpdateEventArgs deviceUpdateEventArgs) => 
                UpdateDeviceProperty(deviceUpdateEventArgs.Property, deviceUpdateEventArgs.Value);

            StartProcesses();

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
                        DeviceLock.Release();
                    }
                }
            });
        }

        #endregion

        #region Public Methods

        public async Task<Device> GetDevice()
        {
            await DeviceLock.WaitAsync();

            try
            {
                return Device;
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task UpdateMediaItemsSelection(IEnumerable<int> mediaItemIndices, bool isSelected)
        {
            await DeviceLock.WaitAsync();

            try
            {
                foreach(int mediaItemIndex in mediaItemIndices)
                {
                    MediaItem? mediaItem = Device.MediaQueue.ElementAtOrDefault(mediaItemIndex);
                    if (mediaItem != null)
                    {
                        mediaItem.IsSelected = isSelected;
                    }
                }                
                
                await UpdateClients();
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task PlaySelectedMediaItem()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any(mediaItem => mediaItem.IsSelected))
                {
                    await StopMedia();
                    UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Device.MediaQueue.IndexOf(Device.MediaQueue.First(mediaItem => mediaItem.IsSelected)));
                    await PlayMedia();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task AddMediaItems(List<MediaItem> mediaItems, bool launch = false, bool insertBeforeSelectedMediaItem = false)
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (launch)
                {
                    await StopMedia();
                    UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), null);
                    Device.MediaQueue.Clear();
                }

                int insertBeforeIndex = insertBeforeSelectedMediaItem && Device.MediaQueue.Any(mediaItem => mediaItem.IsSelected) ? 
                    Device.MediaQueue.IndexOf(Device.MediaQueue.First(mediaItem => mediaItem.IsSelected)) : 
                    Device.MediaQueue.Count;

                Device.MediaQueue.InsertRange(insertBeforeIndex, mediaItems);

                if (launch)
                {
                    UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                    await PlayMedia();
                }
                                        
                await UpdateClients();
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task RemoveSelectedMediaItems()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any())
                {
                    int? currentMediaIndex = null;
                    if (Device.CurrentMedia != null && Device.CurrentMedia.IsSelected)
                        currentMediaIndex = Device.MediaQueue.IndexOf(Device.CurrentMedia);

                    foreach (MediaItem mediaItem in Device.MediaQueue.ToArray().Where(mediaItem => mediaItem.IsSelected).Reverse())
                    {
                        if (mediaItem == Device.CurrentMedia)
                            await StopMedia();

                        Device.MediaQueue.Remove(mediaItem);
                    }

                    if (currentMediaIndex != null)
                    {
                        Device.CurrentMediaIndex = Math.Min((int)currentMediaIndex, Device.MediaQueue.Count - 1);
                        await StopMedia();
                        await PlayMedia();
                    }

                    await UpdateClients();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task MoveSelectedMediaItemsUp()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any())
                {
                    foreach (MediaItem mediaItem in Device.MediaQueue.ToArray().Where(mediaItem => mediaItem.IsSelected))
                    {
                        int index = Device.MediaQueue.IndexOf(mediaItem);
                        if (index > 0 && index < Device.MediaQueue.Count)
                        {
                            index--;

                            Device.MediaQueue.Remove(mediaItem);
                            Device.MediaQueue.Insert(index, mediaItem);
                        }
                    }

                    await UpdateClients();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task MoveSelectedMediaItemsDown()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any())
                {
                    foreach (MediaItem mediaItem in Device.MediaQueue.ToArray().Where(mediaItem => mediaItem.IsSelected).Reverse())
                    {
                        int index = Device.MediaQueue.IndexOf(mediaItem);
                        if (index >= 0 && index < Device.MediaQueue.Count - 1)
                        {
                            index++;

                            Device.MediaQueue.Remove(mediaItem);
                            Device.MediaQueue.Insert(index, mediaItem);
                        }
                    }

                    await UpdateClients();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task Play()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.DeviceStatus == DeviceStatus.Paused)
                {
                    await SetDeviceStatus(DeviceStatus.Unpausing);

                    await SendCommandAsync(new string[] { "set", "pause", "no" }, true);
                }
                else if (Device.DeviceStatus == DeviceStatus.Stopped && Device.MediaQueue.Any())
                {
                    UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                    await PlayMedia();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task Stop()
        {
            await DeviceLock.WaitAsync();

            try
            {
                await StopMedia();

                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), null);

                Device.MediaQueue.Clear();

                await SetDeviceStatus(DeviceStatus.Stopped);
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task Pause()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.DeviceStatus == DeviceStatus.Playing)
                {
                    await SetDeviceStatus(DeviceStatus.Pausing);

                    await SendCommandAsync(new string[] { "set", "pause", "yes" }, true);
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task Next()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any())
                {
                    await StopMedia();
                    UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Min((Device.CurrentMediaIndex ?? 0) + 1, Device.MediaQueue.Count - 1));
                    await PlayMedia();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task Previous()
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any())
                {
                    await StopMedia();
                    UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Max((Device.CurrentMediaIndex ?? 0) - 1, 0));
                    await PlayMedia();
                }
            }
            finally
            {
                DeviceLock.Release();
            }    
        }

        public async Task Seek(float timeToSeek)
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (IsMediaLoaded && Device.CurrentMedia != null)
                {
                    await SendCommandAsync(new string[] { "seek", Math.Min(Math.Max(timeToSeek, 0), Device.CurrentMedia.Runtime).ToString(), "absolute" }, true);
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task SeekRelative(float timeDifference)
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (IsMediaLoaded && Device.CurrentMedia != null)
                {
                    await SendCommandAsync(new string[] { "seek", timeDifference.ToString() }, true);
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task ChangeRepeatMode(RepeatMode repeatMode)
        {
            await DeviceLock.WaitAsync();

            try
            {
                if (Device.MediaQueue.Any())
                {
                    if (Device.RepeatMode != RepeatMode.Shuffle && repeatMode == RepeatMode.Shuffle)
                    {
                        await StopMedia();

                        UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.OrderBy(_ => Random.Next()).ToList());
                        UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);

                        await PlayMedia();
                    }

                    UpdateDeviceProperty(nameof(Device.RepeatMode), repeatMode);
                    await UpdateClients();
                }
            }
            finally
            {
                DeviceLock.Release();
            }
        }

        public async Task SetPlaybackRate(float playbackRate)
        {
            await DeviceLock.WaitAsync();

            try
            {
                await SendCommandAsync(new string[] { "set", "speed", playbackRate.ToString() }, true);
            }
            finally
            {
                DeviceLock.Release();
            }    
        }

        public async Task SetVolume(float volume)
        {
            await DeviceLock.WaitAsync();

            try
            {
                await SendCommandAsync(new string[] { "set", "volume", (Math.Min(Math.Max(volume, 0), 1) * 100).ToString() }, true);
            }
            finally
            {
                DeviceLock.Release();
            }          
        }

        public async Task ToggleMuted()
        {
            await DeviceLock.WaitAsync();

            try
            {
                await SendCommandAsync(new string[] { "set", "mute", !Device.IsMuted ? "yes" : "no" }, true);
            }
            finally
            {
                DeviceLock.Release();
            }      
        }

        #endregion

        #region Process Methods

        private void StartProcesses()
        {
            bool playerRestarted = true;
            if (Player == null || Player.HasExited || !Player.Responding)
            {
                Player?.Dispose();

                bool videoCapable = Configuration.GetValue<bool>("Services:Player:VideoCapable");

                // TODO: change device lock semaphore to FIFO
                // TODO: Persist device settings on restarts.
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
                    "--cache=yes",
                    $"--cache-secs=30",
                    $"--cache-dir={Path.Combine("/", "tmp", "mpv-cache")}",
                    $"--volume={Device.Volume * 100}",
                    $"--input-ipc-server={MPVSocketLocation}"
                };

                if (!videoCapable)
                    playerArguments.Add("--no-video");

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

                Player.OutputDataReceived += Player_OutputDataReceived;
                Player.ErrorDataReceived += Player_ErrorDataReceived;
                Player.Exited += Player_Exited;

                Player.Start();

                Player.BeginOutputReadLine();
                Player.BeginErrorReadLine();

                playerRestarted = true;

                Task.Delay(3000).GetAwaiter().GetResult();
            }

            if (Socket == null || Socket.HasExited || !Socket.Responding)
            {
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

                Socket.BeginOutputReadLine();
                Socket.BeginErrorReadLine();
            }

            if (playerRestarted)
            {
                Task.Delay(1000).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "disable_event", "all" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 1, "volume" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 2, "mute" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 3, "pause" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 4, "speed" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "enable_event", "end-file" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "enable_event", "seek" }, true).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "enable_event", "playback-restart" }, true).GetAwaiter().GetResult();
            }
        }

        private async void Player_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (!IgnoredOutputs.Contains(dataReceivedEventArgs.Data ?? string.Empty))
                await LoggingService.LogWarning("Player Warning", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void Player_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (!IgnoredOutputs.Contains(dataReceivedEventArgs.Data ?? string.Empty))
                await LoggingService.LogError("Player Error", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void Player_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogError("Player Exited", "Restarting...");

            StartProcesses();
        }

        private async void Socket_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            string? socketOutput = dataReceivedEventArgs.Data;
            if (socketOutput != null)
            {
                Debug.WriteLine($"Debug - Received socket output: {socketOutput}");

                if (socketOutput.TryParseJson(out CommandResponse? commandResponse) && commandResponse != null &&
                    WaitingCommands.TryGetValue(commandResponse.RequestId, out WaitingCommand? waitingCommand) && waitingCommand != null)
                {
                    waitingCommand.Data = commandResponse.Data;
                    waitingCommand.Success = commandResponse.Error.Equals("success", StringComparison.InvariantCultureIgnoreCase);
                    waitingCommand.Error = commandResponse.Error;

                    WaitingCommands.TryRemove(waitingCommand.RequestId, out _);
                    waitingCommand.Callback.SetResult(waitingCommand);
                }
                else if (socketOutput.TryParseJson(out EventResponse? eventResponse) && eventResponse != null)
                {
                    await DeviceLock.WaitAsync();

                    try
                    {
                        switch (eventResponse.Event)
                        {
                            case "property-change" when eventResponse.Name.Equals("volume"):
                                if (float.TryParse(eventResponse.Data.ToString(), out float volume))
                                    UpdateDeviceProperty(nameof(Device.Volume), volume / 100);
                                await UpdateClients();
                                break;

                            case "property-change" when eventResponse.Name.Equals("mute"):
                                if (bool.TryParse(eventResponse.Data.ToString(), out bool mute))
                                    UpdateDeviceProperty(nameof(Device.IsMuted), mute);
                                await UpdateClients();
                                break;

                            case "property-change" when eventResponse.Name.Equals("speed"):
                                if (float.TryParse(eventResponse.Data.ToString(), out float playbackRate))
                                    UpdateDeviceProperty(nameof(Device.PlaybackRate), playbackRate);
                                await UpdateClients();
                                break;

                            case "property-change" when eventResponse.Name.Equals("pause"):
                                if (bool.TryParse(eventResponse.Data.ToString(), out bool isPaused))
                                {
                                    if (Device.DeviceStatus == DeviceStatus.Pausing && isPaused)
                                        await SetDeviceStatus(DeviceStatus.Paused);
                                    else if (Device.DeviceStatus == DeviceStatus.Unpausing && !isPaused)
                                        await SetDeviceStatus(DeviceStatus.Playing);
                                }
                                break;

                            case "end-file":
                                await LoggingService.LogDebug("Playback stopping.", string.Empty);

                                if (eventResponse.Reason == "eof")
                                {
                                    await SetDeviceStatus(DeviceStatus.Finished);

                                    switch (Device.RepeatMode)
                                    {
                                        case RepeatMode.Off:
                                            if (Device.CurrentMediaIndex == Device.MediaQueue.Count - 1)
                                            {
                                                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                                                await SetDeviceStatus(DeviceStatus.Ended);
                                            }
                                            else
                                            {
                                                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Min((Device.CurrentMediaIndex ?? 0) + 1, Device.MediaQueue.Count - 1));
                                                await PlayMedia();
                                            }
                                            break;
                                        case RepeatMode.One:                                            
                                            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                                            await PlayMedia();                                            
                                            break;
                                        case RepeatMode.All:
                                        case RepeatMode.Shuffle:
                                            if (Device.CurrentMediaIndex == Device.MediaQueue.Count - 1)
                                            {
                                                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
                                                await PlayMedia();
                                            }
                                            else
                                            {
                                                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Min((Device.CurrentMediaIndex ?? 0) + 1, Device.MediaQueue.Count - 1));
                                                await PlayMedia();
                                            }
                                            break;
                                    }
                                }
                                break;

                            case "seek":
                                await SetDeviceStatus(DeviceStatus.Buffering);
                                break;

                            case "playback-restart":
                                await SetDeviceStatus(DeviceStatus.Playing);
                                await UpdateCurrentTime();
                                break;
                        }
                    }
                    finally
                    {
                        DeviceLock.Release();
                    }
                }
            }

            await Task.CompletedTask;
        }

        private async void Socket_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (!IgnoredOutputs.Contains(dataReceivedEventArgs.Data ?? string.Empty))
                await LoggingService.LogError("Socket Error", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void Socket_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogError("Socket Exited", "Restarting...");

            StartProcesses();
        }

        #endregion

        #region Private Methods

        private async Task<object?> SendCommandAsync(IEnumerable<object> commandSegments, bool waitForResponse = false)
        {
            long requestId = Interlocked.Increment(ref LatestRequestId);
            commandSegments = commandSegments.Select(commandSegment => commandSegment is string ? $"\"{commandSegment}\"" : (commandSegment.ToString() ?? string.Empty));
            string command = $"{{ command: [ {string.Join(", ", commandSegments)} ], request_id: {requestId} }}";

            Debug.WriteLine($"Debug - Sending {(waitForResponse ? "waiting " : string.Empty)}command: {command}");

            CancellationToken socketWaitToken = new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds)).Token;
            while (Socket == null || !Socket.Responding || Socket.HasExited)
            {
                if (socketWaitToken.IsCancellationRequested)
                {
                    await LoggingService.LogError("Socket Process Timeout", $"Command \"{command}\" could not be processed since process is not available.");
                    return null;
                }

                await Task.Delay(100);
            }

            CancellationToken playerWaitToken = new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds)).Token;
            while (Player == null || !Player.Responding || Player.HasExited)
            {
                if (playerWaitToken.IsCancellationRequested)
                {
                    await LoggingService.LogError("Player Process Timeout", $"Command \"{command}\" could not be processed since process is not available.");
                    return null;
                }

                await Task.Delay(100);
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
                        await LoggingService.LogError("Socket Command Failure", $"Command \"{command}\" received \"{result.Error}\".");
                        return null;
                    }

                    await LoggingService.LogDebug("Socket Command Response", $"Got \"{result.Data}\" for command \"{command}\".");
                    return result.Data;
                }
                catch (TaskCanceledException)
                {
                    await LoggingService.LogError("Socket Command Timeout", $"Command \"{command}\" timedout after {ProcessTimeoutSeconds} second(s).");
                }

            }

            return null;
        }

        private async Task UpdateCurrentTime()
        {
            object? currentTime = await SendCommandAsync(new string[] { "get_property", "time-pos" }, true);

            if (currentTime != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentTime), Convert.ToDouble(currentTime));
                await UpdateClients();
            }
        }

        private async Task UpdateClients()
        {
            await DeviceHubContext.Clients.All.SendAsync("UpdateDevice", Device);
        }

        private async Task PlayMedia()
        {
            if (Device.CurrentMedia == null)
                return;

            string playLocation = string.Empty;
            if (Device.CurrentMedia.Cache)
            {
                CacheItem? cacheItem = await CachingService.GetCacheItem(Device.CurrentMedia);
                if (cacheItem != null)
                {
                    CacheItem = cacheItem;
                    cacheItem.PropertyChanged += CacheItem_PropertyChanged;
                    Device.CurrentCacheRatio = CacheItem.CachedRatio;

                    if (CacheItem.IsReady)
                        playLocation = cacheItem.CacheFileInfo.FullName;
                    else
                        SendStatusMessage("Caching media...");
                }
            }

            if (string.IsNullOrWhiteSpace(playLocation))
            {
                UriCreationOptions options = new();
                if (Uri.TryCreate(Device.CurrentMedia.Location, in options, out Uri? parsedUri) && parsedUri != null)
                    playLocation = parsedUri.ToString();
                else
                    SendStatusMessage("Invalid URI!");
            }

            if (!string.IsNullOrWhiteSpace(playLocation))
            {
                UpdateDeviceProperty(nameof(Device.CurrentTime), 0);
                await SetDeviceStatus(DeviceStatus.Starting);

                await SendCommandAsync(new string[] { "loadfile", playLocation, "replace", $"start={Device.CurrentMedia.StartTime},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\"" }, true);
            }
            else
                await LoggingService.LogError("Player Service", $"Could not get a valid location from cache or URI: \"{Device.CurrentMedia.Location}\".");
            
        }

        private async Task StopMedia()
        {
            if (CacheItem != null)
            {
                CacheItem.PropertyChanged -= CacheItem_PropertyChanged;
                CacheItem.CacheCancellationTokenSource.Cancel();
                CacheItem = null;
            }

            if (Device.CurrentMedia != null && Device.DeviceStatus == DeviceStatus.Finished)
            {
                await SetDeviceStatus(DeviceStatus.Stopping);

                Device.CurrentTime = 0;
                Device.CurrentMedia.StartTime = 0;                
            }
            else if (Device.CurrentMedia != null && IsMediaLoaded)
            {
                await SetDeviceStatus(DeviceStatus.Stopping);

                object? currentTime = await SendCommandAsync(new string[] { "get_property", "time-pos" }, true);

                if (currentTime != null)
                {
                    Device.CurrentTime = 0;
                    Device.CurrentMedia.StartTime = Math.Max(Convert.ToDouble(currentTime) - 5, 0);
                }

                await SendCommandAsync(new string[] { "stop" }, true);
            }
        }

        private async void CacheItem_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            if (sender != null)
            {
                CacheItem = (CacheItem)sender;
                Device.CurrentCacheRatio = CacheItem.CachedRatio;

                if (CacheItem.IsReady && Device.CurrentMedia != null && IsMediaLoaded)
                    await SendCommandAsync(new string[] { "loadfile", CacheItem.CacheFileInfo.FullName, "replace", $"start={Device.CurrentTime}{(Device.DeviceStatus == DeviceStatus.Pausing || Device.DeviceStatus == DeviceStatus.Paused ? ",pause" : String.Empty)},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\"" }, true);
            }
        }

        private async Task SetDeviceStatus(DeviceStatus deviceStatus)
        {
            Debug.WriteLine($"Debug - Setting status: {deviceStatus}");

            UpdateDeviceProperty(nameof(Device.DeviceStatus), deviceStatus);
            await UpdateClients();
        }

        private async void SendStatusMessage(string statusMessage)
        {
            UpdateDeviceProperty(nameof(Device.StatusMessage), statusMessage);
            await UpdateClients();
            _ = Task.Run(async () => {
                await Task.Delay(3000);
                UpdateDeviceProperty(nameof(Device.StatusMessage), null);
                await UpdateClients();
            });            
        }

        private void UpdateDeviceProperty(string propertyName, object? value) 
        {
            Debug.WriteLine($"Debug - Updating property \"{propertyName}\" with value: {value}");

            typeof(Device).GetProperty(propertyName)?.SetValue(Device, value);            
        }

        #endregion

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    PeriodicTimerCancellationTokenSource.Cancel();
                }

                Player?.Dispose();
                Player = null;
                Socket?.Dispose();
                Socket = null;
                DisposedValue = true;
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
