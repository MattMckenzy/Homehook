using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Home
    {
        [JsonProperty("params")]
        public Params Params { get; set; }
    }
}