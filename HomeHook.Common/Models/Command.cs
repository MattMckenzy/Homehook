namespace HomeHook.Common.Models
{
    public class CommandDefinition
    {        
        public required string Name { get; set; }
        public required string Command { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? MaterialDesignIcon { get; set; }
    }
}
