using HomeHook.Common.Services;
using HomeHook.Models;
using HomeHook.Models.Language;

namespace HomeHook.Services
{
    public class LanguageService
    {
        private IConfiguration Configuration { get; }
        private CastService CastService { get; }
        private LoggingService<LanguageService> LoggingService { get; }

        public LanguageService(IConfiguration configuration, CastService castService, LoggingService<LanguageService> loggingService)
        {
            Configuration = configuration;
            CastService = castService;
            LoggingService = loggingService;
        }

        public async Task<LanguagePhrase?> ParseSimplePhrase(string simplePhrase)
        {
            UserMappings? user = Configuration.GetSection("Services:Language:DefaultUser").Get<UserMappings>();
            if (user == null) 
            {
                await LoggingService.LogError($"Configuration error", $"Please make sure a default user is set!");
                return null; 
            }

            string? device = Configuration["Services:Language:DefaultDevice"];
            if (device == null)
            {
                await LoggingService.LogError($"Configuration error", $"Please make sure a default device is set!");
                return null;
            }

            OrderType? orderType = Enum.TryParse(Configuration["Services:Language:DefaultOrder"] ?? string.Empty, out OrderType defaultOrderTypeResult) ? defaultOrderTypeResult : null;
            if (orderType == null)
            {
                await LoggingService.LogError($"Configuration error", $"Please make sure a default order type is set!");
                return null;
            }
            
            MediaType? mediaType = Enum.TryParse(Configuration["Services:Language:DefaultMediaType"] ?? string.Empty, out MediaType defaultMediaTypeResult) ? defaultMediaTypeResult : null;
            if (mediaType == null)
            {
                await LoggingService.LogError($"Configuration error", $"Please make sure a default media type is set!");
                return null;
            }

            PlaybackMethod? playbackMethod = Enum.TryParse(Configuration["Services:Language:DefaultPlaybackMethod"] ?? string.Empty, out PlaybackMethod defaultPlaybackMethodResult) ? defaultPlaybackMethodResult : null;
            if (playbackMethod == null)
            {
                await LoggingService.LogError($"Configuration error", $"Please make sure a default playback method is set!");
                return null;
            }

            Source? source = Enum.TryParse(Configuration["Services:Language:DefaultSource"] ?? string.Empty, out Source defaultSourceResult) ? defaultSourceResult : null;
            if (source == null)
            {
                await LoggingService.LogError($"Configuration error", $"Please make sure a default source is set!");
                return null;
            }

            IEnumerable<string> phraseTokens = ProcessWordMappings(simplePhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));;

            // Prepare recognized tokens and prepositions.
            Dictionary<string, IEnumerable<string>> orderTokens = new()
            {
                { "Watch", (Configuration["Services:Language:OrderTerms:Watch"] ?? "Watch,Play" ).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)   },
                { "Played", (Configuration["Services:Language:OrderTerms:Played"] ?? "Watched,Played").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Unplayed", (Configuration["Services:Language:OrderTerms:Unplayed"] ?? "Unwatched,Unplayed").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Continue", (Configuration["Services:Language:OrderTerms:Continue"] ?? "Continue,Resume").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Shuffle", (Configuration["Services:Language:OrderTerms:Shuffle"] ?? "Shuffle,Random,Any").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Ordered", (Configuration["Services:Language:OrderTerms:Ordered"] ?? "Ordered,Sequential,Order").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Oldest", (Configuration["Services:Language:OrderTerms:Oldest"] ?? "Oldest,First").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Newest", (Configuration["Services:Language:OrderTerms:Newest"] ?? "Last,Latest,Newest,Recent").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Shortest", (Configuration["Services:Language:OrderTerms:Shortest"] ?? "Shortest,Quickest,Fastest").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Longest", (Configuration["Services:Language:OrderTerms:Longest"] ?? "Longest,Slowest").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            };

