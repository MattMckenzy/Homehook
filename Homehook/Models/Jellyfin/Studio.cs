using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin
{
    public class Studio
    {
        [JsonProperty("id")]
        public required string Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
    }
}