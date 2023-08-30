using static System.Runtime.InteropServices.JavaScript.JSType;

namespace HomeHook.Common.Models
{
    public class MediaItem
    {
        public required string Id { get; set; }

        public required string MediaId { get; set; }

        public required string Location { get; set; }
        public required MediaSource MediaSource { get; set; }
        public required string Container { get; set; }

        public string User { get; set; } = string.Empty;
        public long Size { get; set; } = 0;

        public CacheStatus CacheStatus { get; set; } = CacheStatus.Off;
        public double CachedRatio = 0;

        public double StartTime { get; set; } = 0;
        public double Runtime { get; set; } = 0;
        public required MediaItemKind MediaItemKind { get; set; }
        public required MediaMetadata Metadata { get; set; }

        public required IEnumerable<Chapter> Chapters { get; set; } = Array.Empty<Chapter>();
        public required IEnumerable<Track> VideoTracks { get; set; } = Array.Empty<Track>();
        public required IEnumerable<Track> AudioTracks { get; set; } = Array.Empty<Track>();
        public required IEnumerable<Track> SubtitleTracks { get; set; } = Array.Empty<Track>();

    }
}
