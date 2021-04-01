using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin
{
    public class User
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
