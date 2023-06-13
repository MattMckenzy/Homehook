using HomeHook.Common.Models;

namespace HomeCast.Models
{
    public class CachingUpdateEventArgs
    {
        public required FileInfo? CacheFileInfo { get; set; } 
        public required string MediaItemId { get; set; }
        public required CacheStatus CacheStatus { get; set; }
        public required double CacheRatio { get; set; }
    }
}
