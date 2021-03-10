using Homehook.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Services
{
    public class LanguageService
    {
        private readonly IConfiguration _configuration;
        private readonly LoggingService<LanguageService> _loggingService;

        public LanguageService(IConfiguration configuration, LoggingService<LanguageService> loggingService)
        {
            _configuration = configuration;
            _loggingService = loggingService;
        }

        public async Task<JellyPhrase> ParseJellyfinSimplePhrase(string simplePhrase)
        {
            JellyPhrase jellyPhrase = new()
            {
                JellyUser = _configuration["Services:Jellyfin:DefaultUser"],
                JellyDevice = _configuration["Services:Jellyfin:DefaultDevice"],
                JellyOrderType = (JellyOrderType)Enum.Parse(typeof(JellyOrderType), _configuration["Services:Jellyfin:DefaultOrder"]),
                JellyMediaType = (JellyMediaType)Enum.Parse(typeof(JellyMediaType), _configuration["Services:Jellyfin:DefaultMediaType"]),
            };

            IEnumerable<string> phraseTokens = simplePhrase.Split(' ');

            Dictionary<string, IEnumerable<string>> orderTokens = new()
            { 
                { "Continue", _configuration["Services:Jellyfin:OrderTerms:Continue"].Split(",") },
                { "Shuffle", _configuration["Services:Jellyfin:OrderTerms:Shuffle"].Split(",") },
                { "Oldest", _configuration["Services:Jellyfin:OrderTerms:Oldest"].Split(",") },
                { "Newest", _configuration["Services:Jellyfin:OrderTerms:Newest"].Split(",") },
                { "Shortest", _configuration["Services:Jellyfin:OrderTerms:Shortest"].Split(",") },
                { "Longest", _configuration["Services:Jellyfin:OrderTerms:Longest"].Split(",") },
            };
            Dictionary<string, IEnumerable<string>> mediaTypeTokens = new()
            {
                { "Audio", _configuration["Services:Jellyfin:MediaTypeTerms:Audio"].Split(",") },
                { "Video", _configuration["Services:Jellyfin:MediaTypeTerms:Video"].Split(",") },
                { "Photo", _configuration["Services:Jellyfin:MediaTypeTerms:Photo"].Split(",") },
            };

            if (phraseTokens.Count() >= 1 && orderTokens.SelectMany(tokens => tokens.Value).Any(orderToken => phraseTokens.First().Equals(orderToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyOrderType = orderTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.First(), StringComparison.InvariantCultureIgnoreCase))).Key;

                await _loggingService.LogDebug($"Mapped spoken order token to {spokenJellyOrderType}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                jellyPhrase.JellyOrderType = (JellyOrderType)Enum.Parse(typeof(JellyOrderType), spokenJellyOrderType);

                phraseTokens = phraseTokens.Skip(1).AsEnumerable();
            }

            if (phraseTokens.Count() >= 2 && _configuration["Services:Language:UserPrepositions"].Split(',').Any(userPreposition => phraseTokens.Reverse().Skip(1).First().Equals(userPreposition, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyUser = _configuration.GetSection("UserMappings").Get<UserMappings[]>().FirstOrDefault(userMappings => userMappings.Spoken.Split(",").Any(spokenMapping => spokenMapping.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase)))?.Jellyfin;
                
                if (string.IsNullOrWhiteSpace(spokenJellyUser))
                    await _loggingService.LogWarning($"Spoken user not mapped.", "Please map a spoken user to a Jellyfin user.", new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                else
                {
                    await _loggingService.LogDebug($"Mapped spoken user token to {spokenJellyUser}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                    jellyPhrase.JellyUser = spokenJellyUser;
                }

                phraseTokens = phraseTokens.SkipLast(2).AsEnumerable();
            }

            if (phraseTokens.Count() >= 2 && _configuration["Services:Language:DevicePrepositions"].Split(',').Any(devicePreposition => phraseTokens.Reverse().Skip(1).First().Equals(devicePreposition, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyDevice = _configuration["Services:HomeAssistant:MediaPlayers"].Split(",").FirstOrDefault(mediaPlayer => mediaPlayer.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase));

                if (string.IsNullOrWhiteSpace(spokenJellyDevice))
                    await _loggingService.LogWarning($"Spoken device is not listed.", "Please add spoken device to configuration.", new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                else
                {
                    await _loggingService.LogDebug($"Mapped spoken device token to {spokenJellyDevice}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                    jellyPhrase.JellyDevice = spokenJellyDevice;
                }

                phraseTokens = phraseTokens.SkipLast(2).AsEnumerable();
            }

            if (phraseTokens.Count() >= 1 && mediaTypeTokens.SelectMany(tokens => tokens.Value).Any(mediaTypeToken => phraseTokens.Last().Equals(mediaTypeToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyMediaType = mediaTypeTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                await _loggingService.LogDebug($"Mapped spoken media type token to {spokenJellyMediaType}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                jellyPhrase.JellyMediaType = (JellyMediaType)Enum.Parse(typeof(JellyMediaType), spokenJellyMediaType);

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }

            jellyPhrase.SearchTerm = string.Join(' ', phraseTokens);

            return jellyPhrase;
        }
    }
}
