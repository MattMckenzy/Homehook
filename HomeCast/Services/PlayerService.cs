using HomeCast.Extensions;
using HomeCast.Models;
using HomeHook.Common.Models;
using HomeHook.Common.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace HomeCast.Services
{
    public class PlayerService : IDisposable
    {
        #region Constants

        private const string DefaultVersion = "1.0.0";
        private const string MPVLocation = "mpv";
        private const int CacheSeconds = 30;
        private const string CacheDir = "/tmp/mpv-cache";

        private const string SocatLocation = "socat";
        private const string SocketFile = "/tmp/mpv-socket";

        private const int ProcessTimeoutSeconds = 10;
        private const int PeriodicTimerIntervalSeconds = 1;

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
        private Process? Socket { get; set; }

        private ConcurrentDictionary<int, WaitingCommand> WaitingCommands { get; } = new();

        private int LatestRequestId = 1;
        private bool DisposedValue { get; set; }

        private PeriodicTimer PeriodicTimer { get; } = new(TimeSpan.FromSeconds(PeriodicTimerIntervalSeconds));
        private CancellationTokenSource PeriodicTimerCancellationTokenSource { get; } = new();
        
        private ConcurrentQueue<(Delegate @Task, object?[]? Arguments)> TaskQueue { get; } = new();
        private bool IsTaskQueueProcessing { get; set; } = false;

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

            StartProcesses();

            StartPlayerTick();
        }

        #endregion

        #region Public Methods

        public async Task UpdateClients()
        {
            await DeviceHubContext.Clients.All.SendAsync("UpdateDevice", Device);
        }

        public async Task PlayAsync()
        {
            if (Device.DeviceStatus == DeviceStatus.Paused)
            {
                await SetDeviceStatus(DeviceStatus.Unpausing);

                await WaitedCommandAsync(new string[] { "set", "pause", "no" });

                await SetDeviceStatus(DeviceStatus.Playing);
            }
            else if (Device.CurrentMediaIndex != null && Device.MediaQueue.Any())
            {
                await StopMedia();
                await PlayMedia();
            }
        }

        public async Task StopAsync()
        {
            await StopMedia();

            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), null);
            MediaQueueClear();

            await UpdateClients();
        }

        public async Task PauseAsync()
        {
            if (Device.DeviceStatus == DeviceStatus.Playing)
            {
                await SetDeviceStatus(DeviceStatus.Pausing);

                await WaitedCommandAsync(new string[] { "set", "pause", "yes" });

                await SetDeviceStatus(DeviceStatus.Paused);
            }
        }

        public async Task NextAsync()
        {
            if (Device.CurrentMediaIndex != null)
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Min((int)Device.CurrentMediaIndex + 1, Device.MediaQueue.Count - 1));
                await PlayMedia();
            }
        }

        public async Task PreviousAsync()
        {
            if (Device.CurrentMediaIndex != null)
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), Math.Max((int)Device.CurrentMediaIndex - 1, 0));
                await PlayMedia();
            }
        }

        public async Task SeekAsync(float timeToSeek)
        {
            if (Device.CurrentMedia != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentTime), Math.Min(Math.Max(timeToSeek, 0), Device.CurrentMedia.Runtime));
                await WaitedCommandAsync(new string[] { "seek", Device.CurrentTime.ToString(), "absolute" });
                await UpdateClients();
            }
        }

        public async Task SeekRelativeAsync(float timeDifference)
        {
            UpdateDeviceProperty(nameof(Device.CurrentTime), Math.Max(Math.Min(Device.CurrentTime + timeDifference, Device.CurrentMedia?.Runtime ?? 0), 0));
            await WaitedCommandAsync(new string[] { "seek", timeDifference.ToString() });
            await UpdateClients();
        }

        public async Task ChangeCurrentMediaAsync(int mediaId)
        {
            await StopMedia();
            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), mediaId);
            await PlayMedia();
        }

        public async Task ChangeRepeatModeAsync(RepeatMode repeatMode)
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

        public async Task SetPlaybackRateAsync(float playbackRate)
        {
            UpdateDeviceProperty(nameof(Device.PlaybackRate), playbackRate);

            await WaitedCommandAsync(new string[] { "set", "speed", Device.PlaybackRate.ToString() });

            await UpdateClients();
        }

        public async Task LaunchQueue(List<MediaItem> mediaItems)
        {
            await StopAsync();

            UpdateDeviceProperty(nameof(Device.CurrentMediaIndex), 0);
            UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);

            await PlayMedia();
        }

        public async Task UpdateQueueAsync(List<MediaItem> mediaItems)
        {
            UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);

            await UpdateClients();
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
            foreach (int itemId in itemIds.OrderByDescending(itemId => itemId))
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

            await WaitedCommandAsync(new string[] { "set", "volume", (Device.Volume * 100).ToString() });

            await UpdateClients();
        }

        public async Task ToggleMutedAsync()
        {
            UpdateDeviceProperty(nameof(Device.IsMuted), !Device.IsMuted);

            await WaitedCommandAsync(new string[] { "set", "mute", Device.IsMuted ? "yes" : "no" });

            await UpdateClients();
        }

        #endregion

        #region Process Methods

        private void StartProcesses()
        {
            bool playerRestarted = true;
            if (Player == null || Player.HasExited || !Player.Responding)
            {
                Player?.Dispose();

                bool videoCapable = Configuration.GetValue<bool>("Device:VideoCapable");

                List<string> playerArguments = new()
                {
                    "--idle",
                    "--force-seekable=yes",
                    "--really-quiet",
                    "--no-input-default-bindings",
                    "--msg-level=all=warn",
                    "--cache=yes",
                    $"--cache-secs={CacheSeconds}",
                    $"--cache-dir={CacheDir}",
                    $"--volume={Device.Volume * 100}",
                    $"--input-ipc-server={SocketFile}"
                };

                if (!videoCapable)
                    playerArguments.Add("--no-video");

                Player = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = MPVLocation,
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
            }

            if (Socket == null || Socket.HasExited || !Socket.Responding)
            {
                List<string> socketArguments = new()
                {
                    "-",
                    SocketFile,
                };

                Socket = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = SocatLocation,
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
                WaitedCommandAsync(new string[] { "disable_event", "all" }).GetAwaiter().GetResult();
                WaitedCommandAsync(new string[] { "enable_event", "file-loaded" }).GetAwaiter().GetResult();
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

                object? currentTime = await WaitedCommandAsync(new string[] { "get_property", "time-pos" });

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

                    if (waitingCommand.Success && waitingCommand.WaitingEvent != null)
                        WaitingCommands.AddOrUpdate(waitingCommand.RequestId, (requestId) => waitingCommand, (requestId, oldWaitingCommand) => waitingCommand);
                    else
                    {
                        WaitingCommands.TryRemove(waitingCommand.RequestId, out _);
                        waitingCommand.Callback.SetResult(waitingCommand);
                    }
                }
                else if (socketOutput.TryParseJson(out EventResponse? eventResponse) && eventResponse != null)
                {
                    if (WaitingCommands.Any())
                    {
                        foreach (WaitingCommand stillWaitingCommand in WaitingCommands.Values.ToArray())
                        {
                            if (stillWaitingCommand.WaitingEvent != null &&
                                stillWaitingCommand.WaitingEvent.Equals(eventResponse.Event, StringComparison.InvariantCultureIgnoreCase))
                            {
                                WaitingCommands.TryRemove(stillWaitingCommand.RequestId, out _);
                                stillWaitingCommand.Callback.SetResult(stillWaitingCommand);
                            }
                        }
                    }
                    else if (eventResponse.Event.Equals("end-file", StringComparison.InvariantCultureIgnoreCase) &&
                        Device.DeviceStatus != DeviceStatus.Stopped)
                    {
                        await LoggingService.LogDebug("Playback Finished", string.Empty);
                        await SetDeviceStatus(DeviceStatus.Finishing);

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
                }
            }
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

        private async Task<object?> WaitedCommandAsync(IEnumerable<string> commandSegments, string? waitingEvent = null, int timeoutSeconds = 1)
        {
            int requestId = Interlocked.Increment(ref LatestRequestId);
            string command = $"{{ command: [ \"{string.Join("\", \"", commandSegments)}\" ], request_id: {requestId} }}";

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

            WaitingCommand waitingCommand = new() 
            {
                RequestId = requestId, 
                Callback = new TaskCompletionSource<WaitingCommand>(), 
                WaitingEvent = waitingEvent 
            };

            WaitingCommands.AddOrUpdate(requestId, (requestId) => waitingCommand, (requestId, oldWaitingCommand) => waitingCommand);
            await Socket!.StandardInput.WriteLineAsync(command);

            try
            {
                WaitingCommand result = await waitingCommand.Callback.Task.WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)).Token);

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
                await LoggingService.LogError("Socket Command Timeout", $"Command \"{command}\" timedout after {timeoutSeconds} second(s).");
            }

            return null;
        }

        private async Task PlayMedia()
        {
            UriCreationOptions options = new();
            if (Uri.TryCreate(Device.CurrentMedia?.Location, in options, out Uri? parsedUri) && parsedUri != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentTime), 0);
                await SetDeviceStatus(DeviceStatus.Starting);

                await WaitedCommandAsync(new string[] { "loadfile", parsedUri.ToString(), "replace", $"start={Device.CurrentMedia.StartTime},title=\\\"{Device.CurrentMedia.Metadata.Title}\\\"" }, "file-loaded", 10);

                await SetDeviceStatus(DeviceStatus.Playing);
            }
            else
            {
                UpdateDeviceProperty(nameof(Device.StatusMessage), "URI not found!");
                await UpdateClients();

                await LoggingService.LogError("Media URI Not found", $"The given URI for the media Item was not valid: \"{Device.CurrentMedia?.Location}\"");
            }
        }

        private async Task StopMedia()
        {
            if (Device.DeviceStatus != DeviceStatus.Stopped)
            {
                if (Device.CurrentMedia != null)
                {
                    object? currentTime = await WaitedCommandAsync(new string[] { "get_property", "time-pos" });

                    if (currentTime != null)
                    {
                        lock (Device)
                        {
                            Device.CurrentMedia.StartTime = Convert.ToDouble(currentTime) - 5;
                        }
                    }
                }

                await SetDeviceStatus(DeviceStatus.Stopping);

                await WaitedCommandAsync(new string[] { "stop" });

                await SetDeviceStatus(DeviceStatus.Stopped);
            }
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
