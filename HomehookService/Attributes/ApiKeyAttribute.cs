using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Homehook.Attributes
{
    [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyAttribute : Attribute, IAsyncActionFilter
    {
        public string ApiKeyName { get; set; }
        public string[] ApiKeyRoutes { get; set; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!context.HttpContext.Request.Query.TryGetValue(ApiKeyName, out var extractedApiKey))
            {
                context.Result = new ContentResult()
                {
                    StatusCode = 401,
                    Content = "API key was not provided."
                };
                return;
            }

            List<string> validApiKeys = new();
            foreach (string apiKeyRoute in ApiKeyRoutes)
                validApiKeys.Add(context.HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue<string>(apiKeyRoute));

             if (!validApiKeys.Any(validApiKey => validApiKey.Equals(extractedApiKey, StringComparison.InvariantCulture)))
            {
                context.Result = new ContentResult()
                {
                    StatusCode = 401,
                    Content = "API key is not valid."
                };
                return;
            }

            await next();
        }
    }
}