using Microsoft.Extensions.Configuration;
using System;

namespace Homehook.Services
{
    public class HomeassistantServiceAppProvider : IRestServiceProvider
    {
        private readonly IConfiguration _configuration;
        
        public HomeassistantServiceAppProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetHeader() =>
            _configuration["Services:Homeassistant:Header"];


        public string GetScope()
        {
            throw new NotImplementedException();
        }

        public Uri GetServiceUri() =>
            new(_configuration["Services:Homeassistant:ServiceUri"]);

        public string GetToken() =>
            _configuration["Services:Homeassistant:AccessToken"];
    }
}