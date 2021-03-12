using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Homehook.Models
{
    public class HomeAssistantMedia
    {
        [JsonProperty("items")]
        public IEnumerable<HomeAssistantMediaItem> Items { get; set; }
    }

    public class HomeAssistantMediaItem
    {
        [JsonProperty("entity_id")]
        public string EntityId { get; set; }

        [JsonProperty("media_content_type")]
        public string MediaContentType { get; set; }

        [JsonProperty("media_content_id")]
        public string MediaContentId { get; set; }

        [JsonProperty("extra")]
        public HomeAssistantExtra Extra { get; set; }
    }

    public class HomeAssistantExtra
    {
        [JsonProperty("enqueue")]
        public bool? Enqueue { get; set; }

        [JsonProperty("metadata")]
        public HomeAssistantMedadata Metadata { get; set; }
    }

    public class HomeAssistantMedadata
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("images")]
        public HomeAssistantImages[] Images { get; set; }

        [JsonProperty("metadataType")]
        public int MetadataType { get; set; }

        [JsonProperty("subtitle")]
        public string Subtitle { get; set; }

        [JsonProperty("seriesTitle")]
        public string SeriesTitle { get; set; }

        [JsonProperty("season")]
        public int? Season { get; set; }

        [JsonProperty("episode")]
        public int? Episode { get; set; }

        [JsonProperty("originalAirDate")]
        public DateTime? OriginalAirDate { get; set; }

        [JsonProperty("albumName")]
        public string AlbumName { get; set; }

        [JsonProperty("albumArtist")]
        public string AlbumArtist { get; set; }

        [JsonProperty("trackNumber")]
        public int? TrackNumber { get; set; }

        [JsonProperty("discNumber")]
        public int? DiscNumber { get; set; }

        [JsonProperty("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonProperty("creationDateTime")]
        public DateTime? CreationDateTime { get; set; }
    }

    public class HomeAssistantImages
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
