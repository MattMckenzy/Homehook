using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Homehook.Models
{
    public class HomeAssistantMedia
    {
        [JsonPropertyName("items")]
        public IEnumerable<HomeAssistantMediaItem> Items { get; set; }
    }

    public class HomeAssistantMediaItem
    {
        [JsonPropertyName("entity_id")]
        public string EntityId { get; set; }

        [JsonPropertyName("media_content_type")]
        public string MediaContentType { get; set; }

        [JsonPropertyName("media_content_id")]
        public string MediaContentId { get; set; }

        [JsonPropertyName("extra")]
        public HomeAssistantExtra Extra { get; set; }
    }

    public class HomeAssistantExtra
    {
        [JsonPropertyName("enqueue")]
        public bool? Enqueue { get; set; }

        [JsonPropertyName("metadata")]
        public HomeAssistantMedadata Metadata { get; set; }
    }

    public class HomeAssistantMedadata
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("images")]
        public HomeAssistantImages[] Images { get; set; }

        [JsonPropertyName("metadataType")]
        public int MetadataType { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        [JsonPropertyName("seriesTitle")]
        public string SeriesTitle { get; set; }

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("originalAirDate")]
        public DateTime? OriginalAirDate { get; set; }

        [JsonPropertyName("albumName")]
        public string AlbumName { get; set; }

        [JsonPropertyName("albumArtist")]
        public string AlbumArtist { get; set; }

        [JsonPropertyName("trackNumber")]
        public int? TrackNumber { get; set; }

        [JsonPropertyName("discNumber")]
        public int? DiscNumber { get; set; }

        [JsonPropertyName("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonPropertyName("creationDateTime")]
        public DateTime? CreationDateTime { get; set; }
    }

    public class HomeAssistantImages
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
