using HomeHook.Common.Models;
using Newtonsoft.Json;

namespace HomeHook.Models.Jellyfin
{
    public class Item
    {
        public required string Id { get; set; }

        public string? Name { get; set; }

        public string? Overview { get; set; }

        public string? Path { get; set; }

        public string? Type { get; set; }

        public string? MediaType { get; set; }

        public UserData? UserData { get; set; }

        public int? IndexNumber { get; set; }

        public int? ParentIndexNumber { get; set; }

        public long? RunTimeTicks { get; set; }

        public string? SeriesName { get; set; }

        public string? SeriesStudio { get; set; }

        public IEnumerable<Studio>? Studios { get; set; }

        public string? Album { get; set; }

        public string[]? Artists { get; set; }

        public string? AlbumArtist { get; set; }

        public DateTime? DateCreated { get; set; }

        public DateTime? PremiereDate { get; set; }

        public IEnumerable<MediaSource>? MediaSources { get; set; }
    }
}