            Dictionary<string, IEnumerable<string>> mediaTypeTokens = new()
            {
                { "Audio", (Configuration["Services:Language:MediaTypeTerms:Audio"] ?? "Song,Songs,Music,Track,Tracks,Audio").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Video", (Configuration["Services:Language:MediaTypeTerms:Video"] ?? "Video,Videos,Movies,Movie,Show,Shows,Episode,Episodes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Photo", (Configuration["Services:Language:MediaTypeTerms:Photo"] ?? "Photo,Photos,Pictures,Picture").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            };

            Dictionary<string, IEnumerable<string>> sourceTokens = new()
            {
                { "Jellyfin", (Configuration["Services:Language:SourceTerms:Jellyfin"] ?? "Jellyfin,Jelly,Local").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "YouTube", (Configuration["Services:Language:SourceTerms:YouTube"] ?? "YouTube,Tube,Online").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) }
            };

            Dictionary<string, IEnumerable<string>> playbackMethodTokens = new()
            {
                { "Direct", (Configuration["Services:Language:PlaybackMethodTerms:Direct"] ?? "Direct,Stream,Streamed").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                { "Cached", (Configuration["Services:Language:PlaybackMethodTerms:Cached"] ?? "Cached,Saved,Save,Cache").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) }
            };

            IEnumerable<string> userPrepositions = (Configuration["Services:Language:UserPrepositions"] ?? "as").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> devicePrepositions = (Configuration["Services:Language:DevicePrepositions"] ?? "on,to").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> pathPrepositions = (Configuration["Services:Language:PathPrepositions"] ?? "from,in,inside").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            IEnumerable<(PrepositionType type, string preposition)> prepositions = 
                userPrepositions.Select(preposition => (PrepositionType.User, preposition))
                .Union(devicePrepositions.Select(preposition => (PrepositionType.Device, preposition)))
                .Union(pathPrepositions.Select(preposition => (PrepositionType.Path, preposition)));

            IEnumerable<(PrepositionType type, string token)> typedPhraseTokens = 
                phraseTokens.Select(token=> (token, prepositions.FirstOrDefault(prepositionItem => prepositionItem.preposition.Equals(token, StringComparison.InvariantCultureIgnoreCase)).type))
                .Select(prepositionItem => (prepositionItem.type, prepositionItem.token));

            // Map spoken device.
            string spokenDevice = string.Empty;
            (spokenDevice, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.Device, typedPhraseTokens);
            
            if (!string.IsNullOrWhiteSpace(spokenDevice))
            {
                string? availableDevice = CastService.DeviceServices.TryGetValue(spokenDevice, out DeviceService? deviceService) ? deviceService.Device.Name : null;

                if (string.IsNullOrWhiteSpace(availableDevice))
                    await LoggingService.LogWarning($"Spoken device is not listed.", $"Please add  \"{spokenDevice}\" to device configuration.", simplePhrase);
                else
                {
                    await LoggingService.LogDebug($"Mapped spoken device token to {availableDevice}.", string.Empty, availableDevice);
                    device = availableDevice;
                }
            }
            else
                await LoggingService.LogDebug($"No device extracted from the phrase spoken. Defaulting to device: \"{device}\".", string.Empty, simplePhrase);


            // Map spoken user.
            string spokenUser = string.Empty;
            (spokenUser, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.User, typedPhraseTokens);
            
            if (!string.IsNullOrWhiteSpace(spokenUser))
            {
                UserMappings? availableUser =
                    Configuration.GetSection("Services:HomeHook:UserMappings").Get<UserMappings[]>()?
                    .FirstOrDefault(userMappings => userMappings.Spoken?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)?
                        .Any(spokenMapping => spokenMapping.Equals(spokenUser, StringComparison.InvariantCultureIgnoreCase)) ?? false);

                if (availableUser == null)
                    await LoggingService.LogWarning($"Spoken user not mapped.", $"Please map \"{spokenUser}\" to a user.", simplePhrase);
                else
                {
                    await LoggingService.LogDebug($"Mapped spoken user token to {availableUser}.", string.Empty, simplePhrase);
                    user = availableUser;
                }
            }
            else 
                await LoggingService.LogDebug($"No user extracted from the phrase spoken. Defaulting to user: \"{user}\".", string.Empty, simplePhrase);

            // Map spoken path term.
            string spokenPathTerm = string.Empty;
            (spokenPathTerm, typedPhraseTokens) = ExtractPrepositionTerm(PrepositionType.Path, typedPhraseTokens);

            await LoggingService.LogDebug($"Mapped spoken path term to {spokenPathTerm}.", string.Empty, simplePhrase);

