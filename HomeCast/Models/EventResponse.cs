using Newtonsoft.Json;

namespace HomeCast.Models
{
    public class EventResponse
    {
        [JsonProperty("event")]
        public required string Event { get; set; }
    }
}
