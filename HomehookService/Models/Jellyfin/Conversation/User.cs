using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Homehook.Models.Jellyfin.Converation
{
    public class User
    {
        [JsonProperty("locale")]
        public string Locale { get; set; }

        [JsonProperty("params")]
        public Params Params { get; set; }

        [JsonProperty("accountLinkingStatus")]
        public string AccountLinkingStatus { get; set; }

        [JsonProperty("verificationStatus")]
        public string VerificationStatus { get; set; }

        [JsonProperty("packageEntitlements")]
        public List<object> PackageEntitlements { get; set; }

        [JsonProperty("gaiamint")]
        public string Gaiamint { get; set; }

        [JsonProperty("permissions")]
        public List<object> Permissions { get; set; }

        [JsonProperty("lastSeenTime")]
        public DateTime LastSeenTime { get; set; }
    }
}