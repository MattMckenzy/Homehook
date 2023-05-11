namespace HomeHook.Common.Models
{
    public class SeriesEpisodeMetadata : MediaMetadata
    {
        /// <summary>
        /// Gets or sets the overview
        /// </summary>
        public string? Overview { get; set; }

        /// <summary>
        /// Gets or sets the descriptive series title of the content
        /// </summary>
        public string? SeriesTitle { get; set; }

        /// <summary>
        /// Gets or sets the studio that produced the series
        /// </summary>
        public string? SeriesStudio { get; set; }

        /// <summary>
        /// Gets or sets the season number of the content
        /// </summary>
        public int? SeasonNumber { get; set; }

        /// <summary>
        /// Gets or sets the episode number of the content
        /// </summary>
        public int? EpisodeNumber { get; set; }
    }
}
