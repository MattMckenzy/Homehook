using Homehook.Attributes;
using Homehook.Models;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
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

        [HttpPost("simple")]
        [ApiKey(ApiKeyName="apiKey", ApiKeyRoutes = new [] { "Services:IFTTT:Token", "Services:HomeAssistant:Token" })]
        public async Task PostJellySimpleHook([FromBody] JellySimplePhrase jellySimplePhrase)
        {
            JellyPhrase jellyPhrase = await _languageService.ParseJellyfinSimplePhrase(jellySimplePhrase.Content);
            await _loggingService.LogDebug("PostJellySimpleHook parsed phrase.", $"Succesfully parsed the following phrase from the search term: {jellySimplePhrase.Content}" , jellyPhrase);

            HomeAssistantMedia homeAssistantMedia = await _jellyfinService.GetItems(jellyPhrase);
            await _loggingService.LogDebug("PostJellySimpleHook items found.", $"Found {homeAssistantMedia.items.Count()} item(s) with the search term {jellyPhrase.SearchTerm}.");
            await _loggingService.LogInformation("PostJellySimpleHook items found.", "Found the following items:", homeAssistantMedia);

            foreach(HomeAssistantMediaItem item in homeAssistantMedia.items)
                await _homeAssistantService.PlayMedia(JsonConvert.SerializeObject(item, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }

        [HttpPost("conversation")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeyRoutes = new[] { "Services:Google:Token" })]
        public async Task PostJellyConversationHook([FromBody] JellyConversation jellyConversation)
        {
            throw new NotImplementedException();

            await _loggingService.LogDebug("PostJellyConversationHook received conversation.", $"Received the foolowing JellyConversation:", jellyConversation);

            JellyPhrase jellyPhrase = new JellyPhrase
            {
                
            };

            HomeAssistantMedia homeAssistantMedia = await _jellyfinService.GetItems(jellyPhrase);
            await _loggingService.LogDebug("PostJellySimpleHook items found.", $"Found {homeAssistantMedia.items.Count()} item(s) with the search term {jellyPhrase.SearchTerm}.");
            await _loggingService.LogInformation("PostJellySimpleHook items found.", "Found the following items:", homeAssistantMedia);

            foreach (HomeAssistantMediaItem item in homeAssistantMedia.items)
                await _homeAssistantService.PlayMedia(JsonConvert.SerializeObject(item, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }
    }
}
  