using Newtonsoft.Json;

namespace Homehook.Models.Jellyfin.Converation
{
    public class RequestJson
    {
        [JsonProperty("handler")]
        public Handler Handler { get; set; }

        [JsonProperty("intent")]
        public Intent Intent { get; set; }

        [JsonProperty("scene")]
        public Scene Scene { get; set; }

        [JsonProperty("session")]
        public Session Session { get; set; }

        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("home")]
        public Home Home { get; set; }

        [JsonProperty("device")]
        public Device Device { get; set; }
    }
}