using Microsoft.AspNetCore.SignalR.Client;
using HomeHook.Common.Services;

namespace HomeHook.Models
{
    public sealed class DeviceRetryPolicy<T> : IRetryPolicy
    {
        private DeviceConfiguration DeviceConfiguration { get; }
        private LoggingService<T> Logger { get; }

        private static TimeSpan?[] DefaultBackoffTimes { get;  } = new TimeSpan?[]
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5)
        };

        public DeviceRetryPolicy(DeviceConfiguration deviceConfiguration, LoggingService<T> logger)
        {
            DeviceConfiguration = deviceConfiguration;
            Logger = logger;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount == DefaultBackoffTimes.Length)
            {
                _ = Logger.LogError("Device could not connect.", $"Device \"{DeviceConfiguration.Name}\" failed to connect after 3 attempts. Please verify if the host address ({DeviceConfiguration.Address}) and access tokens are correct.");
                return null;
            }

            return DefaultBackoffTimes[retryContext.PreviousRetryCount];
        }
    }
}
