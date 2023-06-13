namespace HomeCast.Models
{
    public class Script
    {
        public string Key { get { return $"{DeviceModel}-{ScriptType}"; } }
        public required DeviceModel DeviceModel { get; set; }
        public required ScriptType ScriptType { get; set; }
        public string? Arguments { get; set; }
        public bool IsPersistent { get; set; }
    }
}
