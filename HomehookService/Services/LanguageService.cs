using Homehook.Models;
using Homehook.Models.Jellyfin;
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
        private readonly CastService _castService;
        private readonly LoggingService<LanguageService> _loggingService;

        public LanguageService(IConfiguration configuration, CastService castService, LoggingService<LanguageService> loggingService)
        {
            _configuration = configuration;
            _castService = castService;
            _loggingService = loggingService;
        }

        public async Task<Phrase> ParseJellyfinSimplePhrase(string simplePhrase)
        {
            Phrase jellyPhrase = new()
            {
                User = _configuration["Services:Jellyfin:DefaultUser"],
                Device = _configuration["Services:Jellyfin:DefaultDevice"],
                OrderType = (OrderType)Enum.Parse(typeof(OrderType), _configuration["Services:Jellyfin:DefaultOrder"]),
                MediaType = (MediaType)Enum.Parse(typeof(MediaType), _configuration["Services:Jellyfin:DefaultMediaType"]),
            };

            IEnumerable<string> phraseTokens = ProcessWordMappings(simplePhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));;

            Dictionary<string, IEnumerable<string>> orderTokens = new()
            { 
                { "Continue", _configuration["Services:Jellyfin:OrderTerms:Continue"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Shuffle", _configuration["Services:Jellyfin:OrderTerms:Shuffle"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Ordered", _configuration["Services:Jellyfin:OrderTerms:Ordered"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Oldest", _configuration["Services:Jellyfin:OrderTerms:Oldest"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Newest", _configuration["Services:Jellyfin:OrderTerms:Newest"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Shortest", _configuration["Services:Jellyfin:OrderTerms:Shortest"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Longest", _configuration["Services:Jellyfin:OrderTerms:Longest"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            };
            Dictionary<string, IEnumerable<string>> mediaTypeTokens = new()
            {
                { "Audio", _configuration["Services:Jellyfin:MediaTypeTerms:Audio"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Video", _configuration["Services:Jellyfin:MediaTypeTerms:Video"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Photo", _configuration["Services:Jellyfin:MediaTypeTerms:Photo"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            };

            if (phraseTokens.Any() && orderTokens.SelectMany(tokens => tokens.Value).Any(orderToken => phraseTokens.First().Equals(orderToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyOrderType = orderTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.First(), StringComparison.InvariantCultureIgnoreCase))).Key;

                await _loggingService.LogDebug($"Mapped spoken order token to {spokenJellyOrderType}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                jellyPhrase.OrderType = (OrderType)Enum.Parse(typeof(OrderType), spokenJellyOrderType);

                phraseTokens = phraseTokens.Skip(1).AsEnumerable();
            }

            if (phraseTokens.Count() >= 2 && _configuration["Services:Language:UserPrepositions"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(userPreposition => phraseTokens.Reverse().Skip(1).First().Equals(userPreposition, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyUser = _configuration.GetSection("UserMappings").Get<UserMappings[]>().FirstOrDefault(userMappings => userMappings.Spoken.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(spokenMapping => spokenMapping.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase)))?.Jellyfin;
                
                if (string.IsNullOrWhiteSpace(spokenJellyUser))
                    await _loggingService.LogWarning($"Spoken user not mapped.", "Please map a spoken user to a Jellyfin user.", new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                else
                {
                    await _loggingService.LogDebug($"Mapped spoken user token to {spokenJellyUser}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                    jellyPhrase.User = spokenJellyUser;
                }

                phraseTokens = phraseTokens.SkipLast(2).AsEnumerable();
            }

            if (phraseTokens.Count() >= 2 && _configuration["Services:Language:DevicePrepositions"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(devicePreposition => phraseTokens.Reverse().Skip(1).First().Equals(devicePreposition, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyDevice = _castService.Receivers.Select(receiver => receiver.FriendlyName).FirstOrDefault(mediaPlayer => mediaPlayer.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase));

                if (string.IsNullOrWhiteSpace(spokenJellyDevice))
                    await _loggingService.LogWarning($"Spoken device is not listed.", "Please add spoken device to configuration.", new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                else
                {
                    await _loggingService.LogDebug($"Mapped spoken device token to {spokenJellyDevice}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                    jellyPhrase.Device = spokenJellyDevice;
                }

                phraseTokens = phraseTokens.SkipLast(2).AsEnumerable();
            }

            if (phraseTokens.Any() && mediaTypeTokens.SelectMany(tokens => tokens.Value).Any(mediaTypeToken => phraseTokens.Last().Equals(mediaTypeToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyMediaType = mediaTypeTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                await _loggingService.LogDebug($"Mapped spoken media type token to {spokenJellyMediaType}.", string.Empty, new { SearchTerm = simplePhrase, JellyPhrase = jellyPhrase });
                jellyPhrase.MediaType = (MediaType)Enum.Parse(typeof(MediaType), spokenJellyMediaType);

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }

            jellyPhrase.SearchTerm = string.Join(' ', phraseTokens);

            return jellyPhrase;
        }

        private IEnumerable<string> ProcessWordMappings(string[] phraseTokens)
        {
            Dictionary<string, IEnumerable<string>> wordMappings =
                _configuration.GetSection("Services:Language:WordMappings")
                    .GetChildren()
                    .Where(section => !string.IsNullOrWhiteSpace(section.Key))
                    .ToDictionary(section => section.Key, section => section.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).AsEnumerable());

            for (int index = 0; index < phraseTokens.Length; index++)            
                foreach (string wordMapping in wordMappings.Keys.Reverse())                
                    if (wordMappings[wordMapping].Any(mappedWord => mappedWord.Equals(phraseTokens[index], StringComparison.InvariantCultureIgnoreCase)))
                        phraseTokens[index] = wordMapping;

            return phraseTokens;
        }
    }
}
