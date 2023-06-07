using System.Collections.Concurrent;

namespace HomeCast.Models
{
    public class SemaphoreQueue
    {
        private ConcurrentQueue<SemaphoreSlim> WaitQueue { get; } = new();
        
        private int MaxConcurrency { get; }

        private int ConcurrencyCount = 0;

        private SemaphoreSlim ConcurrentSemaphoreSlim { get; }

        public SemaphoreQueue(int maxConcurrency)
        {
            MaxConcurrency = maxConcurrency;
            ConcurrentSemaphoreSlim = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public async Task WaitAsync()
        {
            SemaphoreSlim waitingSemaphoreSlim;
            lock (ConcurrentSemaphoreSlim)
            {
                if (ConcurrencyCount < MaxConcurrency)
                {
                    waitingSemaphoreSlim = new SemaphoreSlim(1, 1);
                    Interlocked.Increment(ref ConcurrencyCount);
                }
                else
                {
                    waitingSemaphoreSlim = new SemaphoreSlim(0, 1);
                    WaitQueue.Enqueue(waitingSemaphoreSlim);
                }
            }

            await waitingSemaphoreSlim.WaitAsync();

            await ConcurrentSemaphoreSlim.WaitAsync();
        }

        public void Release()
        {
            lock (ConcurrentSemaphoreSlim)
            {
                ConcurrentSemaphoreSlim.Release();

                Interlocked.Decrement(ref ConcurrencyCount);
                if (WaitQueue.TryDequeue(out SemaphoreSlim? semaphoreSlim) && semaphoreSlim != null)
                    semaphoreSlim.Release();
            }
        }
    }
}