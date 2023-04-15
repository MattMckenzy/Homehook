namespace WonkCast.Common.Models
{
    public class MediaMetadata
    {
        /// <summary>
        /// Gets or sets the descriptive title of the content.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the descriptive subtitle of the content.
        /// </summary>
        public string? Subtitle { get; set; }

        /// <summary>
        /// Gets or sets the URI of a thumbnail image associated with the content.
        /// </summary>
        public string? ThumbnailUri { get; set; }

        /// <summary>
        /// Gets or sets the creation date of the content
        /// </summary>
        public DateTime? CreationDate { get; set; }
    }
}
