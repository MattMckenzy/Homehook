using System.Text.Json.Serialization;

namespace WonkCast.Common.Models
{
    public class Device
    {
        public required string Name { get; set; }
        public required string Address { get; set; }
        public double Volume { get; set; } = 0.5;
        public bool IsMuted { get; set; } = false;
        public double PlaybackRate { get; set; } = 1;
        public double CurrentTime { get; set; } = 0;
        public string User { get; set; } = string.Empty;
        public string? StatusMessage { get; set; }
        public DeviceStatus DeviceStatus { get; set; } = DeviceStatus.Stopped;
        public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
        public int? CurrentMediaIndex { get; set; } = null;
        public List<Media> MediaQueue { get; set; } = new List<Media>();

        [JsonIgnore]
        public Media? CurrentMedia { get { return MediaQueue.ElementAtOrDefault(CurrentMediaIndex ?? 0) ?? null; } }
    }
}
