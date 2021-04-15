using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Params
    {
        [JsonProperty("Order")]
        public Order Order { get; set; }

        [JsonProperty("MediaType")]
        public MediaType MediaType { get; set; }

        [JsonProperty("Content")]
        public Content Content { get; set; }

        [JsonProperty("Device")]
        public Device Device { get; set; }

        [JsonProperty("UserName")]
        public UserName UserName { get; set; }
    }
}