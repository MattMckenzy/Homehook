namespace WonkCast.Common.Models
{
    public class GotifyMessage
    {
        public int? AppId { get; set; }
        public int? Id { get; set; }

        public required string Title { get; set; }
        public required string Message { get; set; }

        public DateTime? Date { get; set; }

        public int? Priority { get; set; }

    }
}
