using GoogleCast.Models.Media;
using System.Collections.Generic;

namespace HomehookCommon.Models
{
    public class ReceiverStatus
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string IPAddress { get; set; }
        public bool IsMediaInitialized { get; set; }
        public bool IsStopped { get; set; }
        public float Volume { get; set; }
        public bool IsMuted { get; set; }
        public MediaStatus CurrentMediaStatus { get; set; }
        public MediaInformation CurrentMediaInformation { get; set; }
        public int? CurrentRunTime { get; set; }
        public IEnumerable<QueueItem> Queue { get; set; }
    }
}
