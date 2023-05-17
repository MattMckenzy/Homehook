using Newtonsoft.Json;

namespace HomeCast.Models
{
    public class CommandResponse
    {
        [JsonProperty("data")]
        public object? Data { get; set; }

        [JsonProperty("request_id")]
        public int RequestId { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = string.Empty;
    }
}
