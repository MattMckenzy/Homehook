namespace HomeCast.Models
{
    public class DeviceUpdateEventArgs
    {
        public required string Property { get; set; }
        public required object Value { get; set; }
    }
}