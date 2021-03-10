using System.Net.Http;
using System.Threading.Tasks;

namespace Homehook.Services
{
    /// <summary>
    /// Extends the rest service caller for a singleton-designed anonymous call.
    /// </summary>
    public sealed class AnonymousCaller<T> : IRestServiceCaller where T : IRestServiceProvider
    {
        private readonly T _restServiceProvider;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="restServiceProvider">An instance of the service provider used for this caller.</param>
        /// <param name="httpClient">An instance of a configured HttpClient.</param>
        public AnonymousCaller(T restServiceProvider, HttpClient httpClient)
        {
            _restServiceProvider = restServiceProvider;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Builds and returns a base request message containing proper configuration.
        /// </summary>
        /// <returns>The base HttpRequestMessage.</returns>
        Task<HttpRequestMessage> IRestServiceCaller.GetBaseRequestMessage()
        {
            HttpRequestMessage returningHttpRequestMessage = new()
            {
                RequestUri = _restServiceProvider.GetServiceUri()
            };

            return Task.FromResult(returningHttpRequestMessage);
        }

        /// <summary>
        /// Sends the given http request message.
        /// </summary>
        /// <returns>The response message.</returns>
        public async Task<HttpResponseMessage> SendRequest(HttpRequestMessage httpRequestMessage)
        {
            return await _httpClient.SendAsync(httpRequestMessage).ConfigureAwait(false);
        }
    }
}