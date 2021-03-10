using Homehook.Attributes;
using Homehook.Models;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class JellyController : ControllerBase
    {
        private readonly JellyfinService _jellyfinService;
        private readonly LanguageService _languageService;
        private readonly HomeAssistantService _homeAssistantService;
        private readonly LoggingService<JellyController> _loggingService;
        private readonly IConfiguration _configuration;

        public JellyController(JellyfinService jellyfinService, LanguageService languageService, HomeAssistantService homeAssistantService, LoggingService<JellyController> loggingService, IConfiguration configuration)
        {
            _jellyfinService = jellyfinService;
            _languageService = languageService;
            _homeAssistantService = homeAssistantService;
            _loggingService = loggingService;
            _configuration = configuration;
        }

        [HttpPost]
        [ApiKey(ApiKeyName="apiKey", ApiKeyRoutes = new [] { "Services:IFTTT:Token", "Services:HomeAssistant:Token" })]
        public async Task PostJellyHook([FromBody] JellySimplePhrase jellySimplePhrase)
        {
            JellyPhrase jellyPhrase = await _languageService.ParseJellyfinSimplePhrase(jellySimplePhrase.Content);
            await _loggingService.LogDebug("PostJellyHook parsed phrase.", $"Succesfully parsed the following phrase from the search term: {jellySimplePhrase.Content}" , jellyPhrase);

            IEnumerable<JellyItem> items = await _jellyfinService.GetItems(jellyPhrase);
            await _loggingService.LogDebug("PostJellyHook items found.", $"Found {items.Count()} item(s) with the search term {jellyPhrase.SearchTerm}.");
            await _loggingService.LogInformation("PostJellyHook items found.", "Found the following items:", items);

            foreach ((JellyItem item, int index) in items.Select((v, i) => (v, i)))
                if (index <= _configuration.GetValue<int>("Services:Jellyfin:MaaxQueueSize"))
                    await _homeAssistantService.PostHook(_configuration["Services:HomeAssistant:JellyWebhookId"], JsonConvert.SerializeObject(item));
        }
    }
}
  