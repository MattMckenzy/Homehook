using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using HomeHook.Common.Models;
using System.Collections.Concurrent;
using HomeHook.Common.Services;

namespace HomeCast.Services
{
    [Authorize]
    public class DeviceHub : Hub
    {
        private const int CommandMillisecondsTimeout = 15000;

        private PlayerService PlayerService { get; }
        private LoggingService<DeviceHub> LoggingService { get; }

        private ConcurrentQueue<(Delegate Command, object?[]? Arguments)> CommandQueue { get; } = new();
        private bool IsCommandQueueProcessing { get; set; } = false;

        public DeviceHub(PlayerService playerService, LoggingService<DeviceHub> loggingService)
        {
            PlayerService = playerService;
            LoggingService = loggingService;
        }

        public Device GetDevice() =>
            PlayerService.Device;

        public async Task Play() =>
            await QueueCommand(PlayerService.PlayAsync);

        public async Task Stop() =>
            await QueueCommand(PlayerService.StopAsync);

        public async Task Pause() =>
            await QueueCommand(PlayerService.PauseAsync);

        public async Task Next() =>
            await QueueCommand(PlayerService.NextAsync);

        public async Task Previous() =>
            await QueueCommand(PlayerService.PreviousAsync);

        public async Task Seek(float timeToSeek) =>
            await QueueCommand(PlayerService.SeekAsync, new object?[] { timeToSeek });

        public async Task SeekRelative(float timeDifference) =>
            await QueueCommand(PlayerService.SeekRelativeAsync, new object?[] { timeDifference });

        public async Task ChangeCurrentMedia(string mediaId) =>
            await QueueCommand(PlayerService.ChangeCurrentMediaAsync, new object?[] { mediaId });

        public async Task ChangeRepeatMode(RepeatMode repeatMode) =>
            await QueueCommand(PlayerService.ChangeRepeatModeAsync, new object?[] { repeatMode });

        public async Task SetPlaybackRate(float playbackRate) =>
            await QueueCommand(PlayerService.SetPlaybackRateAsync, new object?[] { playbackRate });

        public async Task LaunchQueue(List<MediaItem> mediaItems) =>
            await QueueCommand(PlayerService.LaunchQueue, new object?[] { mediaItems });

        public async Task InsertQueue(List<MediaItem> mediaItems, string? insertBefore) =>
            await QueueCommand(PlayerService.InsertQueueAsync, new object?[] { mediaItems, insertBefore });

        public async Task UpdateQueue(List<MediaItem> mediaItems) =>
            await QueueCommand(PlayerService.UpdateQueueAsync, new object?[] { mediaItems });

        public async Task RemoveQueue(IEnumerable<string> mediaIds) =>
            await QueueCommand(PlayerService.RemoveQueueAsync, new object?[] { mediaIds });

        public async Task UpQueue(IEnumerable<string> mediaIds) =>
            await QueueCommand(PlayerService.UpQueueAsync, new object?[] { mediaIds });

        public async Task DownQueue(IEnumerable<string> mediaIds) =>
            await QueueCommand(PlayerService.DownQueueAsync, new object?[] { mediaIds });

        public async Task SetVolume(float volume) =>
            await QueueCommand(PlayerService.SetVolumeAsync, new object?[] { volume });

        public async Task ToggleMute() =>
            await QueueCommand(PlayerService.ToggleMutedAsync);

        private async Task QueueCommand(Delegate command, object?[]? arguments = null)
        {
            CommandQueue.Enqueue((command, arguments));
            if (!IsCommandQueueProcessing)
            {
                IsCommandQueueProcessing = true;

                while (CommandQueue.TryDequeue(out (Delegate Command, object?[]? Arguments) result))
                {
                    try
                    {
                        Task? commandTask = (Task?)result.Command.DynamicInvoke(result.Arguments);

                        if (commandTask != null)
                            await commandTask.WaitAsync(new CancellationTokenSource(CommandMillisecondsTimeout).Token);
                    }
                    catch (TaskCanceledException)
                    {
                        await LoggingService.LogError("Command Timeout", $"The command \"{result.Command.Method.Name}\" timed out.");
                    }
                }

                IsCommandQueueProcessing = false;
            }
        }
    }
}