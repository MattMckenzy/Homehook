namespace HomehookApp.Extensions
{
    public static class TimeSpanExtensions
    {
        public static TimeSpan StripMilliseconds(this TimeSpan timeSpan) =>
            new(timeSpan.Days, timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
        
    }
}
