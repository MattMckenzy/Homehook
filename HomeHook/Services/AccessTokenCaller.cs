using HomeHook.Common.Services;

namespace HomeHook.Services
{
    /// <summary>
    /// Extends the rest service caller for a singleton-designed client token call.
    /// </summary>
    public sealed class AccessTokenCaller<T> : IRestServiceCaller where T : IRestServiceProvider
    {
        private readonly T _restServiceProvider;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, string> accessTokens = new();

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="restServiceProvider">An instance of the service provider used for this caller.</param>
        /// <param name="httpClient">An instance of a configured HttpClient.</param>
        public AccessTokenCaller(T restServiceProvider, HttpClient httpClient)
        {
            _restServiceProvider = restServiceProvider;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Builds and returns a base request message containing proper configuration and authentication.
        /// </summary>
        /// <returns>The base HttpRequestMessage.</returns>
        async Task<HttpRequestMessage> IRestServiceCaller.GetBaseRequestMessage(string? credential, Func<string, string, Task<string>>? accessTokenDelegate)
        {
            HttpRequestMessage returningHttpRequestMessage = new()
            {
                RequestUri = _restServiceProvider.GetServiceUri()
            };

            string headerValue = _restServiceProvider.GetScope() ?? throw new InvalidOperationException("Scope must be provided for the access token caller!");

            if (headerValue.Contains("{0}"))
            {
                if (credential != null && accessTokens.TryGetValue(credential, out string? code) && code != null && accessTokenDelegate == null)
                    headerValue = string.Format(headerValue, code);
                else if (credential != null && _restServiceProvider.GetCredentials().TryGetValue(credential, out code) && code != null && accessTokenDelegate != null)
                {
                    accessTokens[credential] = await accessTokenDelegate(credential, code);
                    headerValue = string.Format(headerValue, accessTokens[credential]);
                }
                else
                    headerValue = string.Format(headerValue, _restServiceProvider.GetToken());
            }

            returningHttpRequestMessage.Headers.Add(_restServiceProvider.GetHeader() ?? throw new InvalidOperationException("Header must be provided for the access token caller!"), headerValue);

            return returningHttpRequestMessage;
        }

        /// <summary>
        /// Sends the given http request message with configurated request message.
        /// </summary>
        /// <returns>The response message.</returns>
        public async Task<HttpResponseMessage> SendRequest(HttpRequestMessage httpRequestMessage)
        {
            return await _httpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);
        }
    }
}