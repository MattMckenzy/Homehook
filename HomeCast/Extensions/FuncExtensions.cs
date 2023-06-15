namespace HomeCast.Extensions
{
    public static class FuncExtensions
    {
        public static Task InvokeAsync<TArgs>(this Func<TArgs, Task> func, TArgs arguments)
        {
            return func == null ? Task.CompletedTask
                : Task.WhenAll(func.GetInvocationList().Cast<Func<TArgs, Task>>().Select(func => func(arguments)));
        }
    }
}
