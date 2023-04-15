namespace WonkCast.Common.Models
{
    public class PhotoMetadata : MediaMetadata
    {
        /// <summary>
        /// Gets or sets the latitude of the content
        /// </summary>
        public double? Latitude { get; set; }

        /// <summary>
        /// Gets or sets the longitude of the content
        /// </summary>
        public double? Longitude { get; set; }

        /// <summary>
        /// Gets or sets the width of the content
        /// </summary>
        public int? Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the content
        /// </summary>
        public int? Height { get; set; }
    }
}
