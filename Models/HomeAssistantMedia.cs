using System;
using System.Collections.Generic;

namespace Homehook.Models
{
    public class HomeAssistantMedia
    {
        public IEnumerable<HomeAssistantMediaItem> items { get; set; }
    }

    public class HomeAssistantMediaItem
    {
        public string entity_id { get; set; }
        public string media_content_type { get; set; }
        public string media_content_id { get; set; }
        public HomeAssistantExtra extra { get; set; }
    }

    public class HomeAssistantExtra
    {
        public bool? enqueue { get; set; }
        public HomeAssistantMedadata metadata { get; set; }
    }

    public class HomeAssistantMedadata
    {
        public string title { get; set; }
        public HomeAssistantImages[] images { get; set; }
        public int metadataType { get; set; }
        public string subtitle { get; set; }
        public string seriesTitle { get; set; }
        public int? season { get; set; }
        public int? episode { get; set; }
        public DateTime? originalAirDate { get; set; }
        public string albumName { get; set; }
        public string albumArtist { get; set; }
        public int? trackNumber { get; set; }
        public int? discNumber { get; set; }
        public DateTime? releaseDate { get; set; }
        public DateTime? creationDateTime { get; set; }
    }

    public class HomeAssistantImages
    {
        public string url { get; set; }
    }
}
