namespace HomehookApp.Models
{
    public class TableQueueItem
    {
        public int OrderId { get; set; }
        public int ItemId { get; set; }
        public string Title { get; set; }
        public string Subtitle  { get; set; }
        public bool IsPlaying { get; set; }
        public double Runtime { get; set; }

        public override bool Equals(object obj)
        {
            return OrderId == ((TableQueueItem)obj).OrderId && 
                ItemId == ((TableQueueItem)obj).ItemId && 
                Title == ((TableQueueItem)obj).Title && 
                Subtitle == ((TableQueueItem)obj).Subtitle && 
                IsPlaying == ((TableQueueItem)obj).IsPlaying && 
                Runtime == ((TableQueueItem)obj).Runtime;
        }

        public override int GetHashCode()
        {
            throw new System.NotImplementedException();
        }
    }
}
