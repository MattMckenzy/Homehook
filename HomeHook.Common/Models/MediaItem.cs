namespace HomeHook.Common.Models
{
    public class MediaItem
    {
        public required string Id { get; set; }

        public required string Location { get; set; }
        public required MediaSource MediaSource { get; set; }
        public required string Container { get; set; }


        public string User { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = false;
        public long Size { get; set; } = 0;

        public bool Cache { get; set; } = false;
        public CacheFormat? CacheFormat { get; set; } = null;
        public double? CachedRatio = null;

        public double StartTime { get; set; } = 0;
        public double Runtime { get; set; } = 0;
        public required MediaItemKind MediaItemKind { get; set; }
        public required MediaMetadata Metadata { get; set; }

    }
}
