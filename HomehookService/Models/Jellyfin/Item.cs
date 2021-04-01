using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Models.Jellyfin
{
    public class Item
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("overview")]
        public string Overview { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("mediaType")]
        public string MediaType { get; set; }

        [JsonProperty("userData")]
        public UserData UserData { get; set; }

        [JsonProperty("indexNumber")]
        public int? IndexNumber { get; set; }

        [JsonProperty("parentIndexNumber")]
        public int? ParentIndexNumber { get; set; }

        [JsonProperty("runTimeTicks")]
        public long? RunTimeTicks { get; set; }

        [JsonProperty("seriesName")]
        public string SeriesName { get; set; }

        [JsonProperty("album")]
        public string Album { get; set; }

        [JsonProperty("artists")]
        public string[] Artists { get; set; }

        [JsonProperty("albumArtist")]
        public string AlbumArtist { get; set; }

        [JsonProperty("dateCreated")]
        public DateTime? DateCreated { get; set; }

        [JsonProperty("premiereDate")]
        public DateTime? PremiereDate { get; set; }

        [JsonProperty("productionYear")]
        public int? ProductionYear { get; set; }
    }
}
