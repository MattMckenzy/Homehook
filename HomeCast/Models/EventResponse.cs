using Newtonsoft.Json;

namespace HomeCast.Models
{
    public class EventResponse
    {
        [JsonProperty("event")]
        public required string Event { get; set; }

        [JsonProperty("id")]
        public required int ID { get; set; }
        [JsonProperty("name")]
        public required string Name { get; set; }
        [JsonProperty("data")]
        public required object Data { get; set; }
        [JsonProperty("reason")]
        public required string Reason { get; set; }
        [JsonProperty("playlist_entry_id")]
        public required int PlaylistEntryId { get; set; }
    }
}
