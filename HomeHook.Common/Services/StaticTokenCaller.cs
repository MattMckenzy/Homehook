namespace HomeHook.Common.Services
{
    /// <summary>
    /// Extends the rest service caller for a singleton-designed client token call.
    /// </summary>
    public sealed class StaticTokenCaller<T> : IRestServiceCaller where T : IRestServiceProvider
    {
        private readonly T _restServiceProvider;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="restServiceProvider">An instance of the service provider used for this caller.</param>
        /// <param name="httpClient">An instance of a configured HttpClient.</param>
        public StaticTokenCaller(T restServiceProvider, HttpClient httpClient)
        {
            _restServiceProvider = restServiceProvider;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Builds and returns a base request message containing proper configuration and authentication.
        /// </summary>
        /// <returns>The base HttpRequestMessage.</returns>
        Task<HttpRequestMessage> IRestServiceCaller.GetBaseRequestMessage(string? credential, Func<string, string, Task<string>>? accessTokenDelegate)
        {
            HttpRequestMessage returningHttpRequestMessage = new()
            {
                RequestUri = _restServiceProvider.GetServiceUri()
            };

            returningHttpRequestMessage.Headers.Add(_restServiceProvider.GetHeader() ?? throw new InvalidOperationException("Header must be provided for the static token caller!"), _restServiceProvider.GetToken());

            return Task.FromResult(returningHttpRequestMessage);
        }

        /// <summary>
        /// Sends the given http request message with configurated request message..
        /// </summary>
        /// <returns>The response message.</returns>
        public async Task<HttpResponseMessage> SendRequest(HttpRequestMessage httpRequestMessage)
        {
            return await _httpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);
        }
    }
}