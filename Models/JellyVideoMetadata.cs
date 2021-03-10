using System;

namespace Homehook.Models
{
    public class JellyVideoMetadata
    {
        public string SeriesName { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public DateTime? PremiereDate { get; set; }
    }
}