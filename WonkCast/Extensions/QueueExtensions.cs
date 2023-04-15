namespace WonkCast.Extensions
{
    public static class QueueExtensions
    {
        public static IEnumerable<T> DequeueMany<T>(this Queue<T> queue, int count)
        {
            for (int i = 0; i < count && queue.Count > 0; i++)
            {
                yield return queue.Dequeue();
            }
        }
    }
}
