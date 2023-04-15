using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Primitives;
using System.Security.Claims;
using WonkCast.DeviceService;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PlayerService>();
builder.Services.AddSingleton<ScriptsProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScriptsProcessor>());

builder.Services.AddAuthentication(options =>
{
    // Identity made Cookie authentication the default.
    // However, we want JWT Bearer Auth to be the default.
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            PathString path = context.HttpContext.Request.Path;
            if (path.StartsWithSegments("/devicehub") &&
                context.Request.Headers.TryGetValue("Authorization", out StringValues accessToken) &&
                !string.IsNullOrEmpty(accessToken.ToString()) &&
                string.Equals(builder.Configuration["Services:HomehookApp:Token"], accessToken.ToString().Replace("Bearer ", ""), StringComparison.Ordinal))
            {
                IEnumerable<Claim> claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, "HomeHookApp")
                };

                context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Jwt"));
                context.Success();
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<DeviceHub>("/devicehub");

app.UseHttpsRedirection();