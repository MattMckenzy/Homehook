using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Conversation
    {
        [JsonProperty("requestJson")]
        public RequestJson RequestJson { get; set; }
    }
}