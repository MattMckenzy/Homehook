using HomeHook.Attributes;
using HomeHook.Common.Exceptions;
using HomeHook.Common.Models;
using HomeHook.Models;
using HomeHook.Models.Language;
using HomeHook.Services;
using Microsoft.AspNetCore.Mvc;

namespace HomeHook.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DeviceController : ControllerBase
    {
        // TODO: Add missing device controller commands.

        private LanguageService LanguageService { get; }
        private CastService CastService { get; }

        public DeviceController(LanguageService languageService, CastService castService)
        {
            LanguageService = languageService;
            CastService = castService;
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
        public async Task<IActionResult> PostSimpleHook([FromBody] SimplePhrase simplePhrase, [FromQuery] bool launch = true, [FromQuery] string? insertBefore = null) =>
            await SearchPlay(simplePhrase, launch, insertBefore);

        private async Task<IActionResult> SearchPlay(SimplePhrase simplePhrase, bool launch, string? insertBefore)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(simplePhrase.Content))
                    return BadRequest("Please post the search phrase in the body as \"Content\"!");

                LanguagePhrase languagePhrase = await LanguageService.ParseSimplePhrase(simplePhrase.Content);

                (bool getSuccess, DeviceService? deviceService) = await CastService.TryGetDeviceService(languagePhrase.Device);

                if (!getSuccess || deviceService == null)
                    return NotFound($"Requested device \"{languagePhrase.Device}\" not found!");

                int itemCount = 0;
                await foreach (MediaItem mediaItem in deviceService.GetItems(languagePhrase))
                {
                    if (itemCount++ == 0 && launch)
                        await deviceService.LaunchQueue(new List<MediaItem> { mediaItem });
                    else
                        await deviceService.AddItems(new List<MediaItem> { mediaItem }, insertBefore);
                }

                if (itemCount == 0)
                    return NotFound($"No media found! -  \"{languagePhrase.SearchTerm}\", returned no media.");
                else
                    return Ok($"Found {itemCount} item(s) with the search term {simplePhrase.Content}.");
            }
            catch (ConfigurationException configurationException)
            {
                return UnprocessableEntity($"Configuration Error! - {configurationException.Message}.");
            }
            catch (ArgumentException argumentException)
            {
                return BadRequest($"Search term invalid! - {argumentException.Message}.");
            }
        }
    }
}