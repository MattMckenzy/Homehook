namespace WonkCast.Common.Models
{
    public class Media
    {
        public required string Id { get; set; }
        public required string Location { get; set; }
        public required MediaKind MediaKind { get; set; }
        public required MediaMetadata Metadata { get; set; }
        public bool Cache { get; set; } = false;
        public double StartTime { get; set; } = 0;
        public double Runtime { get; set; } = 0;
    }
}
