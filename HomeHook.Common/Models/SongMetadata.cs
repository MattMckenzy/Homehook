namespace HomeHook.Common.Models
{
    public class SongMetadata : MediaMetadata
    {
        /// <summary>
        /// Gets or sets the descriptive album name of the content
        /// </summary>
        public string? AlbumName { get; set; }

        /// <summary>
        /// Gets or sets the descriptive album artist of the content
        /// </summary>
        public string? AlbumArtist { get; set; }

        /// <summary>
        /// Gets or sets the descriptive artist of the content
        /// </summary>
        public string? Artist { get; set; }

        /// <summary>
        /// Gets or sets the descriptive composer of the content
        /// </summary>
        public string? Composer { get; set; }

        /// <summary>
        /// Gets or sets the track number of the content
        /// </summary>
        public int? TrackNumber { get; set; }

        /// <summary>
        /// Gets or sets the disc number of the content
        /// </summary>
        public int? DiscNumber { get; set; }       
    }
}
