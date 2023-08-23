using HomeHook.Common.Services;

namespace HomeHook.Services
{
    /// <summary>
    /// Extends the rest service caller for a singleton-designed client token call.
    /// </summary>
    public sealed class AccessTokenCaller<T> : IRestServiceCaller where T : IRestServiceProvider
    {
        private T RestServiceProvider { get; }
        private HttpClient HttpClient { get; }
        private Dictionary<string, string> AccessTokens { get; } = new();

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="restServiceProvider">An instance of the service provider used for this caller.</param>
        /// <param name="httpClient">An instance of a configured HttpClient.</param>
        public AccessTokenCaller(T restServiceProvider, HttpClient httpClient)
        {
            RestServiceProvider = restServiceProvider;
            HttpClient = httpClient;
        }

        /// <summary>
        /// Builds and returns a base request message containing proper configuration and authentication.
        /// </summary>
        /// <returns>The base HttpRequestMessage.</returns>
        async Task<HttpRequestMessage> IRestServiceCaller.GetBaseRequestMessage(string? credential, Func<string, string, Task<string>>? accessTokenDelegate)
        {
            HttpRequestMessage returningHttpRequestMessage = new()
            {
                RequestUri = RestServiceProvider.GetServiceUri()
            };

            string headerValue = RestServiceProvider.GetScope() ?? throw new InvalidOperationException("Scope must be provided for the access token caller!");

            if (headerValue.Contains("{0}"))
            {
                if (credential != null && AccessTokens.TryGetValue(credential, out string? code) && code != null)
                    headerValue = string.Format(headerValue, code);
                else if (credential != null && RestServiceProvider.GetCredentials().TryGetValue(credential, out code) && code != null && accessTokenDelegate != null)
                {
                    AccessTokens[credential] = await accessTokenDelegate(credential, code);
                    headerValue = string.Format(headerValue, AccessTokens[credential]);
                }
                else
                    headerValue = string.Format(headerValue, RestServiceProvider.GetToken());
            }

            returningHttpRequestMessage.Headers.Add(RestServiceProvider.GetHeader() ?? throw new InvalidOperationException("Header must be provided for the access token caller!"), headerValue);

            return returningHttpRequestMessage;
        }

        public async Task RefreshAccessToken(string credential, Func<string, string, Task<string>> accessTokenDelegate)
        {
            if (RestServiceProvider.GetCredentials().TryGetValue(credential, out string? code) && code != null)
            {
                AccessTokens[credential] = await accessTokenDelegate(credential, code);
            }
        }

        /// <summary>
        /// Sends the given http request message with configurated request message.
        /// </summary>
        /// <returns>The response message.</returns>
        public async Task<HttpResponseMessage> SendRequest(HttpRequestMessage httpRequestMessage)
        {
            return await HttpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);
        }
    }
}