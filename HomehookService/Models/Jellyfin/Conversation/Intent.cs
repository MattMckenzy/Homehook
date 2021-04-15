using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Intent
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("params")]
        public Params Params { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }
    }
}