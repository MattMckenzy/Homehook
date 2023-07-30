using System.Text.Json.Serialization;

namespace HomeHook.Common.Models
{
    public class Device
    {
        public required string Name { get; set; }
        public required string Address { get; set; }
        public required string Version { get; set; }

        public PlayerStatus DeviceStatus { get; set; } = PlayerStatus.Stopped;
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
                return DeviceStatus != PlayerStatus.Finished &&
                    DeviceStatus != PlayerStatus.Ended &&
                    DeviceStatus != PlayerStatus.Stopping &&
                    DeviceStatus != PlayerStatus.Stopped &&
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
                    DeviceStatus == PlayerStatus.Paused || (DeviceStatus == PlayerStatus.Ended && CurrentMedia != null),
                
                PlayerCommand.Pause => 
                    DeviceStatus == PlayerStatus.Playing,
                
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
    }
}
