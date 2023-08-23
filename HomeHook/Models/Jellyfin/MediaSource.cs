namespace HomeHook.Models.Jellyfin
{
    public class MediaSource
    {
        public required string Id { get; set; }

        public string? Path { get; set; }

        public string? Name { get; set; }

        public long? Size { get; set; }

        public long? RunTimeTicks { get; set; }
    }
}