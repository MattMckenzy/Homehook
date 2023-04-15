using WonkCast.Extensions;
using Microsoft.AspNetCore.SignalR.Client;
using WonkCast.Common.Models;

namespace WonkCast.Models
{
    public class DeviceConnection
    {
        public required Device Device { get; set; }
        public required HubConnection HubConnection { get; set; }
        public event EventHandler? DeviceUpdated;
        public void InvokeDeviceUpdatedAsync()
        {
            DeviceUpdated?.InvokeAsync(this, EventArgs.Empty);
        }
    }
}
