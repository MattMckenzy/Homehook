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
        private enum PrepositionType
        { 
            None,
            User,
            Device,
            Path
        }

        private readonly IConfiguration _configuration;
        private readonly CastService _castService;
        private readonly LoggingService<LanguageService> _loggingService;

        public LanguageService(IConfiguration configuration, CastService castService, LoggingService<LanguageService> loggingService)
        {
            _configuration = configuration;
            _castService = castService;
            _loggingService = loggingService;
        }

        public async Task<JellyPhrase> ParseJellyfinSimplePhrase(string simplePhrase)
        {
            JellyPhrase jellyPhrase = new()
            {
                User = _configuration["Services:Jellyfin:DefaultUser"],
                Device = _configuration["Services:Jellyfin:DefaultDevice"],
                OrderType = (OrderType)Enum.Parse(typeof(OrderType), _configuration["Services:Jellyfin:DefaultOrder"]),
                MediaType = (MediaType)Enum.Parse(typeof(MediaType), _configuration["Services:Jellyfin:DefaultMediaType"]),
            };

            IEnumerable<string> phraseTokens = ProcessWordMappings(simplePhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));;


            // Prepare recognized tokens and prepositions.

            Dictionary<string, IEnumerable<string>> orderTokens = new()
            {
                { "Watch", _configuration["Services:Jellyfin:OrderTerms:Watch"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Played", _configuration["Services:Jellyfin:OrderTerms:Played"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Unplayed", _configuration["Services:Jellyfin:OrderTerms:Unplayed"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
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

            IEnumerable<string> userPrepositions = _configuration["Services:Language:UserPrepositions"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> devicePrepositions = _configuration["Services:Language:DevicePrepositions"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> pathPrepositions = _configuration["Services:Language:PathPrepositions"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            IEnumerable<(PrepositionType type, string preposition)> prepositions = 
                userPrepositions.Select(preposition => (PrepositionType.User, preposition))
                .Union(devicePrepositions.Select(preposition => (PrepositionType.Device, preposition)))
                .Union(pathPrepositions.Select(preposition => (PrepositionType.Path, preposition)));

            IEnumerable<(PrepositionType type, string token)> typedPhraseTokens = 
                phraseTokens.Select(token=> (token, prepositions.FirstOrDefault(prepositionItem => prepositionItem.preposition.Equals(token, StringComparison.InvariantCultureIgnoreCase)).type))
                .Select(prepositionItem => (prepositionItem.type, prepositionItem.token));


            // Map spoken Jelly device.

            string spokenJellyDevice = string.Empty;
            (spokenJellyDevice, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.Device, typedPhraseTokens);
            string jellyDevice = _castService.Receivers
                .FirstOrDefault(receiver => receiver.FriendlyName.Equals(spokenJellyDevice, StringComparison.InvariantCultureIgnoreCase))
                ?.FriendlyName;

            if (string.IsNullOrWhiteSpace(jellyDevice))
                await _loggingService.LogWarning($"Spoken device is not listed.", $"Please add  \"{spokenJellyDevice}\" to device configuration.", jellyPhrase);
            else
            {
                await _loggingService.LogDebug($"Mapped spoken device token to {jellyDevice}.", string.Empty, jellyPhrase);
                jellyPhrase.Device = jellyDevice;
            }


            // Map spoken user.

            string spokenJellyUser = string.Empty;
            (spokenJellyUser, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.User, typedPhraseTokens);
            string jellyUser = 
                _configuration.GetSection("UserMappings").Get<UserMappings[]>()
                .FirstOrDefault(userMappings => userMappings.Spoken.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(spokenMapping => spokenMapping.Equals(spokenJellyUser, StringComparison.InvariantCultureIgnoreCase)))?
                .Jellyfin;

            if (string.IsNullOrWhiteSpace(jellyUser))
                await _loggingService.LogWarning($"Spoken user not mapped.", $"Please map \"{spokenJellyUser}\" to a Jellyfin user.", jellyPhrase);
            else
            {
                await _loggingService.LogDebug($"Mapped spoken user token to {jellyUser}.", string.Empty, jellyPhrase);
                jellyPhrase.User = jellyUser;
            }


            // Map spoken path term.

            string spokenPathTerm = string.Empty;
            (spokenPathTerm, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.Path, typedPhraseTokens);

            await _loggingService.LogDebug($"Mapped spoken path term to {spokenPathTerm}.", string.Empty, jellyPhrase);
            jellyPhrase.PathTerm = spokenPathTerm;

            phraseTokens = typedPhraseTokens.Select(tokenItem => tokenItem.token);


            // Map spoken media order.

            if (phraseTokens.Any() && orderTokens.SelectMany(tokens => tokens.Value).Any(orderToken => phraseTokens.First().Equals(orderToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyOrderType = orderTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.First(), StringComparison.InvariantCultureIgnoreCase))).Key;

                await _loggingService.LogDebug($"Mapped spoken order token to {spokenJellyOrderType}.", string.Empty, jellyPhrase);
                jellyPhrase.OrderType = (OrderType)Enum.Parse(typeof(OrderType), spokenJellyOrderType);

                phraseTokens = phraseTokens.Skip(1).AsEnumerable();
            }


            // Map spoken media type.

            if (phraseTokens.Any() && mediaTypeTokens.SelectMany(tokens => tokens.Value).Any(mediaTypeToken => phraseTokens.Last().Equals(mediaTypeToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyMediaType = mediaTypeTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                await _loggingService.LogDebug($"Mapped spoken media type token to {spokenJellyMediaType}.", string.Empty, jellyPhrase);
                jellyPhrase.MediaType = (MediaType)Enum.Parse(typeof(MediaType), spokenJellyMediaType);

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }


            // Remaining phrase tokens are search term.
            
            jellyPhrase.SearchTerm = string.Join(' ', phraseTokens); 
            await _loggingService.LogDebug($"Mapped search term to {jellyPhrase.SearchTerm}.", string.Empty, jellyPhrase);


            return jellyPhrase;
        }

        private (string, IEnumerable<(PrepositionType type, string token)>) ExtractPrepositionTerm(PrepositionType type, IEnumerable<(PrepositionType type, string token)> typedPhraseTokens)
        {
            List<string> extractedTokens = new();
            List<(PrepositionType type, string token)> remainderTokens = new();
            bool isExtracting = false;
            foreach((PrepositionType type, string token) typedPhraseToken in typedPhraseTokens)
            {
                if (typedPhraseToken.type == type)
                {
                    isExtracting = true;
                }
                else if (typedPhraseToken.type != PrepositionType.None)
                {
                    isExtracting = false;
                    remainderTokens.Add(typedPhraseToken);
                }
                else if (isExtracting)
                {
                    extractedTokens.Add(typedPhraseToken.token);
                }
                else
                {
                    remainderTokens.Add(typedPhraseToken);
                }
            }

            return (string.Join(' ', extractedTokens), remainderTokens);
        }

        public Task<HomeyPhrase> ParseHomeySimplePhrase(string simplePhrase)
        {
            HomeyPhrase homeyPhrase = new();

            IEnumerable<string> phraseTokens = ProcessWordMappings(simplePhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); ;

            if (phraseTokens.Any())
            {
                Dictionary<string, IEnumerable<string>> webhooks =
                    _configuration.GetSection("Services:HomeAssistant:Webhooks")
                        .GetChildren()
                        .Where(section => !string.IsNullOrWhiteSpace(section.Key))
                        .ToDictionary(section => section.Key, section => section.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).AsEnumerable());
                                
                string phrase = string.Join(' ', phraseTokens);
                foreach (KeyValuePair<string,IEnumerable<string>> webhook in webhooks)
                {
                    foreach (string webhookPhrase in webhook.Value)
                    {
                        if (phrase.StartsWith(webhookPhrase, StringComparison.InvariantCultureIgnoreCase))
                        {
                            homeyPhrase.WebhookId = webhook.Key;
                            phrase = phrase.Remove(0, webhookPhrase.Length).Trim();
                            break;
                        }
                    }
                }          

                homeyPhrase.Content = phrase;
            }

            return Task.FromResult(homeyPhrase);
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
