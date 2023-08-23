using HomeHook.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HomeCast.Services
{
    [Authorize]
    public class DeviceHub : Hub
    {
        private PlayerService PlayerService { get; }
        private CommandService CommandService { get; }

        public DeviceHub(PlayerService playerService, CommandService commandService)
        {
            PlayerService = playerService;
            CommandService = commandService;
        }

        public async Task<Device> GetDevice() =>
            await PlayerService.GetDevice(); 

        public async Task PlayMediaItem(string mediaItemId) =>
            await PlayerService.PlayMediaItem(mediaItemId);

        public async Task AddMediaItems(List<MediaItem> mediaItems, bool launch = false, string? insertBeforeMediaItemId = null) =>
            await PlayerService.AddMediaItems(mediaItems, launch, insertBeforeMediaItemId);

        public async Task RemoveMediaItems(IEnumerable<string> mediaItemIds) =>
            await PlayerService.RemoveMediaItems(mediaItemIds);

        public async Task MoveMediaItemsUp(IEnumerable<string> mediaItemIds) =>
            await PlayerService.MoveMediaItemsUp(mediaItemIds);

        public async Task MoveMediaItemsDown(IEnumerable<string> mediaItemIds) =>
            await PlayerService.MoveMediaItemsDown(mediaItemIds);

        public async Task Play() =>
            await PlayerService.Play();

        public async Task Stop() =>
            await PlayerService.Stop();

        public async Task Pause() =>
            await PlayerService.Pause();

        public async Task Next() =>
            await PlayerService.Next();

        public async Task Previous() =>
            await PlayerService.Previous();

        public async Task Seek(float timeToSeek) =>
            await PlayerService.Seek(timeToSeek);

        public async Task SeekRelative(float timeDifference) =>
            await PlayerService.SeekRelative(timeDifference);

        public async Task ChangeRepeatMode(RepeatMode repeatMode) =>
            await PlayerService.ChangeRepeatMode(repeatMode);

        public async Task SetPlaybackRate(float playbackRate) =>
            await PlayerService.SetPlaybackRate(playbackRate);

        public async Task SetVolume(float volume) =>
            await PlayerService.SetVolume(volume);

        public async Task ToggleMute() =>
            await PlayerService.ToggleMute();

        public Task<IEnumerable<CommandDefinition>> GetCommands() =>
            Task.FromResult(CommandService.CommandDefinitions);

        public async Task CallCommand(string Name) =>
            await CommandService.CallCommand(Name);
    }
}