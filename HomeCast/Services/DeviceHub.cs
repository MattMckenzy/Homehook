using HomeHook.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace HomeCast.Services
{
    [Authorize]
    public class DeviceHub : Hub
    {
        private PlayerService PlayerService { get; }

        public DeviceHub(PlayerService playerService)
        {
            PlayerService = playerService;
        }

        public async Task<Device> GetDevice() =>
            await PlayerService.GetDevice(); 

        public async Task UpdateMediaItemsSelection(IEnumerable<int> mediaItemIndices, bool IsSelected) =>
            await PlayerService.UpdateMediaItemsSelection(mediaItemIndices, IsSelected);

        public async Task PlaySelectedMediaItem() =>
            await PlayerService.PlaySelectedMediaItem();

        public async Task AddMediaItems(List<MediaItem> mediaItems, bool launch = false, bool insertBeforeSelectedMediaItem = false) =>
            await PlayerService.AddMediaItems(mediaItems, launch, insertBeforeSelectedMediaItem);

        public async Task RemoveSelectedMediaItems() =>
            await PlayerService.RemoveSelectedMediaItems();

        public async Task MoveSelectedMediaItemsUp() =>
            await PlayerService.MoveSelectedMediaItemsUp();

        public async Task MoveSelectedMediaItemsDown() =>
            await PlayerService.MoveSelectedMediaItemsDown();

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
            await PlayerService.ToggleMuted();
    }
}