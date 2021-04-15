using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Scene
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slotFillingStatus")]
        public string SlotFillingStatus { get; set; }

        [JsonProperty("slots")]
        public Slots Slots { get; set; }

        [JsonProperty("next")]
        public Next Next { get; set; }
    }
}