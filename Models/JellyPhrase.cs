namespace Homehook.Models
{
    public class JellyPhrase
    {
        public string SearchTerm { get; set; }
        
        public JellyOrderType JellyOrderType { get; set; }

        public JellyMediaType JellyMediaType { get; set; }

        public string JellyDevice { get; set; }

        public string JellyUser { get; set; }
    }
}
