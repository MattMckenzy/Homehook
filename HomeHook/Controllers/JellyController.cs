using HomeHook.Attributes;
using HomeHook.Models;
using HomeHook.Models.Jellyfin;
using HomeHook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using HomeHook.Common.Models;
using HomeHook.Common.Services;

namespace HomeHook.Controllers
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
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> TogglePlayback([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.PlayPause();

            return Ok();
        }

        [HttpGet("{receiver}/togglemute")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> ToggleMute([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.ToggleMute();

            return Ok();
        }

        [HttpGet("{receiver}/next")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> Next([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.Next();

            return Ok();
        }


        [HttpGet("{receiver}/previous")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> Previous([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.Previous();

            return Ok();
        }

        [HttpGet("{receiver}/stop")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> Stop([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.Stop();

            return Ok();
        }


        [HttpGet("{receiver}/seek")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> Seek([FromRoute] string deviceName, [FromQuery]float seconds = 30)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.SeekRelative(seconds);

            return Ok();
        }

        [HttpGet("{receiver}/volume")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> Volume([FromRoute] string deviceName, [FromQuery] float change = 10)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.SetVolume(deviceService.Device.Volume + (change / 100));

            return Ok();
        }
        

        [HttpGet("{receiver}/playbackrate")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> PlaybackRate([FromRoute] string deviceName, [FromQuery] double playbackRate = 1)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.SetPlaybackRate(playbackRate);

            return Ok();
        }

        [HttpGet("{receiver}/shuffle")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> Shuffle([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.SetRepeatMode(RepeatMode.Shuffle);

            return Ok();
        }

        [HttpGet("{receiver}/repeatone")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> RepeatOne([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.SetRepeatMode(RepeatMode.One);

            return Ok();
        }

        [HttpGet("{receiver}/repeatall")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> RepeatAll([FromRoute] string deviceName)
        {
            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(deviceName);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{deviceName}\" not found!");

            await deviceService.SetRepeatMode(RepeatMode.All);

            return Ok();
        }

        [HttpPost("simple")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeysRoute = "Services:HomeHook:Tokens")]
        public async Task<IActionResult> PostJellySimpleHook([FromBody] SimplePhrase simplePhrase)
        {
            if (string.IsNullOrWhiteSpace(simplePhrase.Content))
                return BadRequest("Please post the search phrase in the body as \"Content\"!");

            JellyPhrase? phrase = await LanguageService.ParseJellyfinSimplePhrase(simplePhrase.Content);
            
            if (phrase == null)
                return BadRequest("Missing search content! Please specify a search term along with any optional filters.");
            else
                await LoggingService.LogDebug("PostJellySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {simplePhrase.Content}", phrase);

            return await ProcessJellyPhrase(phrase, nameof(PostJellySimpleHook));
        }

        private async Task<IActionResult> ProcessJellyPhrase(JellyPhrase phrase, string controllerName)
        {
            string? userId = await JellyfinService.GetUserId(phrase.User);
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest($"No user found! - {phrase.SearchTerm}, or the default user, returned no available user Ids");
            
            List<MediaItem> queueItems = await JellyfinService.GetItems(phrase, userId);
            await LoggingService.LogDebug($"{controllerName} - items found.", $"Found {queueItems.Count} item(s) with the search term {phrase.SearchTerm}.");
            await LoggingService.LogInformation($"{controllerName} - items found.", "Found the following items:", queueItems);
            if (!queueItems.Any())
            {
                await LoggingService.LogWarning($"{controllerName} - no results", $"{phrase.SearchTerm} returned no search results.", phrase);
                return NotFound($"No user found! - {phrase.SearchTerm}, or the default user, returned no available user IDs.");
            }

            (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(phrase.Device);

            if (!getSuccess || deviceService == null)
                return NotFound($"Requested device \"{phrase.Device}\" not found!");

            _ = Task.Run(async () => await deviceService.StartJellyfinSession(queueItems));

            return Ok($"Found {queueItems.Count} item(s) with the search term {phrase.SearchTerm}.");
        }
    }
}
  