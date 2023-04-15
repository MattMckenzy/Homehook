namespace WonkCast.Models.Jellyfin
{
    public class JellyPhrase
    {
        public required string SearchTerm { get; set; }

        public required string Device { get; set; }

        public required string User { get; set; }

        public string? PathTerm { get; set; }

        public bool Cache { get; set; } = false;

        public OrderType OrderType { get; set; } = OrderType.Newest;

        public MediaType MediaType { get; set; } = MediaType.All;

    }
}
