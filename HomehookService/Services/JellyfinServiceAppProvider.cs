using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Homehook.Services
{
    public class JellyfinServiceAppProvider : IRestServiceProvider
    {
        private readonly IConfiguration _configuration;
        
        public JellyfinServiceAppProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetHeader() =>
            _configuration["Services:Jellyfin:Header"];
        
        public string GetScope() =>
            _configuration["Services:Jellyfin:HeaderValue"];

        public Uri GetServiceUri() =>
            new(_configuration?["Services:Jellyfin:ServiceUri"]);

        public string GetToken() =>
            _configuration["Services:Jellyfin:AccessToken"];

        public Dictionary<string, string> GetCredentials() =>
            _configuration.GetSection("Services:Jellyfin:Credentials").GetChildren().ToDictionary(configurationSection => configurationSection.Key, configurationSection => configurationSection.Value);

    }
}