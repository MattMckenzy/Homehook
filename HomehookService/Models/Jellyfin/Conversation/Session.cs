using Newtonsoft.Json;
using System.Collections.Generic;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Session
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("params")]
        public Params Params { get; set; }

        [JsonProperty("typeOverrides")]
        public List<object> TypeOverrides { get; set; }

        [JsonProperty("languageCode")]
        public string LanguageCode { get; set; }
    }
}