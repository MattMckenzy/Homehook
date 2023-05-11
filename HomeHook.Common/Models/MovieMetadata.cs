namespace HomeHook.Common.Models
{
    public class MovieMetadata : MediaMetadata
    {
        /// <summary>
        /// Gets or sets the overview
        /// </summary>
        public string? Overview { get; set; }

        /// <summary>
        /// Gets or sets the studios that produced the movie
        /// </summary>
        public string? Studios { get; set; }
    }
}
