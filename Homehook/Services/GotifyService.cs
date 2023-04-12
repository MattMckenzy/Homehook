using Homehook.Models;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Homehook.Services
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
