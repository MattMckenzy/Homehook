using Homehook.Services;

namespace Homehook.Middleware
{
    public class ExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly LoggingService<ExceptionHandlerMiddleware> _loggingService;

        public ExceptionHandlerMiddleware(RequestDelegate next, LoggingService<ExceptionHandlerMiddleware> loggingService)
        {
            _next = next;
            _loggingService = loggingService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Call the next delegate/middleware in the pipeline
                await _next(context);
            }
            catch (Exception exception)
            {
                await _loggingService.LogError("HomeHook unhandled exception.", "Please contact support if issue persists.", exception: exception);
                throw;
            }
        }
    }
}