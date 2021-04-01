using Newtonsoft.Json;
using System;

namespace Homehook.Models.Jellyfin
{
    public class UserData
    {
        [JsonProperty("lastPlayedDate")]
        public DateTime? LastPlayedDate { get; set; }

        [JsonProperty("playbackPositionTicks")]
        public long? PlaybackPositionTicks { get; set; }
    }
}