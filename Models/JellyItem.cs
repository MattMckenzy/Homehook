namespace Homehook.Models
{
    public class JellyItem
    {
        public int Index { get; set; }
        public string Device { get; set; }
        public string Url { get; set; }
        public string ImageUrl { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string MediaType { get; set; }

        public JellyVideoMetadata JellyVideoMetadata { get; set; }
        public JellyAudioMetadata JellyAudioMetadata { get; set; }
        public JellyPhotoMetadata JellyPhotoMetadata { get; set; }
    }
}