            phraseTokens = typedPhraseTokens.Select(tokenItem => tokenItem.token);

            // Map spoken media order.
            if (phraseTokens.Any() && orderTokens.SelectMany(tokens => tokens.Value).Any(orderToken => phraseTokens.First().Equals(orderToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenOrderType = orderTokens.FirstOrDefault(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.First(), StringComparison.InvariantCultureIgnoreCase))).Key;

                if (!string.IsNullOrWhiteSpace(spokenOrderType) && Enum.TryParse(spokenOrderType, out OrderType orderTypeResult))
                {
                    orderType = orderTypeResult;
                    await LoggingService.LogDebug($"Mapped spoken order token to {orderType}.", string.Empty, simplePhrase);
                }

                phraseTokens = phraseTokens.Skip(1).AsEnumerable();
            }

            // Map spoken media type.
            if (phraseTokens.Any() && mediaTypeTokens.SelectMany(tokens => tokens.Value).Any(mediaTypeToken => phraseTokens.Last().Equals(mediaTypeToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenMediaType = mediaTypeTokens.First(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                if (!string.IsNullOrWhiteSpace(spokenMediaType) && Enum.TryParse(spokenMediaType, out MediaType mediaTypeResult))
                {
                    mediaType = mediaTypeResult;
                    await LoggingService.LogDebug($"Mapped spoken media type token to {mediaType}.", string.Empty, simplePhrase);
                }

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }

            // Map spoken source.
            if (phraseTokens.Any() && sourceTokens.SelectMany(tokens => tokens.Value).Any(sourceToken => phraseTokens.Last().Equals(sourceToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenSource = sourceTokens.First(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                if (!string.IsNullOrWhiteSpace(spokenSource) && Enum.TryParse(spokenSource, out Source sourceResult))
                {
                    source = sourceResult;
                    await LoggingService.LogDebug($"Mapped source token to {source}.", string.Empty, simplePhrase);
                }

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }


            // Map spoken playback method.
            if (phraseTokens.Any() && playbackMethodTokens.SelectMany(tokens => tokens.Value).Any(playbackMethodToken => phraseTokens.Last().Equals(playbackMethodToken, StringComparison.InvariantCultureIgnoreCase)))
            {
                string spokenPlaybackMethod = playbackMethodTokens.First(keyValuePair => keyValuePair.Value.Any(value => value.Equals(phraseTokens.Last(), StringComparison.InvariantCultureIgnoreCase))).Key;

                if (!string.IsNullOrWhiteSpace(spokenPlaybackMethod) && Enum.TryParse(spokenPlaybackMethod, out PlaybackMethod playbackMethodResult))
                {
                    playbackMethod = playbackMethodResult;
                    await LoggingService.LogDebug($"Mapped spoken playback method token to {playbackMethod}.", string.Empty, simplePhrase);
                }

                phraseTokens = phraseTokens.SkipLast(1).AsEnumerable();
            }

            // Remaining phrase tokens are search term.            
            string searchTerm = string.Join(' ', phraseTokens);

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                await LoggingService.LogError($"No search terms were extracted from the phrase.", $"Please make sure the phrase includes a search term!", simplePhrase);
                return null;
            }
            else
            {
                await LoggingService.LogDebug($"Mapped search term to {searchTerm}.", string.Empty, simplePhrase);

                string sourceUser = string.Empty;

                switch (source)
                {
                    case Source.Jellyfin:
                        if (string.IsNullOrWhiteSpace(user.Jellyfin))
                        {
                            await LoggingService.LogError($"No Jellyfin user was found.", $"Please make sure the settings includes a default Jellyfin user!", simplePhrase);
                            return null;
                        }
                        sourceUser = user.Jellyfin; 
                        break;
                    case Source.YouTube:
                        sourceUser = user.YouTube ?? string.Empty; 
                        break;
                }

                return new LanguagePhrase
                {
                    SearchTerm = searchTerm,
                    Device = device,
                    User = sourceUser,
                    PathTerm = spokenPathTerm,
                    PlaybackMethod = (PlaybackMethod)playbackMethod,
                    Source = (Source)source,
                    MediaType = (MediaType)mediaType,
                    OrderType = (OrderType)orderType
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
                Configuration.GetSection("Services:Language:WordMappings")
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
