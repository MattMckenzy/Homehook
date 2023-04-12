using Microsoft.AspNetCore.SignalR.Client;
using WonkCast.Common.Models;

namespace Homehook.Models
{
    public class DeviceConnection
    {
        public required Device Device { get; set; }
        public required HubConnection HubConnection { get; set; }
    }
}
