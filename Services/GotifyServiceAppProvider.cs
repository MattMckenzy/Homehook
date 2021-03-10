using Microsoft.Extensions.Configuration;
using System;

namespace Homehook.Services
{
    public class GotifyServiceAppProvider : IRestServiceProvider
    {
        private readonly IConfiguration _configuration;

        public GotifyServiceAppProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetHeader() =>
            _configuration["Services:Gotify:Header"];
        

        public string GetScope()
        {
            throw new NotImplementedException();
        }

        public Uri GetServiceUri() =>
            new(_configuration["Services:Gotify:ServiceUri"]);

        public string GetToken() =>
             _configuration["Services:Gotify:Token"];
    }
}