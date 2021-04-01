using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Next
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }
}