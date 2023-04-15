namespace WonkCast.Extensions
{
    public static class EventHandlerExtensions
    {
        public static void InvokeAsync(this EventHandler handler, object sender, EventArgs args)
        {
            Task.Factory.StartNew(() =>
            {
                IEnumerable<Delegate> delegates = handler?.GetInvocationList() ?? Array.Empty<Delegate>();

                foreach (Delegate @delegate in delegates)
                {
                    if (@delegate is EventHandler myEventHandler)
                        Task.Factory.StartNew(() => myEventHandler(sender, args));
                };
            });
        }
    }
}
