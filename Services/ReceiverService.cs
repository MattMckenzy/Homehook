using Emby.ApiClient;
using GoogleCast;
using GoogleCast.Channels;
using GoogleCast.Models.Media;
using GoogleCast.Models.Receiver;
using Homehook.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Services
{
    public class ReceiverService : INotifyPropertyChanged
    {

        #region Private and public properties

        private readonly Sender MediaSender = new();

        public IReceiver Receiver { get; set; }

        private bool isInitialized;
        private bool IsInitialized
        {
            get { return isInitialized; }
            set { isInitialized = value; RaisePropertyChanged(nameof(IsInitialized)); }
        }

        public bool IsStopped
        {
            get
            {
                IMediaChannel mediaChannel = MediaSender.GetChannel<IMediaChannel>();
                return mediaChannel.Status == null || !string.IsNullOrEmpty(mediaChannel.Status.FirstOrDefault()?.IdleReason);
            }
        }

        private MediaStatus currentMediaStatus;
        private MediaStatus CurrentMediaStatus
        {
            get { return currentMediaStatus; }
            set { currentMediaStatus = value; RaisePropertyChanged(nameof(CurrentMediaStatus)); }
        }

        private ObservableCollection<QueueItem> queue = new();
        public ObservableCollection<QueueItem> Queue
        {
            get
            {
                return queue;
            }
            set
            {
                queue = value;
                RaisePropertyChanged(nameof(Queue));
            }
        }

        #endregion

        #region Factory Methods

        public ReceiverService(IReceiver receiver)
        { 
            MediaSender.GetChannel<IMediaChannel>().StatusChanged += MediaChannelStatusChanged;
            MediaSender.GetChannel<IMediaChannel>().QueueStatusChanged += QueueStatusChanged;
            MediaSender.GetChannel<IReceiverChannel>().StatusChanged += ReceiverChannelStatusChanged;

            Receiver = receiver;
            Task.Run(async() => 
            {
                await MediaSender.ConnectAsync(Receiver);
                await MediaSender.GetChannel<IMediaChannel>().GetStatusAsync();
                await RefreshQueueAsync();
            }); 

            Queue = new ObservableCollection<QueueItem>();
        }

        #endregion

        #region Commands

        public async Task InitializeItemAsync(MediaInformation mediaInformation)
        {
            await Try(async () =>
            {
                await InvokeAsync<IMediaChannel>(async mediaChannel =>
                {
                    if (mediaInformation != null)
                    {
                        await MediaSender.LaunchAsync(mediaChannel);

                        await mediaChannel.LoadAsync(mediaInformation);
                        await RefreshQueueAsync();

                        IsInitialized = true;
                    }
                });
            });
        }

        public async Task InitializeQueueAsync(IEnumerable<QueueItem> queueItems)
        {
            await Try(async () =>
            {
                await Try(async () =>
                {
                    await InvokeAsync<IMediaChannel>(async mediaChannel =>
                    {
                        if (queueItems.Any())
                        {
                            await MediaSender.LaunchAsync(mediaChannel);

                            Queue<QueueItem> queue = new(queueItems);

                            await mediaChannel.QueueLoadAsync(RepeatMode.RepeatAll, queue.DequeueMany(20).ToArray());
                            await RefreshQueueAsync();

                            while (queue.Count > 0)
                                await mediaChannel.QueueInsertAsync(queue.DequeueMany(20).ToArray());

                            IsInitialized = true;
                        }
                    });
                });
            });
        }

        public async Task PlayAsync() =>
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(!IsInitialized || IsStopped, null, async mediaChannel => { await mediaChannel.PlayAsync(); }); });
        
        public async Task PauseAsync() =>        
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.PauseAsync()); });
        
        public async Task SetPlaybackRateAsync(double playbackRate) =>        
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.SetPlaybackRateMessage(playbackRate)); });
        
        public async Task StopAsync() =>        
            await Try(async () =>
            {
                if (IsStopped && IsInitialized)
                    await InvokeAsync<IReceiverChannel>(receiverChannel => receiverChannel.StopAsync());    
                else
                    await InvokeAsync<IMediaChannel>(mediaChannel => mediaChannel.StopAsync());                
            });        

        public async Task SetVolumeAsync(float volume) =>
            await Try(async () => { await SendChannelCommandAsync<IReceiverChannel>(IsStopped, null, async receiverChannel => { await receiverChannel.SetVolumeAsync(volume); }); });        

        public async Task ToggleMutedAsync() =>
            await Try(async () => { await SendChannelCommandAsync<IReceiverChannel>(IsStopped, null, async receiverChannel => { await receiverChannel.SetIsMutedAsync(CurrentMediaStatus.Volume.IsMuted ?? true); }); });
        
        public async Task SeekAsync(int timeToSeek) =>
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => { await mediaChannel.SeekAsync(timeToSeek); }); });        

        public async Task NextAsync() =>        
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.NextAsync()); });
        
        public async Task PreviousAsync() =>
            await Try(async () => { await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.PreviousAsync()); });
        
        public async Task UpQueueAsync(IEnumerable<QueueItem> selectedItems) =>
            await Try(async () =>
            {
                if (selectedItems.Any())
                {
                    ObservableCollection<int> ids = new(Queue.Select(i => (int)i.ItemId));
                    foreach (QueueItem selectedItem in (selectedItems).OrderBy(i => i.OrderId))
                    {
                        int currentIndex = ids.IndexOf((int)selectedItem.ItemId);
                        if (currentIndex > 0)
                            ids.Move(currentIndex, currentIndex - 1);
                    }

                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueReorderAsync(ids.ToArray()));
                }
            });

        public async Task DownQueueAsync(IEnumerable<QueueItem> selectedItems) =>        
            await Try(async () =>
            {
                if (selectedItems.Any())
                {
                    ObservableCollection<int> ids = new(Queue.Select(i => (int)i.ItemId));
                    foreach (QueueItem selectedItem in selectedItems.OrderByDescending(i => i.OrderId))
                    {
                        int currentIndex = ids.IndexOf((int)selectedItem.ItemId);
                        if (currentIndex > 0)
                            ids.Move(currentIndex, currentIndex + 1);
                    }

                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueReorderAsync(ids.ToArray()));
                }
            });        

        public async Task InsertQueueAsync(IEnumerable<QueueItem> queueItems) =>
            await Try(async () =>
            {
                if (queueItems.Any())
                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueInsertAsync(queueItems.ToArray()));
            });
        

        public async Task RemoveQueueAsync(IEnumerable<QueueItem> selectedItems) =>
            await Try(async () =>
            {
                if (selectedItems.Any())
                    await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel =>  await mediaChannel.QueueRemoveAsync(selectedItems.Select(queueItem => (int)queueItem.ItemId).ToArray()));
            });
        
        public async Task ShuffleQueueAsync() =>
            await Try(async () => await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueUpdateAsync(null, true)));
        

        public async Task ChangeCurrentMediaAsync(int itemId) =>
            await Try(async () => await SendChannelCommandAsync<IMediaChannel>(IsStopped, null, async mediaChannel => await mediaChannel.QueueUpdateAsync(itemId)));        

        #endregion

        #region Helper Methods

        private async Task Try(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                CurrentMediaStatus.PlayerState = ex.GetBaseException().Message;
                IsInitialized = false;
            }
        }

        private async Task InvokeAsync<TChannel>(Func<TChannel, Task> action) where TChannel : IChannel
        {
            if (action != null)
            {
                await action.Invoke(MediaSender.GetChannel<TChannel>());
            }
        }

        private async Task SendChannelCommandAsync<TChannel>(bool condition, Func<TChannel, Task> action, Func<TChannel, Task> otherwise) where TChannel : IChannel
        {
            await InvokeAsync(condition ? action : otherwise);
        }

        #endregion

        #region Event Handlers

        private void MediaChannelStatusChanged(object sender, EventArgs e)
        {
            CurrentMediaStatus = ((IMediaChannel)sender).Status?.FirstOrDefault();
            string playerState = CurrentMediaStatus?.PlayerState;

            if (new string[] { "PLAYING", "BUFFERING", "PAUSED" }.Contains(playerState))
                IsInitialized = true;
            else if (new string[] { "CANCELLED", "FINISHED", "ERROR" }.Contains(CurrentMediaStatus?.IdleReason))
            {
                IsInitialized = false;
                Queue = new ObservableCollection<QueueItem>();
                CurrentMediaStatus = null;
            }

            QueueItem currentItem = Queue.FirstOrDefault(i => i.ItemId == CurrentMediaStatus?.CurrentItemId);

            if (currentItem != null && CurrentMediaStatus?.Media?.Duration != null)
            {
                IList<QueueItem> currentQueue = Queue.ToList();
                currentQueue[currentQueue.IndexOf(currentItem)].Media.Duration = CurrentMediaStatus?.Media?.Duration;
                Queue = new ObservableCollection<QueueItem>(currentQueue);
            }
        }

        private async void QueueStatusChanged(object sender, EventArgs e)
        {
            QueueStatus status = ((IMediaChannel)sender).QueueStatus;

            switch (status.ChangeType)
            {
                case QueueChangeType.Insert:
                    await RefreshQueueAsync(status.ItemIds);
                    break;
                case QueueChangeType.Update:
                    Queue = new ObservableCollection<QueueItem>(Queue.OrderBy(i => Array.IndexOf(status.ItemIds, i.ItemId)));
                    break;
                case QueueChangeType.Remove:
                    IList<QueueItem> currentQueue = Queue.ToList();
                    foreach (int itemId in status.ItemIds)
                        currentQueue.Remove(currentQueue.FirstOrDefault(i => i.ItemId == itemId));
                    Queue = new ObservableCollection<QueueItem>(currentQueue);
                    break;
            }
        }

        private void ReceiverChannelStatusChanged(object sender, EventArgs e)
        {
            if (!IsInitialized)
            {
                ReceiverStatus status = ((IReceiverChannel)sender).Status;
                if (status != null)
                {
                    if (status.Volume.Level != null)
                    {
                        CurrentMediaStatus.Volume.Level = (float)status.Volume.Level;
                    }
                    if (status.Volume.IsMuted != null)
                    {
                        CurrentMediaStatus.Volume.IsMuted = (bool)status.Volume.IsMuted;
                    }
                }
            }
        }

        #endregion

        #region Helper Methods

        private async Task RefreshQueueAsync(int[] itemIdsToFetch = null)
        {
            IMediaChannel mediaChannel = MediaSender.GetChannel<IMediaChannel>();

            int[] itemIds = itemIdsToFetch ?? await mediaChannel?.QueueGetItemIdsMessage();
            if (itemIds != null && itemIds.Length > 0)
            {
                Queue<int> itemIdsQueue = new(itemIds);
                IList<QueueItem> currentQueue = Queue.ToList();

                while (itemIdsQueue.Count > 0)
                {
                    foreach (QueueItem item in await mediaChannel.QueueGetItemsMessage(itemIdsQueue.DequeueMany(20).ToArray()))
                    {
                        if (currentQueue.FirstOrDefault(i => i.ItemId == item.ItemId) != null)
                            currentQueue[currentQueue.IndexOf(Queue.FirstOrDefault(i => i.ItemId == item.ItemId))] = item;
                        else
                        {
                            if (item.OrderId < currentQueue.Count) 
                                currentQueue.Insert((int)item.OrderId, item);
                            else 
                                currentQueue.Add(item);
                        }
                    }
                }

                Queue = new ObservableCollection<QueueItem>(currentQueue);
            }
        }           

        #endregion

        #region INotifyPropertyChanged Interface Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        #endregion

    }
}