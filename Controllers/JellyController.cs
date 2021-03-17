using Homehook.Attributes;
using Homehook.Exceptions;
using Homehook.Models;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
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
        private readonly CastService _castService;
        private readonly LoggingService<JellyController> _loggingService;
        private readonly IConfiguration _configuration;

        public JellyController(JellyfinService jellyfinService, LanguageService languageService, CastService castService, LoggingService<JellyController> loggingService, IConfiguration configuration)
        {
            _jellyfinService = jellyfinService;
            _languageService = languageService;
            _castService = castService;
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
            await _loggingService.LogDebug("PostJellySimpleHook items found.", $"Found {homeAssistantMedia.Items.Count()} item(s) with the search term {jellyPhrase.SearchTerm}.");
            await _loggingService.LogInformation("PostJellySimpleHook items found.", "Found the following items:", homeAssistantMedia);

            if (!homeAssistantMedia.Items.Any())
                throw new NotFoundException($"{jellyPhrase.SearchTerm} returned no search results.");

            foreach (HomeAssistantMediaItem item in homeAssistantMedia.Items)
            {
                ReceiverService receiverService = _castService.ReceiverServices.FirstOrDefault(receiverService => receiverService.Receiver.FriendlyName.Equals(jellyPhrase.JellyDevice, StringComparison.InvariantCultureIgnoreCase));
            }
        }

        [HttpPost("conversation")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeyRoutes = new[] { "Services:Google:Token" })]
        public async Task PostJellyConversationHook([FromBody] JellyConversation jellyConversation)
        {
            await _loggingService.LogDebug("PostJellyConversationHook received conversation.", $"Received the following JellyConversation:", jellyConversation);

            JellyPhrase jellyPhrase = new()
            {
                SearchTerm = jellyConversation.RequestJson?.Intent?.Params?.Content?.Resolved ?? string.Empty,
                JellyUser = jellyConversation.RequestJson?.Intent?.Params?.UserName?.Resolved ?? _configuration["Services:Jellyfin:DefaultUser"],
                JellyDevice = jellyConversation.RequestJson?.Intent?.Params?.Device?.Resolved ?? _configuration["Services:Jellyfin:DefaultDevice"],
                JellyOrderType = jellyConversation.RequestJson?.Intent?.Params?.Order?.Resolved != null ?
                    (JellyOrderType)Enum.Parse(typeof(JellyOrderType), jellyConversation.RequestJson?.Intent?.Params?.Order?.Resolved) :
                    (JellyOrderType)Enum.Parse(typeof(JellyOrderType), _configuration["Services:Jellyfin:DefaultOrder"]),
                JellyMediaType = jellyConversation.RequestJson?.Intent?.Params?.MediaType?.Resolved != null ?
                    (JellyMediaType)Enum.Parse(typeof(JellyMediaType), jellyConversation.RequestJson?.Intent?.Params?.MediaType?.Resolved) :
                    (JellyMediaType)Enum.Parse(typeof(JellyMediaType), _configuration["Services:Jellyfin:DefaultOrder"]),
            };

            HomeAssistantMedia homeAssistantMedia = await _jellyfinService.GetItems(jellyPhrase);
            await _loggingService.LogDebug("PostJellySimpleHook items found.", $"Found {homeAssistantMedia.Items.Count()} item(s) with the search term {jellyPhrase.SearchTerm}.");
            await _loggingService.LogInformation("PostJellySimpleHook items found.", "Found the following items:", homeAssistantMedia);

            if (!homeAssistantMedia.Items.Any())
                throw new NotFoundException($"{jellyPhrase.SearchTerm} returned no search results.");
                        
            foreach (HomeAssistantMediaItem item in homeAssistantMedia.Items)
            {
                if (item.Extra.Enqueue == null)
                {
                    ReceiverService receiverService = _castService.ReceiverServices.FirstOrDefault(receiverService => receiverService.Receiver.FriendlyName.Equals(jellyPhrase.JellyDevice, StringComparison.InvariantCultureIgnoreCase));
                }
            }
        }
    }
}
  