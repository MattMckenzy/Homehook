using HomeHook.Common.Models;

namespace HomeCast.Models
{
    public class CachingInformation
    {
        public required FileInfo? CacheFileInfo { get; set; } 
        public required string MediaId { get; set; }
        public required CacheStatus CacheStatus { get; set; }
        public required double CachedRatio { get; set; }
    }
}
