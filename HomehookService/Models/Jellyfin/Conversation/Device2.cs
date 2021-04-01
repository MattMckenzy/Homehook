using Newtonsoft.Json;
using System.Collections.Generic;

namespace Homehook.Models.Jellyfin.Converation
{
    public class Device2
    {
        [JsonProperty("capabilities")]
        public List<string> Capabilities { get; set; }
    }
}