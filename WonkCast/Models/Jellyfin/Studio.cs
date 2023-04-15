using Newtonsoft.Json;

namespace WonkCast.Models.Jellyfin
{
    public class Studio
    {
        [JsonProperty("id")]
        public required string Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
    }
}