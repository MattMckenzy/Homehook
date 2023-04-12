using Homehook.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System;

namespace Homehook.Models
{
    public sealed class DeviceRetryPolicy<T> : IRetryPolicy
    {
        private DeviceConfiguration DeviceConfiguration { get; }
        private LoggingService<T> Logger { get; }

        private static TimeSpan?[] DefaultBackoffTimes { get;  } = new TimeSpan?[]
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
        };

        public DeviceRetryPolicy(DeviceConfiguration deviceConfiguration, LoggingService<T> logger)
        {
            DeviceConfiguration = deviceConfiguration;
            Logger = logger;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount == DefaultBackoffTimes.Length)
                _ = Logger.LogError("Device could not connect.", $"Device \"{DeviceConfiguration.Name}\" failed to connect after many attempts. Please verify if the host address ({DeviceConfiguration.Address}) and access tokens are correct. Connections will continue retrying...");

            if (retryContext.PreviousRetryCount > DefaultBackoffTimes.Length)
                return DefaultBackoffTimes[^1];
            else
                return DefaultBackoffTimes[retryContext.PreviousRetryCount];
        }
    }
}
