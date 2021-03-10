using Microsoft.Extensions.Configuration;
using System;

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
        

        public string GetScope()
        {
            throw new NotImplementedException();
        }

        public Uri GetServiceUri() =>
            new(_configuration["Services:Jellyfin:ServiceUri"]);

        public string GetToken() =>
             _configuration["Services:Jellyfin:Token"];
    }
}