using HomeHook.Common.Services;

namespace HomeHook.Services
{
    public class JellyfinAuthenticationServiceAppProvider : IRestServiceProvider
    {
        private readonly IConfiguration _configuration;
        
        public JellyfinAuthenticationServiceAppProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? GetHeader() =>
            _configuration["Services:Jellyfin:Header"];        

        public string? GetScope() =>
            _configuration["Services:Jellyfin:AuthHeaderValue"];

        public Uri? GetServiceUri() =>
            _configuration["Services:Jellyfin:ServiceUri"] == null ? null : new(_configuration["Services:Jellyfin:ServiceUri"]!);

        public string? GetToken() => throw new NotImplementedException();

        public Dictionary<string, string?> GetCredentials() => throw new NotImplementedException();
    }
}