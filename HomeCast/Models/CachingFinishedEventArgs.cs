using HomeHook.Common.Models;

namespace HomeCast.Models
{
    public class CachingFinishedEventArgs
    {
        public required CacheItem CacheItem { get; set; } 
        public required MediaItem MediaItem { get; set; }
    }
}
