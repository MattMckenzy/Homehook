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

        public async Task PlayAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Stopped, DeviceStatus.Paused, DeviceStatus.Finishing }.Contains(Device.DeviceStatus))
            {
                if (Device.DeviceStatus == DeviceStatus.Paused)
                {
                    await SetDeviceStatus(DeviceStatus.Unpausing);

                    await WaitedCommandAsync(new string[] { "set", "pause", "no" });

                    await SetDeviceStatus(DeviceStatus.Playing);
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
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
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

                await WaitedCommandAsync(new string[] { "set", "pause", "yes" });

                await SetDeviceStatus(DeviceStatus.Paused);
            }
        }

        public async Task NextAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped, DeviceStatus.Finishing }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.ElementAt(Math.Min(Device.MediaQueue.IndexOf(Device.CurrentMedia) + 1, Device.MediaQueue.Count - 1)).Id);
                await PlayMedia();
            }
        }

        public async Task PreviousAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.ElementAt(Math.Max(Device.MediaQueue.IndexOf(Device.CurrentMedia) - 1, 0)).Id);
                await PlayMedia();
            }            
        }

        public async Task SeekAsync(float timeToSeek)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentTime), Math.Min(Math.Max(timeToSeek, 0), Device.CurrentMedia.Runtime));
                await WaitedCommandAsync(new string[] { "seek", Device.CurrentTime.ToString(), "absolute" });
                await UpdateClients();
            }
        }

        public async Task SeekRelativeAsync(float timeDifference)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.CurrentMedia != null)
            {
                UpdateDeviceProperty(nameof(Device.CurrentTime), Math.Max(Math.Min(Device.CurrentTime + timeDifference, Device.CurrentMedia?.Runtime ?? 0), 0));
                await WaitedCommandAsync(new string[] { "seek", timeDifference.ToString() });
                await UpdateClients();
            }
        }

        public async Task ChangeCurrentMediaAsync(string mediaId)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
                Device.MediaQueue.Any(mediaItem => mediaItem.Id == mediaId))
            {
                await StopMedia();
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), mediaId);
                await PlayMedia();
            }
        }

        public async Task ChangeRepeatModeAsync(RepeatMode repeatMode)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
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
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                UpdateDeviceProperty(nameof(Device.PlaybackRate), playbackRate);

                await WaitedCommandAsync(new string[] { "set", "speed", Device.PlaybackRate.ToString() });

                await UpdateClients();
            }
        }

        public async Task LaunchQueue(List<MediaItem> mediaItems)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                await StopAsync();

                UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);
                UpdateDeviceProperty(nameof(Device.CurrentMediaId), Device.MediaQueue.First().Id);

                await PlayMedia();
            }
        }

        public async Task UpdateQueueAsync(List<MediaItem> mediaItems)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                UpdateDeviceProperty(nameof(Device.MediaQueue), mediaItems);

                await UpdateClients();
            }
        }

        public async Task InsertQueueAsync(List<MediaItem> mediaItems, string? insertBefore)
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
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
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
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
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
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
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus) &&
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
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                UpdateDeviceProperty(nameof(Device.Volume), Math.Min(Math.Max(volume, 0), 1));

                await WaitedCommandAsync(new string[] { "set", "volume", (Device.Volume * 100).ToString() });

                await UpdateClients();
            }
        }

        public async Task ToggleMutedAsync()
        {
            if (new DeviceStatus[] { DeviceStatus.Playing, DeviceStatus.Paused, DeviceStatus.Stopped }.Contains(Device.DeviceStatus))
            {
                UpdateDeviceProperty(nameof(Device.IsMuted), !Device.IsMuted);

                await WaitedCommandAsync(new string[] { "set", "mute", Device.IsMuted ? "yes" : "no" });

                await UpdateClients();
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

                bool videoCapable = Configuration.GetValue<bool>("Device:VideoCapable");

                // TODO: Add yt-dlp to mpv process and container 
                // TODO: Add caching with yt-dlp
                // TODO: Add event hooks for updating settings from OSD

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

                Task.Delay(3000).GetAwaiter().GetResult();
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
                Task.Delay(1000).GetAwaiter().GetResult();
                WaitedCommandAsync(new string[] { "disable_event", "all" }).GetAwaiter().GetResult();
                WaitedCommandAsync(new string[] { "enable_event", "file-loaded" }).GetAwaiter().GetResult();
                WaitedCommandAsync(new string[] { "enable_event", "end-file" }).GetAwaiter().GetResult();
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
                    else if(eventResponse.Event.Equals("end-file", StringComparison.InvariantCultureIgnoreCase) &&
                        Device.DeviceStatus != DeviceStatus.Stopped)
                    {
                        _ = Task.Run(ProcessEndFile);
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

        private async Task<object?> WaitedCommandAsync(IEnumerable<string> commandSegments, string? waitingEvent = null, int timeoutSeconds = 3)
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
                Callback = new TaskCompletionSource<WaitingCommand>(TaskCreationOptions.RunContinuationsAsynchronously), 
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

        private async Task UpdateClients()
        {
            await DeviceHubContext.Clients.All.SendAsync("UpdateDevice", Device);
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
                await SetDeviceStatus(DeviceStatus.Stopping);

                object? currentTime = await WaitedCommandAsync(new string[] { "get_property", "time-pos" });               

                if (currentTime != null)
                {
                    lock (Device)
                    {
                        Device.CurrentTime = 0;
                        Device.CurrentMedia.StartTime = Math.Max(Convert.ToDouble(currentTime) - 5, 0);
                    }
                }
                 
                await WaitedCommandAsync(new string[] { "stop" });

                await SetDeviceStatus(DeviceStatus.Stopped);
            }
        }

        private async Task ProcessEndFile()
        {
            await LoggingService.LogDebug("Playback Finished", string.Empty);
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


        private async Task SetDeviceStatus(DeviceStatus deviceStatus)
        {
            UpdateDeviceProperty(nameof(Device.DeviceStatus), deviceStatus);
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
