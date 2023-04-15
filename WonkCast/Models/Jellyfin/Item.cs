using Newtonsoft.Json;

namespace WonkCast.Models.Jellyfin
{
    public class Item
    {
        [JsonProperty("id")]
        public required string Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("overview")]
        public string? Overview { get; set; }

        [JsonProperty("path")]
        public string? Path { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("mediaType")]
        public string? MediaType { get; set; }

        [JsonProperty("userData")]
        public UserData? UserData { get; set; }

        [JsonProperty("indexNumber")]
        public int? IndexNumber { get; set; }

        [JsonProperty("parentIndexNumber")]
        public int? ParentIndexNumber { get; set; }

        [JsonProperty("runTimeTicks")]
        public long? RunTimeTicks { get; set; }

        [JsonProperty("seriesName")]
        public string? SeriesName { get; set; }

        [JsonProperty("seriesStudio")]
        public string? SeriesStudio { get; set; }

        [JsonProperty("studios")]
        public IEnumerable<Studio>? Studios { get; set; }

        [JsonProperty("album")]
        public string? Album { get; set; }

        [JsonProperty("artists")]
        public string[]? Artists { get; set; }

        [JsonProperty("albumArtist")]
        public string? AlbumArtist { get; set; }

        [JsonProperty("dateCreated")]
        public DateTime? DateCreated { get; set; }

        [JsonProperty("premiereDate")]
        public DateTime? PremiereDate { get; set; }
               
    }
}
