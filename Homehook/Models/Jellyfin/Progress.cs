using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Homehook.Models.Jellyfin
{
    public class Progress
    {
        public required string ItemId { get; set; }

        public required string MediaSourceId { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ProgressEvents? EventName { get; set; }

        public long? PositionTicks { get; set; }

        public double? PlaybackRate { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public PlayMethod? PlayMethod { get; set; }

        public bool? IsPaused { get; set; }

        public bool? IsMuted { get; set; }

        public int? VolumeLevel { get; set; }

        public int? PlaylistIndex { get; set; }

        public int? PlaylistLength { get; set; }
    }
}
