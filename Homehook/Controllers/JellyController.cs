using Homehook.Attributes;
using Homehook.Models;
using Homehook.Models.Jellyfin;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;

namespace Homehook.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class JellyController : ControllerBase
    {
        private JellyfinService JellyfinService { get; }
        private LanguageService LanguageService { get; }
        private CastService CastService { get; }
        private LoggingService<JellyController> LoggingService { get; }
        private IConfiguration Configuration { get; }

        public JellyController(JellyfinService jellyfinService, LanguageService languageService, CastService castService, LoggingService<JellyController> loggingService, IConfiguration configuration)
        {
            JellyfinService = jellyfinService;
            LanguageService = languageService;
            CastService = castService;
            LoggingService = loggingService;
            Configuration = configuration;
        }

        [HttpGet("{receiver}/toggleplayback")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> TogglePlayback([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            if (receiverService.CurrentMediaStatus?.PlayerState != null && 
                receiverService.CurrentMediaStatus.PlayerState.Equals("Playing", StringComparison.InvariantCultureIgnoreCase))
                await receiverService.PauseAsync();
            else if (receiverService.CurrentMediaStatus?.PlayerState != null)
                await receiverService.PlayAsync();

            return Ok();
        }

        [HttpGet("{receiver}/togglemute")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> ToggleMute([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.ToggleMutedAsync();

            return Ok();
        }

        [HttpGet("{receiver}/next")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> Next([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.NextAsync();

            return Ok();
        }


        [HttpGet("{receiver}/previous")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> Previous([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.PreviousAsync();

            return Ok();
        }

        [HttpGet("{receiver}/stop")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> Stop([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.StopAsync();

            return Ok();
        }


        [HttpGet("{receiver}/seek")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> Seek([FromRoute] string receiver, [FromQuery]int seconds = 30)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            if (receiverService.CurrentRunTime != null)
                await receiverService.SeekAsync((double)receiverService.CurrentRunTime + seconds);

            return Ok();
        }

        [HttpGet("{receiver}/volume")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> Volume([FromRoute] string receiver, [FromQuery] int change = 10)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.SetVolumeAsync(receiverService.Volume + ((float)change / 100));

            return Ok();
        }
        

        [HttpGet("{receiver}/playbackrate")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> PlaybackRate([FromRoute] string receiver, [FromQuery] double change = 0.5)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            if (receiverService?.CurrentMediaStatus?.PlaybackRate != null)
                await receiverService.SetPlaybackRateAsync(receiverService.CurrentMediaStatus.PlaybackRate + change);

            return Ok();
        }

        [HttpGet("{receiver}/shuffle")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> Shuffle([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.ShuffleQueueAsync();
            await receiverService.ChangeRepeatModeAsync(RepeatMode.RepeatAllAndShuffle);

            return Ok();
        }

        [HttpGet("{receiver}/repeatone")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> RepeatOne([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.ChangeRepeatModeAsync(RepeatMode.RepeatSingle);

            return Ok();
        }

        [HttpGet("{receiver}/repeatall")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> RepeatAll([FromRoute] string receiver)
        {
            ReceiverService receiverService = await CastService.GetReceiverService(receiver);

            if (receiverService == null)
                return NotFound("Requested receiver not found!");

            await receiverService.ChangeRepeatModeAsync(RepeatMode.RepeatAll);

            return Ok();
        }

        [HttpPost("simple")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:Homehook:Tokens")]
        public async Task<IActionResult> PostJellySimpleHook([FromBody] SimplePhrase simplePhrase)
        {
            JellyPhrase phrase = await LanguageService.ParseJellyfinSimplePhrase(simplePhrase.Content);
            await LoggingService.LogDebug("PostJellySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {simplePhrase.Content}" , phrase);

            if (string.IsNullOrWhiteSpace(phrase.SearchTerm))
                return BadRequest("Missing search content! Please specify a search term along with any optional filters.");

            return await ProcessJellyPhrase(phrase, nameof(PostJellySimpleHook));
        }

        private async Task<IActionResult> ProcessJellyPhrase(JellyPhrase phrase, string controllerName)
        {
            phrase.UserId = await JellyfinService.GetUserId(phrase.User);
            if (string.IsNullOrWhiteSpace(phrase.UserId))
            {
                await LoggingService.LogWarning($"{controllerName} - no user found", $"{phrase.SearchTerm}, or the default user, returned no available user IDs.", phrase);
                return BadRequest($"No user found! - {phrase.SearchTerm}, or the default user, returned no available user Ids");
            }

            IEnumerable<QueueItem> items = await JellyfinService.GetItems(phrase);
            await LoggingService.LogDebug($"{controllerName} - items found.", $"Found {items.Count()} item(s) with the search term {phrase.SearchTerm}.");
            await LoggingService.LogInformation($"{controllerName} - items found.", "Found the following items:", items);
            if (!items.Any())
            {
                await LoggingService.LogWarning($"{controllerName} - no results", $"{phrase.SearchTerm} returned no search results.", phrase);
                return NotFound($"No user found! - {phrase.SearchTerm}, or the default user, returned no available user IDs.");
            }
            
            _ = Task.Run(async () => await CastService.StartJellyfinSession(phrase.Device, items));

            return Ok($"Found {items.Count()} item(s) with the search term {phrase.SearchTerm}.");
        }
    }
}
  