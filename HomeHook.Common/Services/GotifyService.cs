using Newtonsoft.Json;
using HomeHook.Common.Models;

namespace HomeHook.Common.Services
{
    public class GotifyService
    {
        private readonly IRestServiceCaller _gotifyAppCaller;

        public GotifyService(StaticTokenCaller<GotifyServiceAppProvider> gotifyAppCaller)
        {
            _gotifyAppCaller = gotifyAppCaller;
        }

        public async Task PushMessage(GotifyMessage gotifyMessage)
        {
            await _gotifyAppCaller.PostRequestAsync<string>("message", content: JsonConvert.SerializeObject(gotifyMessage));
        }
    }
}
