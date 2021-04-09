using GoogleCast;
using GoogleCast.Channels;
using GoogleCast.Models.Media;
using GoogleCast.Models.Receiver;
using Homehook.Extensions;
using Homehook.Hubs;
using Homehook.Models.Jellyfin;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace Homehook.Services
{
    public class ReceiverService : IDisposable
    {
        #region Private and public properties

        private readonly JellyfinService _jellyfinService;
        private readonly IHubContext<ReceiverHub> _receiverHub;

        private readonly LoggingService<CastService> _loggingService;
        private readonly ISender _sender = new Sender();
        private readonly string _applicationId;

        private System.Timers.Timer _timer;
        private int _refreshClock = 0;
        private bool _isSessionInitialized = false;

        private bool _disposedValue;

        public IReceiver Receiver { get; set; }

        public bool IsMediaInitialized { get; set; }

        public bool IsStopped
        {
            get
            {
                IMediaChannel mediaChannel = _sender.GetChannel<IMediaChannel>();
                return mediaChannel.Status == null || !string.IsNullOrEmpty(mediaChannel.Status.FirstOrDefault()?.IdleReason);
            }
        }

        public float Volume { get; set; }

        public bool IsMuted { get; set; }

        public MediaStatus CurrentMediaStatus { get; set; }

        public MediaInformation CurrentMediaInformation { get; set; }

        public int? CurrentRunTime { get; set; }

        public List<QueueItem> Queue { get; set; } = new();
        
        #endregion

        #region Factory Methods

        public ReceiverService(IReceiver receiver, string applicationId, JellyfinService jellyfinService, IHubContext<ReceiverHub> receiverHub, LoggingService<CastService> loggingService)
        {
            _applicationId = applicationId;
            _jellyfinService = jellyfinService;
            _receiverHub = receiverHub;
            _loggingService = loggingService;
            Receiver = receiver;

            Initialize();
        }

        private async void Initialize()
        {
            await _sender.ConnectAsync(Receiver);

            _sender.Disconnected += SenderDisconnected;
            _sender.GetChannel<IMediaChannel>().StatusChanged += MediaChannelStatusChanged;
            _sender.GetChannel<IMediaChannel>().QueueStatusChanged += QueueStatusChanged;
            _sender.GetChannel<IReceiverChannel>().StatusChanged += ReceiverChannelStatusChanged;

            _timer = new()
            {
                Interval = 1000,
                AutoReset = true,
                Enabled = true
            };
            _timer.Elapsed += TimerElapsed;

            await RefreshStatus(true);
        }

        #endregion

        #region Commands

        public Task<HomehookCommon.Models.ReceiverStatus> GetReceiverStatus()
        {
            return Task.FromResult(new HomehookCommon.Models.ReceiverStatus()
            {
                Name = Receiver.FriendlyName,
                Id = Receiver.Id,
                IPAddress = Receiver.IPEndPoint.ToString(),
                IsMediaInitialized = IsMediaInitialized,
                IsStopped = IsStopped,
                Volume = Volume,
                IsMuted = IsMuted,
                CurrentMediaStatus = CurrentMediaStatus,
                CurrentMediaInformation = CurrentMediaInformation,
                CurrentRunTime = CurrentRunTime,
                Queue = Queue
            });
        }

        public async Task InitializeItemAsync(MediaInformation mediaInformation)
        {
            await Try(async () =>
            {
                await InvokeAsync<IMediaChannel>(async mediaChannel =>
                {
                    if (mediaInformation != null)
                    {
                        string applicationId = string.IsNullOrWhiteSpace(_applicationId) ? mediaChannel.DefaultApplicationId : _applicationId;
                        string currentApplicationId = _sender.GetChannel<IReceiverChannel>()?.Status?.Applications?.FirstOrDefault()?.AppId;
                        if (currentApplicationId == null || !applicationId.Equals(currentApplicationId, StringComparison.InvariantCultureIgnoreCase))                        
                            await _sender.GetChannel<IReceiverChannel>().LaunchAsync(applicationId);

                        Queue = new();

                        await mediaChannel.LoadAsync(mediaInformation);
                        await RefreshQueueAsync();

                        IsMediaInitialized = true;
                    }
                });
            });
        }

        public async Task InitializeQueueAsync(IEnumerable<QueueItem> queueItems)
        {
            await Try(async () =>
            {
                await InvokeAsync<IMediaChannel>(async mediaChannel =>
                {
                    if (queueItems.Any())
                    {
                        string applicationId = string.IsNullOrWhiteSpace(_applicationId) ? mediaChannel.DefaultApplicationId : _applicationId;
                        string currentApplicationId = _sender.GetChannel<IReceiverChannel>()?.Status?.Applications?.FirstOrDefault()?.AppId;
                        if (currentApplicationId == null || !applicationId.Equals(currentApplicationId, StringComparison.InvariantCultureIgnoreCase))
                            await _sender.GetChannel<IReceiverChannel>().LaunchAsync(applicationId);
                        
                        Queue = new(queueItems);
                        Queue<QueueItem> queue = new(Queue);

                        await mediaChannel.QueueLoadAsync(RepeatMode.RepeatAll, queue.DequeueMany(20).ToArray());

                        while (queue.Count > 0)
                            await mediaChannel.QueueInsertAsync(queue.DequeueMany(20).ToArray());

                        await RefreshQueueAsync();

                        IsMediaInitialized = true;
                    }
                });
            });
        }

        public async Task PlayAsync() =>
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(!IsMediaInitialized || IsStopped, null, async mediaChannel => { await mediaChannel.PlayAsync(); }); });
        
        public async Task PauseAsync() =>        
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.PauseAsync()); });
        
        public async Task SetPlaybackRateAsync(double playbackRate) =>        
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.SetPlaybackRateMessage(playbackRate)); });
        
        public async Task StopAsync() =>        
            await Try(async () =>
            {
                if (IsStopped)
                    await InvokeAsync<IReceiverChannel>(receiverChannel => receiverChannel.StopAsync());    
                else
                    await InvokeAsync<IMediaChannel>(mediaChannel => mediaChannel.StopAsync());                
            });        

        public async Task SetVolumeAsync(float volume) =>
            await Try(async () => { await SendChannelCommandAsync<IReceiverChannel>(IsStopped, null, async receiverChannel => { await receiverChannel.SetVolumeAsync(volume); }); });        

        public async Task ToggleMutedAsync() =>
            await Try(async () => { await SendChannelCommandAsync<IReceiverChannel>(IsStopped, null, async receiverChannel => { await receiverChannel.SetIsMutedAsync(!IsMuted); }); });
        
        public async Task SeekAsync(double timeToSeek) =>
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => { await mediaChannel.SeekAsync(timeToSeek); }); });        

        public async Task NextAsync() =>        
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.NextAsync()); });
        
        public async Task PreviousAsync() =>
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.PreviousAsync()); });
        
        public async Task UpQueueAsync(IEnumerable<int> movingItemIds) =>
            await Try(async () =>
            {
                if (movingItemIds.Any() && Queue != null && Queue.Any())
                {
                    (int orderId, int itemId)[] items = Queue.Select(item => ((int)item.OrderId, (int)item.ItemId)).ToArray();
                    foreach ((int orderId, int itemId) in items.ToArray())
                    {
                        if (movingItemIds.Contains(itemId) && orderId > 0)
                            items.MoveUp(orderId);
                    }

                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueReorderAsync(items.Select(item => item.itemId).ToArray()));
                }
            });

        public async Task DownQueueAsync(IEnumerable<int> movingItemIds) =>        
            await Try(async () =>
            {
                if (movingItemIds.Any() && Queue != null && Queue.Any())
                {
                    (int orderId, int itemId)[] items = Queue.Select(item => ((int)item.OrderId, (int)item.ItemId)).ToArray();
                    foreach ((int orderId, int itemId) in items.Reverse().ToArray())
                    {
                        if (movingItemIds.Contains(itemId) && orderId > 0)
                            items.MoveDown(orderId);
                    }

                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueReorderAsync(items.Select(item => item.itemId).ToArray()));
                }
            });

        public async Task InsertQueueAsync(IEnumerable<QueueItem> queueItems, int? insertBefore = null) =>
            await Try(async () =>
            {
                if (queueItems.Any())
                {
                    Queue<QueueItem> queue = new(new ObservableCollection<QueueItem>(queueItems));
                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueInsertAsync(queue.DequeueMany(20).ToArray(), insertBefore));
                    while (queue.Count > 0)
                        await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueInsertAsync(queue.DequeueMany(20).ToArray(), insertBefore));
                }
            });
        

        public async Task RemoveQueueAsync(IEnumerable<int> itemIds) =>
            await Try(async () =>
            {
                if (itemIds.Any())
                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel =>  await mediaChannel.QueueRemoveAsync(itemIds.ToArray()));
            });
        
        public async Task ShuffleQueueAsync() =>
            await Try(async () => await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueUpdateAsync(shuffle: true)));
        

        public async Task ChangeCurrentMediaAsync(int itemId) =>
            await Try(async () => await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueUpdateAsync(currentItemId: itemId)));
        
        public async Task ChangeRepeatModeAsync(RepeatMode repeatMode) =>
            await Try(async () => await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueUpdateAsync(repeatMode: repeatMode, shuffle: repeatMode == RepeatMode.RepeatAllAndShuffle ? true : null )));

        #endregion

        #region Event Handlers

        private async void MediaChannelStatusChanged(object sender, EventArgs e)
        {
            MediaStatus newMediaStatus = ((IMediaChannel)sender).Status?.FirstOrDefault();

            if (newMediaStatus == null || (
                newMediaStatus.Media?.CustomData != null &&
                newMediaStatus.Media.CustomData.TryGetValue("Id", out string newMediaId) &&
                CurrentMediaInformation?.CustomData != null &&
                CurrentMediaInformation.CustomData.TryGetValue("Id", out string mediaId) &&
                newMediaId != mediaId))
            {
                await JellySessionUpdate(true);
                CurrentMediaInformation = null;
            }

            if (newMediaStatus == null)
            {
                CurrentMediaStatus = null;
                CurrentRunTime = null;
                Queue = new();

                IsMediaInitialized = false;
                _timer.Stop();
            }
            else
            {
                CurrentMediaStatus = newMediaStatus;
                CurrentMediaInformation = CurrentMediaStatus.Media ?? CurrentMediaInformation;

                QueueItem currentItem = Queue.FirstOrDefault(i => i.ItemId == CurrentMediaStatus.CurrentItemId);

                if (currentItem != null && CurrentMediaStatus.Media?.Duration != null)
                {
                    IList<QueueItem> currentQueue = Queue.ToList();
                    currentQueue[currentQueue.IndexOf(currentItem)].Media.Duration = CurrentMediaStatus.Media?.Duration;
                }

                if (new string[] { "PLAYING", "PAUSED" }.Contains(newMediaStatus.PlayerState))
                {
                    IsMediaInitialized = true;
                    CurrentRunTime = Convert.ToInt32(CurrentMediaStatus.CurrentTime);
                    _timer.Start();
                }
                else if (new string[] { "FINISHED" }.Contains(newMediaStatus.PlayerState))
                {
                    IsMediaInitialized = true;
                    _timer.Stop();
                }
                else
                {
                    IsMediaInitialized = false;
                    _timer.Stop();
                }

                await JellySessionUpdate();
            }

            await _receiverHub.Clients.All.SendAsync("ReceiveStatus", Receiver.FriendlyName, await GetReceiverStatus());
        }

        private async void QueueStatusChanged(object sender, EventArgs e)
        {
            IMediaChannel mediaChannel = ((IMediaChannel)sender);
            QueueStatus status = mediaChannel.QueueStatus;

            switch (status.ChangeType)
            {
                case QueueChangeType.Insert:
                    await RefreshQueueAsync();
                    break;
                case QueueChangeType.Update:
                    Queue = new(Queue.OrderBy(item => Array.IndexOf(status.ItemIds, item.ItemId)).Select((QueueItem item, int index) => { item.OrderId = index; return item; }));
                    break;
                case QueueChangeType.Remove:
                    IList<QueueItem> currentQueue = Queue.ToList();
                    foreach (int itemId in status.ItemIds)
                        currentQueue.Remove(currentQueue.FirstOrDefault(item => item.ItemId == itemId));
                    Queue = new(currentQueue.Select((QueueItem item, int index) => { item.OrderId = index; return item; }));
                    break;
            }

            await _receiverHub.Clients.All.SendAsync("ReceiveStatus", Receiver.FriendlyName, await GetReceiverStatus());
        }

        private async void ReceiverChannelStatusChanged(object sender, EventArgs e)
        {
            ReceiverStatus status = ((IReceiverChannel)sender).Status;
            if (status != null)
            {
                if (status.Volume.Level != null)
                {
                    Volume = (float)status.Volume.Level;
                }
                if (status.Volume.IsMuted != null)
                {
                    IsMuted = (bool)status.Volume.IsMuted;
                }
            }

            await _receiverHub.Clients.All.SendAsync("ReceiveStatus", Receiver.FriendlyName, await GetReceiverStatus());
        }

        private async void SenderDisconnected(object sender, EventArgs eventArgs)
        {                        
            CurrentMediaStatus = null;
            CurrentMediaInformation = null;
            CurrentRunTime = null;
            Queue = new();

            IsMediaInitialized = false;
            _timer.Stop();

            await _receiverHub.Clients.All.SendAsync("ReceiveStatus", Receiver.FriendlyName, await GetReceiverStatus());

            Dispose();
        }

        #endregion

        #region Helper Methods

        private async Task Try(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                await _loggingService.LogError("Cast error.", $"Got the following message while interacting with the cast API: {exception.GetBaseException().Message}", exception.StackTrace);
                await _receiverHub.Clients.All.SendAsync("ReceiveMessage", Receiver.FriendlyName, exception.Message); 
                SenderDisconnected(exception.Message, null);
            }
        }

        private async Task InvokeAsync<TChannel>(Func<TChannel, Task> action) where TChannel : IChannel
        {
            if (action != null)
            {
                await action.Invoke(_sender.GetChannel<TChannel>());
            }
        }

        private async Task SendChannelCommandAsync<TChannel>(bool condition, Func<TChannel, Task> action, Func<TChannel, Task> otherwise) where TChannel : IChannel
        {
            await InvokeAsync(condition ? action : otherwise);
        }

        private async void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            CurrentRunTime += 1;
            if (_refreshClock++ == 10)
            {
                await RefreshStatus();
                _refreshClock = 0;
            }
        }

        private async Task RefreshStatus(bool refreshQueue = false)
        {
            CurrentMediaStatus = await _sender.GetChannel<IMediaChannel>().GetStatusAsync();
            if (CurrentMediaStatus != null && refreshQueue)
                await RefreshQueueAsync();
        }

        private async Task RefreshQueueAsync()
        {
            try
            {
                IMediaChannel mediaChannel = _sender.GetChannel<IMediaChannel>();

                int[] itemIds = await mediaChannel?.QueueGetItemIdsMessage();
                if (itemIds != null && itemIds.Length > 0)
                {
                    Queue<int> itemIdsQueue = new(itemIds);
                    IList<QueueItem> currentQueue = Queue.ToList();

                    List<QueueItem> queueItems = new();
                    while (itemIdsQueue.Count > 0)
                    {
                        queueItems.AddRange(await mediaChannel.QueueGetItemsMessage(itemIdsQueue.DequeueMany(20).ToArray()));

                        if (queueItems == null)
                        {
                            Queue = new();
                            return;
                        }
                    }

                    Queue = new(queueItems.OrderBy(item => item.OrderId));
                }
            }
            catch (InvalidOperationException) { }
        }

        private async Task JellySessionUpdate(bool isStopped = false)
        {
            MediaStatus mediaStatus = CurrentMediaStatus;
            MediaInformation mediaInformation = CurrentMediaInformation;
            int? runTime = CurrentRunTime;

            if (mediaStatus != null &&
                mediaInformation != null &&
                mediaInformation.CustomData != null &&
                mediaInformation.CustomData.TryGetValue("Id", out string mediaId) &&
                mediaInformation.CustomData.TryGetValue("Username", out string sessionUser))
            {
                string playerState = isStopped ? "STOPPED" : mediaStatus.PlayerState == "IDLE" ? mediaStatus.IdleReason : mediaStatus.PlayerState;
                _loggingService.LogInformation("Cast Update.", $"", new { runTime, playerState, mediaId }).GetAwaiter().GetResult();

                switch (playerState)
                {
                    case "PLAYING":
                        await _jellyfinService.UpdateProgress(GetProgress(mediaStatus, runTime, mediaId, false, false, _isSessionInitialized ? ProgressEvents.TimeUpdate : null), sessionUser, Receiver.FriendlyName, Receiver.Id);
                        _isSessionInitialized = true;
                        break;
                    case "PAUSED":
                        await _jellyfinService.UpdateProgress(GetProgress(mediaStatus, runTime, mediaId, true, false, ProgressEvents.Pause), sessionUser, Receiver.FriendlyName, Receiver.Id);
                        _isSessionInitialized = true;
                        break;
                    case "FINISHED":
                        await _jellyfinService.UpdateProgress(GetProgress(mediaStatus, runTime, mediaId, true, true), sessionUser, Receiver.FriendlyName, Receiver.Id, true);
                        _isSessionInitialized = false;
                        break;
                    case "STOPPED":
                        await _jellyfinService.UpdateProgress(GetProgress(mediaStatus, runTime, mediaId, false, false), sessionUser, Receiver.FriendlyName, Receiver.Id, true);
                        _isSessionInitialized = false;
                        break;
                    default:
                        break;
                }
            }
        }

        private Progress GetProgress(MediaStatus mediaStatus, double? runTime, string mediaId, bool isPaused, bool isFinished, ProgressEvents? progressEvent = null)
        {
            return new Progress
            {
                EventName = progressEvent,
                ItemId = mediaId,
                MediaSourceId = mediaId,
                PositionTicks = runTime != null ? Convert.ToInt64(runTime * 10000000) : null,
                VolumeLevel = !isFinished ? Convert.ToInt32(Volume * 100) : null,
                IsMuted = !isFinished ? IsMuted : null,
                IsPaused = !isFinished ? isPaused : null,
                PlaybackRate = !isFinished ? mediaStatus.PlaybackRate : null,
                PlayMethod = !isFinished ? PlayMethod.DirectPlay : null
            };
        }

        #endregion

        #region IDisposed Interface Implementation

        public event EventHandler Disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _sender.Disconnected -= SenderDisconnected;
                    _sender.GetChannel<IMediaChannel>().StatusChanged -= MediaChannelStatusChanged;
                    _sender.GetChannel<IMediaChannel>().QueueStatusChanged -= QueueStatusChanged;
                    _sender.GetChannel<IReceiverChannel>().StatusChanged -= ReceiverChannelStatusChanged;
                    _timer.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~ReceiverService()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            Disposed.Invoke(this, null);
            GC.SuppressFinalize(this);
        }

        #endregion

    }
}