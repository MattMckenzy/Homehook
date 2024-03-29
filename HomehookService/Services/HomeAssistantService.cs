﻿using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace Homehook.Services
{
    public class HomeAssistantService
    {
        private readonly IRestServiceCaller _homeAssistantCaller;

        public HomeAssistantService(AnonymousCaller<HomeassistantServiceAppProvider> homeAssistantCaller)
        {
            _homeAssistantCaller = homeAssistantCaller;
        }

        public async Task PostWebhook(string webhookId, string content) =>
            await _homeAssistantCaller.PostRequestAsync<string>($"api/webhook/{webhookId}", content: content);
    }
}