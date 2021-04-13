using GoogleCast.Models.Media;
using Homehook.Attributes;
using Homehook.Models;
using Homehook.Models.Jellyfin;
using Homehook.Models.Jellyfin.Converation;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
        public async Task<IActionResult> PostJellySimpleHook([FromBody] SimplePhrase simplePhrase)
        {
            Phrase phrase = await _languageService.ParseJellyfinSimplePhrase(simplePhrase.Content);
            await _loggingService.LogDebug("PostJellySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {simplePhrase.Content}" , phrase);

            return await ProcessJellyPhrase(phrase, nameof(PostJellySimpleHook));
        }

        [HttpPost("conversation")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeyRoutes = new[] { "Services:Google:Token" })]
        public async Task<IActionResult> PostJellyConversationHook([FromBody] Conversation conversation)
        {
            await _loggingService.LogDebug("PostJellyConversationHook - received conversation.", $"Received the following JellyConversation:", conversation);

            Phrase phrase = new()
            {
                SearchTerm = conversation.RequestJson?.Intent?.Params?.Content?.Resolved ?? string.Empty,
                User = conversation.RequestJson?.Intent?.Params?.UserName?.Resolved ?? _configuration["Services:Jellyfin:DefaultUser"],
                Device = conversation.RequestJson?.Intent?.Params?.Device?.Resolved ?? _configuration["Services:Jellyfin:DefaultDevice"],
                OrderType = (OrderType)(conversation.RequestJson?.Intent?.Params?.Order?.Resolved != null ?
                    Enum.Parse(typeof(OrderType), conversation.RequestJson?.Intent?.Params?.Order?.Resolved) :
                    Enum.Parse(typeof(OrderType), _configuration["Services:Jellyfin:DefaultOrder"])),
                MediaType = (Models.MediaType)(conversation.RequestJson?.Intent?.Params?.MediaType?.Resolved != null ?
                    Enum.Parse(typeof(Models.MediaType), conversation.RequestJson?.Intent?.Params?.MediaType?.Resolved) :
                    Enum.Parse(typeof(Models.MediaType), _configuration["Services:Jellyfin:DefaultOrder"])),
            };

            return await ProcessJellyPhrase(phrase, nameof(PostJellyConversationHook));
        }

        private async Task<IActionResult> ProcessJellyPhrase(Phrase phrase, string controllerName)
        {
            phrase.UserId = await _jellyfinService.GetUserId(phrase.User);
            if (string.IsNullOrWhiteSpace(phrase.UserId))
            {
                await _loggingService.LogWarning($"{controllerName} - no user found", $"{phrase.SearchTerm}, or the default user, returned no available user IDs.", phrase);
                return BadRequest($"No user found! - {phrase.SearchTerm}, or the default user, returned no available user Ids");
            }

            IEnumerable<QueueItem> items = await _jellyfinService.GetItems(phrase);
            await _loggingService.LogDebug($"{controllerName} - items found.", $"Found {items.Count()} item(s) with the search term {phrase.SearchTerm}.");
            await _loggingService.LogInformation($"{controllerName} - items found.", "Found the following items:", items);
            if (!items.Any())
            {
                await _loggingService.LogWarning($"{controllerName} - no results", $"{phrase.SearchTerm} returned no search results.", phrase);
                return NotFound($"No user found! - {phrase.SearchTerm}, or the default user, returned no available user IDs.");
            }

            await _castService.StartJellyfinSession(phrase.Device, items);

            return Ok($"Found {items.Count()} item(s) with the search term {phrase.SearchTerm}.");
        }
    }
}
  