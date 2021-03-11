using Homehook.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Homehook.Services
{
    public class LoggingService<T>
    {
        private readonly GotifyService _gotifyService;
        private readonly ILogger<T> _logger;
        private readonly IConfiguration _configuration;

        public LoggingService(GotifyService gotifyService, ILogger<T> logger, IConfiguration configuration)
        {
            _gotifyService = gotifyService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task LogDebug(string title, string message, object extraObject = null, Exception exception = null) =>
            await Log(LogLevel.Debug, title, message, extraObject, exception, 0);

        public async Task LogInformation(string title, string message, object extraObject = null, Exception exception = null) =>
            await Log(LogLevel.Information, title, message, extraObject, exception, 2);
        
        public async Task LogWarning(string title, string message, object extraObject = null, Exception exception = null) =>
            await Log(LogLevel.Warning, title, message, extraObject, exception, 5);

        public async Task LogError(string title, string message, object extraObject = null, Exception exception = null) =>
            await Log(LogLevel.Error, title, message, extraObject, exception, 8);

        private async Task Log(LogLevel logLevel, string title, string message, object extraObject, Exception exception, int gotifyPriority)
        {
            if (extraObject != null)
                message = $"{message}{Environment.NewLine}Object: {JsonConvert.SerializeObject(extraObject, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })}";
            if (exception != null)
                message = $"{message}{Environment.NewLine}Exception: {JsonConvert.SerializeObject(exception, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })}";

            if (_configuration.GetValue<int>("Services:Gotify:Priority") <= (int)logLevel)
                await _gotifyService.PushMessage(new() { Title = title, Message = message, Priority = gotifyPriority });

            message = $"{title}{Environment.NewLine}Message: {Environment.NewLine}{message}";

            _logger.Log(logLevel, message);
        }
    }
}
