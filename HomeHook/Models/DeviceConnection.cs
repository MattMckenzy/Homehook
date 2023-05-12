using HomeHook.Extensions;
using Microsoft.AspNetCore.SignalR.Client;
using HomeHook.Common.Models;

namespace HomeHook.Models
{
    public class DeviceConnection
    {
        public required Device Device { get; set; }
        public required HubConnection HubConnection { get; set; }

        public double CurrentTime { get; set; } = 0;

        public event DeviceEventHandler? DeviceUpdatedHandler;
        public void InvokeDeviceUpdatedAsync()
        {
            DeviceUpdatedHandler?.Invoke(this, Device);
        }
    }
}
