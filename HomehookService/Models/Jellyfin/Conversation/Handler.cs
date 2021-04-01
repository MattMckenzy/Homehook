using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Handler
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }
}