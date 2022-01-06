using GoogleCast.Models.Media;
using Homehook.Attributes;
using Homehook.Extensions;
using Homehook.Models;
using Homehook.Models.Jellyfin;
using Homehook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
            JellyPhrase phrase = await _languageService.ParseJellyfinSimplePhrase(simplePhrase.Content);
            await _loggingService.LogDebug("PostJellySimpleHook - parsed phrase.", $"Succesfully parsed the following phrase from the search term: {simplePhrase.Content}" , phrase);

            if (string.IsNullOrWhiteSpace(phrase.SearchTerm))
                return BadRequest("Missing search content! Please specify a search term along with any optional filters.");

            return await ProcessJellyPhrase(phrase, nameof(PostJellySimpleHook));
        }

        [HttpPost("conversation")]
        [ApiKey(ApiKeyName = "apiKey", ApiKeyRoutes = new[] { "Services:Google:Token" })]
        public async Task<IActionResult> PostJellyConversationHook([FromBody] JsonElement entity)
        {
            await _loggingService.LogDebug("PostJellyConversationHook - received conversation.", $"Received the following JellyConversation:", entity.GetRawText());

            string orderType = entity.Get("requestJson")?.Get("intent")?.Get("params")?.Get("Order")?.Get("resolved")?.GetString() ?? _configuration["Services:Jellyfin:DefaultOrder"];
            string mediaType = entity.Get("requestJson")?.Get("intent")?.Get("params")?.Get("MediaType")?.Get("resolved")?.GetString() ?? _configuration["Services:Jellyfin:DefaultMediaType"];
            JellyPhrase phrase = new()
            {
                SearchTerm = entity.Get("requestJson")?.Get("intent")?.Get("params")?.Get("Content")?.Get("resolved")?.GetString() ?? string.Empty,
                User = entity.Get("requestJson")?.Get("intent")?.Get("params")?.Get("UserName")?.Get("resolved")?.GetString() ?? _configuration["Services:Jellyfin:DefaultUser"],
                Device = entity.Get("requestJson")?.Get("intent")?.Get("params")?.Get("Device")?.Get("resolved")?.GetString() ?? _configuration["Services:Jellyfin:DefaultDevice"],
                OrderType = (OrderType)Enum.Parse(typeof(OrderType), orderType),
                MediaType = (MediaType)Enum.Parse(typeof(MediaType), mediaType)
            };

            return await ProcessJellyPhrase(phrase, nameof(PostJellyConversationHook));
        }

        private async Task<IActionResult> ProcessJellyPhrase(JellyPhrase phrase, string controllerName)
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
            
            _ = Task.Run(async () => await _castService.StartJellyfinSession(phrase.Device, items));

            return Ok($"Found {items.Count()} item(s) with the search term {phrase.SearchTerm}.");
        }
    }
}
  