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

        #region Public Properties

        public Device Device { get; }

        #endregion

        #region Private Properties

        private string MPVSocketLocation { get; }
        private int ProcessTimeoutSeconds { get; }

        private Random Random { get; } = new(DateTime.Now.Ticks.GetHashCode());

        private Process? Player { get; set; }
        private Process? Socket { get; set; }

        private bool DisposedValue { get; set; }

        private PeriodicTimer PeriodicTimer { get; } = new(TimeSpan.FromSeconds(1));
        private CancellationTokenSource PeriodicTimerCancellationTokenSource { get; } = new();
        
        private ConcurrentQueue<(Delegate @Task, object?[]? Arguments)> TaskQueue { get; } = new();
        private bool IsTaskQueueProcessing { get; set; } = false;

        private CacheItem? CacheItem { get; set; }

        private ConcurrentDictionary<long, WaitingCommand> WaitingCommands { get; } = new();
        private long LatestRequestId = 0;

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

            Device = new()
            {
                Name = name,
                Address = address,
                Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0",
            };

            ScriptsProcessor.DeviceUpdate += UpdateDevice;

            StartProcesses();

            StartPlayerTick();
        }

        #endregion

        #region Public Methods

        public async Task PlayAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Stopped, DeviceStatus.Paused, DeviceStatus.Finishing }.Contains(Device.DeviceStatus))
            {
                if (Device.DeviceStatus == DeviceStatus.Paused)
                {
                    await SetDeviceStatus(DeviceStatus.Unpausing);

                    await SendCommandAsync(new string[] { "set", "pause", "no" });
                }
                else if (Device.CurrentMedia != null && Device.MediaQueue.Any())
                {
                    await StopMedia();
                    await PlayMedia();
                }
            }
        }

        public async Task StopAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                await StopMedia();

                UpdateDeviceProperty(nameof(Device.CurrentMediaId), null);
                MediaQueueClear();

                await UpdateClients();
            }
        }

        public async Task PauseAsync()
        {
            if (Device.DeviceStatus == DeviceStatus.Playing)
            {
                await SetDeviceStatus(DeviceStatus.Pausing);

                await SendCommandAsync(new string[] { "set", "pause", "yes" });
            }
        }

        public async Task NextAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped, DeviceStatus.Finishing }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.ElementAt(Math.Min(Device.MediaQueue.IndexOf(Device.CurrentMedia) + 1, Device.MediaQueue.Count - 1)).Id);
                await PlayMedia();
            }
        }

        public async Task PreviousAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.ElementAt(Math.Max(Device.MediaQueue.IndexOf(Device.CurrentMedia) - 1, 0)).Id);
                await PlayMedia();
            }            
        }

        public async Task SeekAsync(float timeToSeek)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                await SendCommandAsync(new string[] { "seek", Math.Min(Math.Max(timeToSeek, 0), Device.CurrentMedia.Runtime).ToString(), "absolute" });
            }
        }

        public async Task SeekRelativeAsync(float timeDifference)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {              
                await SendCommandAsync(new string[] { "seek", timeDifference.ToString() });
            }
        }

        public async Task ChangeCurrentMediaAsync(string mediaId)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.MediaQueue.Any(mediaItem => mediaItem.Id == mediaId))
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), mediaId);
                await PlayMedia();
            }
        }

        public async Task ChangeRepeatModeAsync(RepeatMode repeatMode)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                if (Device.RepeatMode != RepeatMode.Shuffle && repeatMode == RepeatMode.Shuffle)
                {
                    await StopMedia();

                    UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.OrderBy(_ => Random.Next()).ToList());
                    UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.First().Id);

                    await PlayMedia();
                }

                UpdateDeviceProperty(nameof(Device.RepeatMode), repeatMode);
                await UpdateClients();
            }

        }

        public async Task SetPlaybackRateAsync(float playbackRate)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                await SendCommandAsync(new string[] { "set", "speed", playbackRate.ToString() });
            }
        }

        public async Task LaunchQueue(List<MediaItem> mediaItems)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                await StopAsync();

                UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.First().Id);

                await PlayMedia();
            }
        }

        public async Task UpdateQueueAsync(List<MediaItem> mediaItems)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);

                await UpdateClients();
            }
        }

        public async Task InsertQueueAsync(List<MediaItem> mediaItems, string? insertBefore)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                if (insertBefore == null || !Device.MediaQueue.Any(media => media.Id == insertBefore))
                    MediaQueueAddRange(mediaItems);
                else
                    MediaQueueInsertRange(mediaItems, Device.MediaQueue.IndexOf(Device.MediaQueue.First(mediaItem => mediaItem.Id == insertBefore)));

                await UpdateClients();
            }
        }

        public async Task RemoveQueueAsync(IEnumerable<string> mediaIds)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.MediaQueue.Any())
            {
                if (Device.MediaQueue.All(mediaItem => mediaIds.Contains(mediaItem.Id)))
                    await StopAsync();

                int? currentMediaIndex = null;
                if (Device.CurrentMedia != null && mediaIds.Contains(Device.CurrentMediaId))
                    currentMediaIndex = Device.MediaQueue.IndexOf(Device.CurrentMedia);

                foreach (int itemIndex in Device.MediaQueue.ToArray()
                    .Select((item, index) => (item, index))
                    .Reverse()
                    .Where(mediaQueueItem => mediaIds.Contains(mediaQueueItem.item.Id))
                    .Select(mediaQueueItem => mediaQueueItem.index))
                {
                    MediaQueueRemoveAt(itemIndex);
                }

                if (currentMediaIndex != null)
                {
                    Device.CurrentMediaId = Device.MediaQueue.ElementAt(Math.Min((int)currentMediaIndex, Device.MediaQueue.Count - 1)).Id;
                    await StopMedia();
                    await PlayMedia();
                }

                await UpdateClients();
            }
        }

        public async Task UpQueueAsync(IEnumerable<string> mediaIds)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.MediaQueue.Any())
            {
                foreach (int mediaIndex in Device.MediaQueue.ToArray()
                    .Select((item, index) => (item.Id, index))
                    .SkipWhile(mediaQueueItem => mediaIds.Contains(mediaQueueItem.Id))
                    .Where(mediaQueueItem => mediaIds.Contains(mediaQueueItem.Id))
                    .Select(mediaQueueItem => mediaQueueItem.index))
                    UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.MoveUp(mediaIndex));                

                await UpdateClients();
            }
        }

        public async Task DownQueueAsync(IEnumerable<string> mediaIds)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.MediaQueue.Any())
            {
                foreach (int itemIndex in Device.MediaQueue.ToArray()
                    .Select((item, index) => (item.Id, index))
                    .Reverse()
                    .SkipWhile(mediaQueueItem => mediaIds.Contains(mediaQueueItem.Id))
                    .Where(mediaQueueItem => mediaIds.Contains(mediaQueueItem.Id))
                    .Select(mediaQueueItem => mediaQueueItem.index))
                    UpdateDeviceProperty(nameof(Device.MediaQueue), Device.MediaQueue.MoveDown(itemIndex));                

                await UpdateClients();
            }
        }

        public async Task SetVolumeAsync(float volume)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                await SendCommandAsync(new string[] { "set", "volume", (Math.Min(Math.Max(volume, 0), 1) * 100).ToString() });
            }
        }

        public async Task ToggleMutedAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Buffering, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                await SendCommandAsync(new string[] { "set", "mute", !Device.IsMuted ? "yes" : "no" });
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

                // TODO: Add HDMI-CEC support
                // TODO: Convert queue to MPV playlist so OSD gets control.
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
                    "--no-input-default-bindings",
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
                SendCommandAsync(new string[] { "disable_event", "all" }).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 1, "volume" }).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 2, "mute" }).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 3, "pause" }).GetAwaiter().GetResult();
                SendCommandAsync(new object[] { "observe_property", 4, "speed" }).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "enable_event", "end-file" }).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "enable_event", "seek" }).GetAwaiter().GetResult();
                SendCommandAsync(new string[] { "enable_event", "playback-restart" }).GetAwaiter().GetResult();
            }
        }

        private async void Player_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogWarning("Player Warning", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void Player_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogError("Player Error", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void Player_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogError("Player Exited", "Restarting...");

            StartProcesses();
        }

        private async void StartPlayerTick()
        {
            while (await PeriodicTimer.WaitForNextTickAsync())
            {
                if (PeriodicTimerCancellationTokenSource.Token.IsCancellationRequested || Device.DeviceStatus != DeviceStatus.Playing)
                    continue;

                object? currentTime = await SendCommandAsync(new string[] { "get_property", "time-pos" });

                if (currentTime != null)
                {
                    UpdateDeviceProperty(nameof(Device.CurrentTime), Convert.ToDouble(currentTime));
                    await UpdateClients();
                }
            }
        }

        private async void Socket_OutputDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await QueueTask(ProcessSocketOutput, new object?[] { dataReceivedEventArgs.Data });
        }

        private async Task ProcessSocketOutput(string? socketOutput)
        {
            if (socketOutput != null)
            {
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
                                if (isPaused)
                                    await SetDeviceStatus(DeviceStatus.Paused);
                                else
                                    await SetDeviceStatus(DeviceStatus.Playing);
                            }
                            break;

                        case "end-file":
                            await ProcessEndFile(eventResponse.Reason);
                            break;

                        case "seek":
                            await SetDeviceStatus(DeviceStatus.Buffering);
                            break;

                        case "playback-restart":
                            await SetDeviceStatus(DeviceStatus.Playing);
                            break;
                    }                
                }
            }

            await Task.CompletedTask;
        }

        private async void Socket_ErrorDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            await LoggingService.LogError("Socket Error", dataReceivedEventArgs.Data ?? "N/A");
        }

        private async void Socket_Exited(object? sender, EventArgs eventArgs)
        {
            await LoggingService.LogError("Socket Exited", "Restarting...");

            StartProcesses();
        }

        #endregion

        #region Private Methods

        private async Task QueueTask(Delegate @task, object?[]? arguments)
        {
            TaskQueue.Enqueue((@task, arguments));
            if (!IsTaskQueueProcessing)
            {
                IsTaskQueueProcessing = true;

                while (TaskQueue.TryDequeue(out (Delegate @Task, object?[]? Arguments) resultTask))
                {
                    try
                    {
                        Task? taskTask = (Task?)resultTask.Task.DynamicInvoke(resultTask.Arguments);

                        if (taskTask != null)
                            await taskTask.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds)).Token);
                    }
                    catch (TaskCanceledException)
                    {
                        await LoggingService.LogError("Task Timeout", $"The task \"{resultTask.Task.Method.Name}\" timed out.");
                    }
                }

                IsTaskQueueProcessing = false;
            }
        }

        private async Task<object?> SendCommandAsync(IEnumerable<object> commandSegments, bool waitForResponse = false)
        {
            long requestId = Interlocked.Increment(ref LatestRequestId);
            commandSegments = commandSegments.Select(commandSegment => commandSegment is string ? $"\"{commandSegment}\"" : (commandSegment.ToString() ?? string.Empty));
            string command = $"{{ command: [ {string.Join(", ", commandSegments)} ], request_id: {requestId} }}";

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

                await SendCommandAsync(new string[] { "loadfile", playLocation, "replace", $"start={Device.CurrentMedia.StartTime},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\"" });
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

            if (Device.CurrentMedia != null && Device.DeviceStatus == DeviceStatus.Finishing)
            {
                await SetDeviceStatus(DeviceStatus.Stopping);

                lock (Device)
                {
                    Device.CurrentTime = 0;
                    Device.CurrentMedia.StartTime = 0;
                }

                await SetDeviceStatus(DeviceStatus.Stopped);
            }
            else if (Device.CurrentMedia != null && Device.DeviceStatus != DeviceStatus.Stopped)
            {
                object? currentTime = await SendCommandAsync(new string[] { "get_property", "time-pos" });

                if (currentTime != null)
                {
                    lock (Device)
                    {
                        Device.CurrentTime = 0;
                        Device.CurrentMedia.StartTime = Math.Max(Convert.ToDouble(currentTime) - 5, 0);
                    }
                }

                await SetDeviceStatus(DeviceStatus.Stopping);

                await SendCommandAsync(new string[] { "stop" });
            }
        }

        private async void CacheItem_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            if (sender != null)
            {
                CacheItem = (CacheItem)sender;
                if (CacheItem.IsReady && Device.CurrentMedia != null && new DeviceStatus[] { DeviceStatus.Finishing, DeviceStatus.Stopping, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) == false)
                    await SendCommandAsync(new string[] { "loadfile", CacheItem.CacheFileInfo.FullName, "replace", $"start={Device.CurrentTime}{(Device.DeviceStatus == DeviceStatus.Pausing || Device.DeviceStatus == DeviceStatus.Paused ? ",pause" : String.Empty)},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\"" });

                Device.CurrentCacheRatio = CacheItem.CachedRatio;
            }
        }

        private async Task ProcessEndFile(string reason)
        {
            await LoggingService.LogDebug("Playback Finished", string.Empty);

            if (reason == "eof")
            {
                await SetDeviceStatus(DeviceStatus.Finishing);

                switch (Device.RepeatMode)
                {
                    case RepeatMode.Off:
                        if (Device.CurrentMediaId == Device.MediaQueue.Last().Id)
                        {
                            UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.First().Id);
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
                        if (Device.CurrentMediaId == Device.MediaQueue.Last().Id)
                        {
                            UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.First().Id);
                            await PlayAsync();
                        }
                        else
                            await NextAsync();
                        break;
                }
            }
            else
                await SetDeviceStatus(DeviceStatus.Stopped);
        }


        private async Task SetDeviceStatus(DeviceStatus deviceStatus)
        {
            UpdateDeviceProperty(nameof(Device.DeviceStatus), deviceStatus);
            await UpdateClients();
        }

        private async void SendStatusMessage(string statusMessage)
        {
            UpdateDeviceProperty(nameof(Device.StatusMessage), statusMessage);
            await UpdateClients();
            await Task.Delay(3000);
            UpdateDeviceProperty(nameof(Device.StatusMessage), null);
            await UpdateClients();
        }

        private void UpdateDevice(object? _, DeviceUpdateEventArgs deviceUpdateEventArgs) 
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

        private void MediaQueueInsertRange(IEnumerable<MediaItem> mediaItems, int insertAtIndex)
        {
            lock (Device)
            {
                Device.MediaQueue.InsertRange(insertAtIndex, mediaItems);
            }
        }

        private void MediaQueueRemoveAt(int mediaIndex)
        {
            lock (Device)
            {
                Device.MediaQueue.RemoveAt(mediaIndex);
            }
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
