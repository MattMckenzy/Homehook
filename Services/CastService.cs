using GoogleCast;
using Homehook.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Generic;

namespace Homehook
{
    public class CastService
    {
        private readonly LoggingService<CastService> _loggingService;

        public ObservableCollection<ReceiverService> ReceiverServices { get; } = new();

        public CastService(LoggingService<CastService> loggingService)
        {
            _loggingService = loggingService;

            Task.Run(async () =>
            {
                foreach (IReceiver receiver in await new DeviceLocator().FindReceiversAsync())
                    ReceiverServices.Add(new ReceiverService(receiver));                
            });   
        }      

        public async Task<ReceiverService> GetReceiverService(string receiverName)
        {
            ReceiverService returningReceiverService = 
                ReceiverServices.FirstOrDefault(receiverServices => receiverServices.Receiver.FriendlyName.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase));

            if (returningReceiverService == null)
            {
                await RefreshReceivers();

                returningReceiverService =
                    ReceiverServices.FirstOrDefault(receiverServices => receiverServices.Receiver.FriendlyName.Equals(receiverName, StringComparison.InvariantCultureIgnoreCase));
            }

            if (returningReceiverService == null)
                await _loggingService.LogError($"{receiverName} not found.", $"Requested receiver {receiverName} is not available. Please make sure device is connected and try again.");
           
            return returningReceiverService;
        }

        public async Task RefreshReceivers()
        {
            IEnumerable<IReceiver> newReceivers = await new DeviceLocator().FindReceiversAsync();

            foreach (IReceiver newReceiver in newReceivers)
            {
                ReceiverService oldReceiverService = 
                    ReceiverServices.FirstOrDefault(receiverService => receiverService.Receiver.Id.Equals(newReceiver.Id, StringComparison.InvariantCultureIgnoreCase));

                if (oldReceiverService == null)
                    ReceiverServices.Add(new ReceiverService(newReceiver));
            }

            foreach (ReceiverService oldReceiverService in ReceiverServices.ToArray())
            {
                IReceiver newReceiver =
                    newReceivers.FirstOrDefault(receiver => receiver.Id.Equals(oldReceiverService.Receiver.Id, StringComparison.InvariantCultureIgnoreCase));

                if (newReceiver == null)
                    ReceiverServices.Remove(oldReceiverService);
            }
        }
    }
}