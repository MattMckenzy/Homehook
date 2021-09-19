using Homehook.Attributes;
using Homehook.Models;
using Homehook.Models.Homey;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Homehook.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HomeyController : ControllerBase
    {
        private readonly HomeAssistantService _homeAssistantService;
        private readonly LanguageService _languageService;
        private readonly LoggingService<HomeyController> _loggingService;

        public HomeyController(HomeAssistantService homeAssistantService, LanguageService languageService, LoggingService<HomeyController> loggingService)
        {
            _homeAssistantService = homeAssistantService;
            _languageService = languageService;
            _loggingService = loggingService;
        }

        [HttpPost("simple")]
        [ApiKey(ApiKeyName="apiKey", ApiKeyRoutes = new [] { "Services:IFTTT:Token", "Services:HomeAssistant:Token" })]
        public async Task<IActionResult> PostHomeySimpleHook([FromBody] SimplePhrase simplePhrase)
        {
            HomeyPhrase phrase = await _languageService.ParseHomeySimplePhrase(simplePhrase.Content);
            await _loggingService.LogDebug("PostHomeySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {simplePhrase.Content}", phrase);

            if (string.IsNullOrWhiteSpace(phrase.WebhookId))
                return BadRequest($"No webhook found in phrase: {simplePhrase.Content}. Please make sure to include webhook keywords in phrase!");

            await _homeAssistantService.PostWebhook(phrase.WebhookId, phrase.Content);

            return Ok($"Processing webhook \"{phrase.WebhookId}\"{(string.IsNullOrWhiteSpace(phrase.Content) ? string.Empty : $" with the content: \"{phrase.Content}\"" )}.");
        }
    }
}
  