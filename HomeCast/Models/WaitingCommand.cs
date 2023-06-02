namespace HomeCast.Models
{
    public class WaitingCommand
    {
        public required long RequestId { get; set; }
        public required TaskCompletionSource<WaitingCommand> Callback { get; set; }
        public bool Success { get; set; } = false;
        public object? Data { get; set; }
        public string? Error { get; set; }  
    }
}
