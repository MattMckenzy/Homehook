namespace HomeCast.Models
{
    public class WaitingCommand
    {
        public required int RequestId { get; set; }
        public required TaskCompletionSource<WaitingCommand> Callback { get; set; }
        public string? WaitingEvent { get; set; }
        public bool Success { get; set; } = false;
        public object? Data { get; set; }
        public string? Error { get; set; }  
    }
}
