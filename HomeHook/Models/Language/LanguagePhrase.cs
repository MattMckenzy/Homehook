using HomeHook.Services;

namespace HomeHook.Models.Language
{
    public class LanguagePhrase
    {
        public required string SearchTerm { get; set; }

        public required string Device { get; set; }

        public required string User { get; set; }

        public string? PathTerm { get; set; }

        public OrderType OrderType { get; set; } = OrderType.Newest;

        public PlaybackMethod PlaybackMethod { get; set; } = PlaybackMethod.Cached;

        public Source Source { get; set; } = Source.Jellyfin;

        public MediaType MediaType { get; set; } = MediaType.All;

    }
}
