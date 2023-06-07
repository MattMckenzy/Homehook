using HomeHook.Common.Models;

namespace HomeCast.Models
{
    public class CacheItem
    {
        public required FileInfo CacheFileInfo { get; set; }
        public required CacheFormat CacheFormat { get; set; }
        public CancellationTokenSource CacheCancellationTokenSource { get; set; } = new();
    }
}