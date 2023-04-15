using WonkCast.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace WonkCast.Attributes
{
    [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyAttribute : Attribute, IAsyncActionFilter
    {
        public required string ApiKeyName { get; set; }
        public required string ApiKeysRoute { get; set; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!context.HttpContext.Request.Query.TryGetValue(ApiKeyName, out StringValues extractedApiKey))
            {
                context.Result = new ContentResult()
                {
                    StatusCode = 401,
                    Content = "No API access token for WonkCast was given."
                };
                return;
            }

            List<string> validApiKeys = new();
            foreach (HomehookToken homehookToken in context.HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection(ApiKeysRoute).Get<HomehookToken[]>() ?? Array.Empty<HomehookToken>())
                validApiKeys.Add(homehookToken.Secret);

            if (!validApiKeys.Any(validApiKey => validApiKey.Equals(extractedApiKey, StringComparison.InvariantCulture)))
            {
                context.Result = new ContentResult()
                {
                    StatusCode = 401,
                    Content = "The given API access token for WonkCast was invalid."
                };
                return;
            }

            await next();
        }
    }
}