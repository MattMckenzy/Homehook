using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Content
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("resolved")]
        public string Resolved { get; set; }
    }
}