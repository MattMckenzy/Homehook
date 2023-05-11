using Microsoft.Extensions.Configuration;

namespace HomeHook.Common.Services
{
    public class GotifyServiceAppProvider : IRestServiceProvider
    {
        private readonly IConfiguration Configuration;

        public GotifyServiceAppProvider(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public string? GetHeader() =>
            Configuration["Services:Gotify:Header"];


        public string GetScope() => throw new NotImplementedException();

        public Uri? GetServiceUri() =>
            Configuration["Services:Gotify:ServiceUri"] == null ? null : new(Configuration["Services:Gotify:ServiceUri"]!);

        public string? GetToken() =>
             Configuration["Services:Gotify:AccessToken"];

        public Dictionary<string, string?> GetCredentials() => throw new NotImplementedException();
    }
}