using WonkCast.Common.Models;
using WonkCast.Common.Services;

namespace WonkCast.DeviceService
{
    public class PlayerService
    {
        private LoggingService<PlayerService> LoggingService { get; }
        private IConfiguration Configuration { get; }

        public Device Device { get; }

        public PlayerService(LoggingService<PlayerService> loggingService, IConfiguration configuration)
        {
            LoggingService = loggingService;
            Configuration = configuration;

            string? name = Configuration["Device:Name"];
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Please define a proper device name in the app settings!");

            string? address = Configuration["Device:Address"];
            if (string.IsNullOrWhiteSpace(address))
                throw new InvalidOperationException("Please define a proper device address in the app settings!");

            Device = new Device 
            { 
                Name = name,
                Address = address
            };
        }

        internal void UpdateClients()
        {
            throw new NotImplementedException();
        }

        internal Task<Task> PlayAsync()
        {
            throw new NotImplementedException();
        }

        internal Task StopAsync()
        {
            throw new NotImplementedException();
        }

        internal Task PauseAsync()
        {
            throw new NotImplementedException();
        }

        internal Task NextAsync()
        {
            throw new NotImplementedException();
        }

        internal Task PreviousAsync()
        {
            throw new NotImplementedException();
        }

        internal Task SeekAsync(double timeToSeek)
        {
            throw new NotImplementedException();
        }

        internal Task ChangeCurrentMediaAsync(int mediaId)
        {
            throw new NotImplementedException();
        }

        internal Task ChangeRepeatModeAsync(RepeatMode repeatMode)
        {
            throw new NotImplementedException();
        }

        internal Task SetPlaybackRateAsync(double playbackRate)
        {
            throw new NotImplementedException();
        }

        internal Task StartJellyfinSession(List<Media> media)
        {
            throw new NotImplementedException();
        }

        internal Task InsertQueueAsync(List<Media> media, int? insertBefore)
        {
            throw new NotImplementedException();
        }

        internal Task RemoveQueueAsync(IEnumerable<int> itemIds)
        {
            throw new NotImplementedException();
        }

        internal Task UpQueueAsync(IEnumerable<int> itemIds)
        {
            throw new NotImplementedException();
        }

        internal Task DownQueueAsync(IEnumerable<int> itemIds)
        {
            throw new NotImplementedException();
        }

        internal Task SetVolumeAsync(float volume)
        {
            throw new NotImplementedException();
        }

        internal Task ToggleMutedAsync()
        {
            throw new NotImplementedException();
        }
    }
}
