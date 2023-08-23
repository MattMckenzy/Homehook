using System.Text.Json.Serialization;

namespace HomeHook.Common.Models
{
    public class Device
    {
        public required string Name { get; set; }
        public required string Address { get; set; }
        public required string Version { get; set; }

        public DeviceStatus DeviceStatus { get; set; } = DeviceStatus.Stopped;
        public string? CurrentMediaItemId { get; set; } = null;
        public List<MediaItem> MediaQueue { get; set; } = new List<MediaItem>();

        public double CurrentTime { get; set; }
        public float Volume { get; set; } = 0.5f;
        public bool IsMuted { get; set; } = false;
        public float PlaybackRate { get; set; } = 1;
        public string? StatusMessage { get; set; }
        public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

        [JsonIgnore]
        public MediaItem? CurrentMedia { get { return CurrentMediaItemId == null ? null : MediaQueue.FirstOrDefault(mediaItem => CurrentMediaItemId == mediaItem.Id); } }

        [JsonIgnore]
        public bool IsMediaLoaded 
        { 
            get 
            {
                return DeviceStatus != DeviceStatus.Finished &&
                    DeviceStatus != DeviceStatus.Ended &&
                    DeviceStatus != DeviceStatus.Stopping &&
                    DeviceStatus != DeviceStatus.Stopped &&
                    CurrentMedia != null;
            } 
        }

        public bool IsCommandAvailable(PlayerCommand deviceCommand) 
        {
            return deviceCommand switch
            {
                PlayerCommand.PlayMediaItem or 
                PlayerCommand.RemoveMediaItems or 
                PlayerCommand.MoveMediaItemsUp or 
                PlayerCommand.MoveMediaItemsDown or 
                PlayerCommand.ChangeRepeatMode => 
                    MediaQueue.Any(),
                
                PlayerCommand.AddMediaItems or 
                PlayerCommand.Stop or 
                PlayerCommand.SetPlaybackRate or 
                PlayerCommand.SetVolume or 
                PlayerCommand.ToggleMute => 
                    true,
                
                PlayerCommand.Play => 
                    DeviceStatus == DeviceStatus.Paused || (DeviceStatus == DeviceStatus.Ended && CurrentMedia != null),
                
                PlayerCommand.Pause => 
                    DeviceStatus == DeviceStatus.Playing,
                
                PlayerCommand.Next or 
                PlayerCommand.Previous => 
                    CurrentMedia != null,
                
                PlayerCommand.Seek or 
                PlayerCommand.SeekRelative => 
                    IsMediaLoaded,

                _ => 
                    false,
            };
        }

        public void AddMediaItems(List<MediaItem> mediaItems, bool launch, string? insertBeforeMediaItemId)
        {
            if (launch)
            {
                CurrentMediaItemId = null;
                MediaQueue.Clear();
            }

            int insertBeforeIndex = insertBeforeMediaItemId != null && MediaQueue.Any(mediaItem => mediaItem.Id == insertBeforeMediaItemId) ?
                MediaQueue.IndexOf(MediaQueue.First(mediaItem => mediaItem.Id == insertBeforeMediaItemId)) :
                MediaQueue.Count;

            MediaQueue.InsertRange(insertBeforeIndex, mediaItems);


            if (launch)
            {
                CurrentMediaItemId = MediaQueue.FirstOrDefault()?.Id;
            }
        }

        public void RemoveMediaItems(IEnumerable<string> mediaItemIds)
        {
            foreach (MediaItem mediaItem in MediaQueue.ToArray().Where(mediaItem => mediaItemIds.Contains(mediaItem.Id)).Reverse())
            {
                if (mediaItem == CurrentMedia)
                {
                    int currentIndex = MediaQueue.IndexOf(CurrentMedia);

                    MediaQueue.Remove(mediaItem);
                    CurrentMediaItemId = MediaQueue.ElementAt(Math.Min(currentIndex, MediaQueue.Count - 1)).Id;
                }
                else
                    MediaQueue.Remove(mediaItem);
            }
        }

        public void MoveUpMediaItems(IEnumerable<string> mediaItemIds)
        {
            foreach (MediaItem mediaItem in MediaQueue.ToArray()
                .SkipWhile(mediaItem => mediaItemIds.Contains(mediaItem.Id))
                .Where(mediaItem => mediaItemIds.Contains(mediaItem.Id)))
            {
                int index = MediaQueue.IndexOf(mediaItem);
                if (index > 0 && index < MediaQueue.Count)
                {
                    index--;

                    MediaQueue.Remove(mediaItem);
                    MediaQueue.Insert(index, mediaItem);
                }
            }
        }

        public void MoveDownMediaItems(IEnumerable<string> mediaItemIds)
        {
            foreach (MediaItem mediaItem in MediaQueue.ToArray()
                .Reverse()
                .SkipWhile(mediaItem => mediaItemIds.Contains(mediaItem.Id))
                .Where(mediaItem => mediaItemIds.Contains(mediaItem.Id)))
            {
                int index = MediaQueue.IndexOf(mediaItem);
                if (index >= 0 && index < MediaQueue.Count - 1)
                {
                    index++;

                    MediaQueue.Remove(mediaItem);
                    MediaQueue.Insert(index, mediaItem);
                }
            }
        }
        
        public void OrderMediaItems(IEnumerable<string> mediaItemIds)
        {
            List<string> mediaItemIdList = mediaItemIds.ToList();
            MediaQueue = MediaQueue
                .Where(mediaItem => mediaItemIdList.IndexOf(mediaItem.Id) >= 0)
                .OrderBy(mediaItem => mediaItemIdList.IndexOf(mediaItem.Id))
                .ToList();
        }
    }
}
