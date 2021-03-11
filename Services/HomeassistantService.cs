using System.Threading.Tasks;

namespace Homehook.Services
{
    public class HomeAssistantService
    {
        private readonly IRestServiceCaller _homeAssistantCaller;

        public HomeAssistantService(StaticTokenCaller<HomeassistantServiceAppProvider> homeAssistantCaller)
        {
            _homeAssistantCaller = homeAssistantCaller;
        }

        public async Task PlayMedia(string content) =>
            await _homeAssistantCaller.PostRequestAsync<string>($"api/services/media_player/play_media", content: content);
        
    }
}