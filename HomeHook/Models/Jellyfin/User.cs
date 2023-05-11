using Newtonsoft.Json;

namespace HomeHook.Models.Jellyfin
{
    public class User
    {
        [JsonProperty("name")]
        public required string Name { get; set; }

        [JsonProperty("id")]
        public required string Id { get; set; }
    }
}
