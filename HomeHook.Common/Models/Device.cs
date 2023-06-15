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

        public bool IsCommandAvailable(DeviceCommand deviceCommand) 
        {
            return deviceCommand switch
            {
                DeviceCommand.PlayMediaItem or 
                DeviceCommand.RemoveMediaItems or 
                DeviceCommand.MoveMediaItemsUp or 
                DeviceCommand.MoveMediaItemsDown or 
                DeviceCommand.ChangeRepeatMode => 
                    MediaQueue.Any(),
                
                DeviceCommand.AddMediaItems or 
                DeviceCommand.Stop or 
                DeviceCommand.SetPlaybackRate or 
                DeviceCommand.SetVolume or 
                DeviceCommand.ToggleMute => 
                    true,
                
                DeviceCommand.Play => 
                    DeviceStatus == DeviceStatus.Paused || (DeviceStatus == DeviceStatus.Ended && CurrentMedia != null),
                
                DeviceCommand.Pause => 
                    DeviceStatus == DeviceStatus.Playing,
                
                DeviceCommand.Next or 
                DeviceCommand.Previous => 
                    CurrentMedia != null,
                
                DeviceCommand.Seek or 
                DeviceCommand.SeekRelative => 
                    IsMediaLoaded,

                _ => 
                    false,
            };
        }
    }
}
