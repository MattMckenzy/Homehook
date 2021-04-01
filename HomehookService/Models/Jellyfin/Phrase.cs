namespace Homehook.Models.Jellyfin
{
    public class Phrase
    {
        public string SearchTerm { get; set; }
        
        public OrderType OrderType { get; set; }

        public MediaType MediaType { get; set; }

        public string Device { get; set; }

        public string User { get; set; }

        public string UserId { get; set; }
    }
}
