using Newtonsoft.Json;
using System;

namespace WonkCast.Models.Jellyfin
{
    public class UserData
    {
        [JsonProperty("lastPlayedDate")]
        public DateTime? LastPlayedDate { get; set; }

        [JsonProperty("playbackPositionTicks")]
        public long? PlaybackPositionTicks { get; set; }

        [JsonProperty("played")]
        public bool? Played { get; set; }
    }
}