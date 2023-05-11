using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using HomeHook.Common.Models;
using HomeCast.Services;

namespace HomeCast
{
    [Authorize]
    public class DeviceHub : Hub
    {
        private PlayerService PlayerService { get; }

        public DeviceHub(PlayerService playerService)
        {
            PlayerService = playerService;
        }

        public Device GetDevice() =>
            PlayerService.Device;

        public async Task Play() =>
            await PlayerService.PlayAsync();

        public async Task Stop() =>
            await PlayerService.StopAsync();

        public async Task Pause() =>
            await PlayerService.PauseAsync();

        public async Task Next() =>
            await PlayerService.NextAsync();

        public async Task Previous() =>
            await PlayerService.PreviousAsync();

        public async Task Seek(float timeToSeek) =>
            await PlayerService.SeekAsync(timeToSeek);

        public async Task ChangeCurrentMedia(int mediaId) =>
            await PlayerService.ChangeCurrentMediaAsync(mediaId);

        public async Task ChangeRepeatMode(RepeatMode repeatMode) =>
            await PlayerService.ChangeRepeatModeAsync(repeatMode);

        public async Task SetPlaybackRate(float playbackRate) =>
            await PlayerService.SetPlaybackRateAsync(playbackRate);

        public async Task LaunchQueue(List<MediaItem> mediaItem) =>
            await PlayerService.LaunchQueue(mediaItem);

        public async Task InsertQueue(List<MediaItem> media, int? insertBefore) =>
            await PlayerService.InsertQueueAsync(media, insertBefore);        

        public async Task RemoveQueue(IEnumerable<int> itemIds) =>
            await PlayerService.RemoveQueueAsync(itemIds);

        public async Task UpQueue(IEnumerable<int> itemIds) =>
            await PlayerService.UpQueueAsync(itemIds);

        public async Task DownQueue(IEnumerable<int> itemIds) =>
            await PlayerService.DownQueueAsync(itemIds);

        public async Task SetVolume(float volume) =>
            await PlayerService.SetVolumeAsync(volume);

        public async Task ToggleMute() =>
            await PlayerService.ToggleMutedAsync();
    }
}