using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HomeHook.Common.Services
{
    public class LoggingService<T>
    {
        private readonly GotifyService GotifyService;
        private readonly ILogger<T> Logger;
        private readonly IConfiguration Configuration;

        public LoggingService(GotifyService gotifyService, ILogger<T> logger, IConfiguration configuration)
        {
            GotifyService = gotifyService;
            Logger = logger;
            Configuration = configuration;
        }

        public async Task LogDebug(string title, string message, object? extraObject = null, Exception? exception = null) =>
            await Log(LogLevel.Debug, title, message, extraObject, exception, 0);

        public async Task LogInformation(string title, string message, object? extraObject = null, Exception? exception = null) =>
            await Log(LogLevel.Information, title, message, extraObject, exception, 2);
        
        public async Task LogWarning(string title, string message, object? extraObject = null, Exception? exception = null) =>
            await Log(LogLevel.Warning, title, message, extraObject, exception, 5);

        public async Task LogError(string title, string message, object? extraObject = null, Exception? exception = null) =>
            await Log(LogLevel.Error, title, message, extraObject, exception, 8);

        private async Task Log(LogLevel logLevel, string title, string message, object? extraObject, Exception? exception, int gotifyPriority)
        {
            if (extraObject != null)
                message = $"{message}{Environment.NewLine}Object: {JsonConvert.SerializeObject(extraObject)}";
            if (exception != null)
                message = $"{message}{Environment.NewLine}Exception: {JsonConvert.SerializeObject(exception)}";

            try
            {
                if (Configuration.GetValue<int>("Services:Gotify:Priority") <= (int)logLevel)
                    await GotifyService.PushMessage(new() { Title = title, Message = message, Priority = gotifyPriority });
            }
            catch
            {
                Logger.Log(LogLevel.Error, "The gotify service is unavailable. Log message couldn't be pushed.");
            }
            finally
            {
                Logger.Log(logLevel, "{title}{NewLine}Message: {NewLine}{message}",  title, Environment.NewLine, Environment.NewLine, message);
            }
        }
    }
}
