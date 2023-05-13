using HomeHook.Common.Services;
using HomeHook.Models;
using HomeHook.Models.Jellyfin;

namespace HomeHook.Services
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

        public async Task<JellyPhrase?> ParseJellyfinSimplePhrase(string simplePhrase)
        {
            string? user = _configuration["Services:Jellyfin:DefaultUser"];
            if (user == null) 
            {
                await _loggingService.LogError($"Configuration error", $"Please make sure a default Jellyfin user is set!");
                return null; 
            }

            string? device = _configuration["Services:Jellyfin:DefaultDevice"];
            if (device == null)
            {
                await _loggingService.LogError($"Configuration error", $"Please make sure a default Jellyfin device is set!");
                return null;
            }

            OrderType? orderType = Enum.TryParse(_configuration["Services:Jellyfin:DefaultOrder"] ?? string.Empty, out OrderType defaultOrderTypeResult) ? defaultOrderTypeResult : null;
            if (orderType == null)
            {
                await _loggingService.LogError($"Configuration error", $"Please make sure a default Jellyfin order type is set!");
                return null;
            }
            
            MediaType? mediaType = Enum.TryParse(_configuration["Services:Jellyfin:DefaultMediaType"] ?? string.Empty, out MediaType defaultMediaTypeResult) ? defaultMediaTypeResult : null;
            if (mediaType == null)
            {
                await _loggingService.LogError($"Configuration error", $"Please make sure a default Jellyfin media type is set!");
                return null;
            }

            IEnumerable<string> phraseTokens = ProcessWordMappings(simplePhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));;

            // Prepare recognized tokens and prepositions.
            Dictionary<string, IEnumerable<string>> orderTokens = new()
            {
                { "Watch", (_configuration["Services:Jellyfin:OrderTerms:Watch"] ?? "Watch,Play" ).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)   },
                { "Played", (_configuration["Services:Jellyfin:OrderTerms:Played"] ?? "Watched,Played").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Unplayed", (_configuration["Services:Jellyfin:OrderTerms:Unplayed"] ?? "Unwatched,Unplayed").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Continue", (_configuration["Services:Jellyfin:OrderTerms:Continue"] ?? "Continue,Resume").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Shuffle", (_configuration["Services:Jellyfin:OrderTerms:Shuffle"] ?? "Shuffle,Random,Any").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Ordered", (_configuration["Services:Jellyfin:OrderTerms:Ordered"] ?? "Ordered,Sequential,Order").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Oldest", (_configuration["Services:Jellyfin:OrderTerms:Oldest"] ?? "Oldest,First").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Newest", (_configuration["Services:Jellyfin:OrderTerms:Newest"] ?? "Last,Latest,Newest,Recent").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Shortest", (_configuration["Services:Jellyfin:OrderTerms:Shortest"] ?? "Shortest,Quickest,Fastest").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Longest", (_configuration["Services:Jellyfin:OrderTerms:Longest"] ?? "Longest,Slowest").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            };
            Dictionary<string, IEnumerable<string>> mediaTypeTokens = new()
            {
                { "Audio", (_configuration["Services:Jellyfin:MediaTypeTerms:Audio"] ?? "Song,Songs,Music,Track,Tracks,Audio").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Video", (_configuration["Services:Jellyfin:MediaTypeTerms:Video"] ?? "Video,Videos,Movies,Movie,Show,Shows,Episode,Episodes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Photo", (_configuration["Services:Jellyfin:MediaTypeTerms:Photo"] ?? "Photo,Photos,Pictures,Picture").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            };

            IEnumerable<string> userPrepositions = (_configuration["Services:Language:UserPrepositions"] ?? "as").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> devicePrepositions = (_configuration["Services:Language:DevicePrepositions"] ?? "on,to").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> pathPrepositions = (_configuration["Services:Language:PathPrepositions"] ?? "from,in,inside").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
            
            if (!string.IsNullOrWhiteSpace(spokenJellyDevice))
            {
                string? jellyDevice = _castService.DeviceServices.TryGetValue(spokenJellyDevice, out DeviceService? deviceService) ? deviceService.Device.Name : null;

                if (string.IsNullOrWhiteSpace(jellyDevice))
                    await _loggingService.LogWarning($"Spoken device is not listed.", $"Please add  \"{spokenJellyDevice}\" to device configuration.", simplePhrase);
                else
                {
                    await _loggingService.LogDebug($"Mapped spoken device token to {jellyDevice}.", string.Empty, jellyDevice);
                    device = jellyDevice;
                }
            }
            else
                await _loggingService.LogDebug($"No device extracted from the phrase spoken. Defaulting to device: \"{device}\".", string.Empty, simplePhrase);


            // Map spoken user.
            string spokenJellyUser = string.Empty;
            (spokenJellyUser, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.User, typedPhraseTokens);
            
            if (!string.IsNullOrWhiteSpace(spokenJellyUser))
            {
                string? jellyUser =
                    _configuration.GetSection("Services:HomeHook:UserMappings").Get<UserMappings[]>()?
                    .FirstOrDefault(userMappings => userMappings.Spoken?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)?
                        .Any(spokenMapping => spokenMapping.Equals(spokenJellyUser, StringComparison.InvariantCultureIgnoreCase)) ?? false)?
                    .Jellyfin;

                if (string.IsNullOrWhiteSpace(jellyUser))
                    await _loggingService.LogWarning($"Spoken user not mapped.", $"Please map \"{spokenJellyUser}\" to a Jellyfin user.", simplePhrase);
                else
                {
                    await _loggingService.LogDebug($"Mapped spoken user token to {jellyUser}.", string.Empty, simplePhrase);
                    user = jellyUser;
                }
            }
            else 
                await _loggingService.LogDebug($"No user extracted from the phrase spoken. Defaulting to user: \"{user}\".", string.Empty, simplePhrase);

            // Map spoken path term.
            string spokenPathTerm = string.Empty;
            (spokenPathTerm, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.Path, typedPhraseTokens);

            await _loggingService.LogDebug($"Mapped spoken path term to {spokenPathTerm}.", string.Empty, simplePhrase);

            phraseTokens = typedPhraseTokens.Select(tokenItem => tokenItem.token);

            // Map spoken media order.
            if (phraseTokens.Any() && orderTokens.SelectMany(tokens => tokens.Value).Any(orderToken => phraseTokens.First().Equals(orderToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyOrderType = orderTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.First(), StringComparison.InvariantCultureIgnoreCase))).Key;

                if (!string.IsNullOrWhiteSpace(spokenJellyOrderType) && Enum.TryParse(spokenJellyOrderType, out OrderType orderTypeResult))
                {
                    orderType = orderTypeResult;
                    await _loggingService.LogDebug($"Mapped spoken order token to {orderType}.", string.Empty, simplePhrase);
                }

                phraseTokens = phraseTokens.Skip(1).AsEnumerable();
            }

            // Map spoken media type.
            if (phraseTokens.Any() && mediaTypeTokens.SelectMany(tokens => tokens.Value).Any(mediaTypeToken => phraseTokens.Last().Equals(mediaTypeToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenJellyMediaType = mediaTypeTokens.First(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                if (!string.IsNullOrWhiteSpace(spokenJellyMediaType) && Enum.TryParse(spokenJellyMediaType, out MediaType mediaTypeResult))
                {
                    mediaType = mediaTypeResult;
                    await _loggingService.LogDebug($"Mapped spoken media type token to {mediaType}.", string.Empty, simplePhrase);
                }

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }

            // Remaining phrase tokens are search term.            
            string searchTerm = string.Join(' ', phraseTokens);

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                await _loggingService.LogError($"No search terms were extracted from the phrase.", $"Please make sure the phrase includes a search term!", simplePhrase);
                return null;
            }
            else
            {
                await _loggingService.LogDebug($"Mapped search term to {searchTerm}.", string.Empty, simplePhrase);
                return new JellyPhrase
                {
                    SearchTerm = searchTerm,
                    Device = device,
                    User = user,
                    PathTerm = spokenPathTerm,
                    MediaType = (MediaType)mediaType,
                    OrderType = (OrderType)orderType,
                    Cache = true,
                };
            }      
        }

        private static (string, IEnumerable<(PrepositionType type, string token)>) ExtractPrepositionTerm(PrepositionType type, IEnumerable<(PrepositionType type, string token)> typedPhraseTokens)
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

        private IEnumerable<string> ProcessWordMappings(string[] phraseTokens)
        {
            Dictionary<string, IEnumerable<string>?> wordMappings =
                _configuration.GetSection("Services:Language:WordMappings")
                    .GetChildren()
                    .Where(section => !string.IsNullOrWhiteSpace(section.Key))
                    .ToDictionary(section => section.Key, section => section.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).AsEnumerable());

            for (int index = 0; index < phraseTokens.Length; index++)            
                foreach (string wordMapping in wordMappings.Keys.Reverse())                
                    if (wordMappings[wordMapping]?.Any(mappedWord => mappedWord.Equals(phraseTokens[index], StringComparison.InvariantCultureIgnoreCase)) ?? false)
                        phraseTokens[index] = wordMapping;

            return phraseTokens;
        }
    }
}
