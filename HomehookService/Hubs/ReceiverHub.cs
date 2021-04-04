using GoogleCast.Models.Media;
using HomehookCommon.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Hubs
{
    public class ReceiverHub : Hub
    {
        private readonly CastService _castService;

        public ReceiverHub(CastService castService)
        {
            _castService = castService;
        }

        public Task<IEnumerable<string>> GetReceivers() =>
            Task.FromResult(_castService.ReceiverServices.Select(receiverService => receiverService.Receiver.FriendlyName));

        public async Task<ReceiverStatus> GetStatus(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).GetReceiverStatus();

        public async Task Play(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).PlayAsync();

        public async Task Stop(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).StopAsync();

        public async Task Pause(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).PauseAsync();

        public async Task Next(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).NextAsync();

        public async Task Previous(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).PreviousAsync();

        public async Task Seek(string receiverName, double timeToSeek) =>
            await (await _castService.GetReceiverService(receiverName)).SeekAsync(timeToSeek);

        public async Task ChangeCurrentMedia(string receiverName, int mediaId) =>
            await (await _castService.GetReceiverService(receiverName)).ChangeCurrentMediaAsync(mediaId);

        public async Task ChangeRepeatMode(string receiverName, RepeatMode repeatMode) =>
            await (await _castService.GetReceiverService(receiverName)).ChangeRepeatModeAsync(repeatMode);

        public async Task SetPlaybackRate(string receiverName, double playbackRate) =>
            await (await _castService.GetReceiverService(receiverName)).SetPlaybackRateAsync(playbackRate);

        public async Task InsertQueue(string receiverName, IEnumerable<QueueItem> queueItems) =>
            await (await _castService.GetReceiverService(receiverName)).InsertQueueAsync(queueItems);

        public async Task RemoveQueue(string receiverName, IEnumerable<QueueItem> queueItems) =>
            await (await _castService.GetReceiverService(receiverName)).RemoveQueueAsync(queueItems);

        public async Task UpQueue(string receiverName, IEnumerable<QueueItem> queueItems) =>
            await (await _castService.GetReceiverService(receiverName)).UpQueueAsync(queueItems);

        public async Task DownQueue(string receiverName, IEnumerable<QueueItem> queueItems) =>
            await (await _castService.GetReceiverService(receiverName)).DownQueueAsync(queueItems);

        public async Task ShuffleQueue(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).ShuffleQueueAsync();

        public async Task SetVolume(string receiverName, float volume) =>
            await (await _castService.GetReceiverService(receiverName)).SetVolumeAsync(volume);

        public async Task ToggleMute(string receiverName) =>
            await (await _castService.GetReceiverService(receiverName)).ToggleMutedAsync();
    }
}