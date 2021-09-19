using Homehook.Hubs;
using Homehook.Middleware;
using Homehook.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Homehook
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Homehook", Version = "v1" });

                OpenApiSecurityScheme openApiSecurityScheme = new() 
                { 
                    Name = "apiKey", 
                    In = ParameterLocation.Query, 
                    Type = SecuritySchemeType.ApiKey,
                    Reference = new OpenApiReference
                    {
                        Id = "ApiKey",
                        Type = ReferenceType.SecurityScheme
                    }
                };
                c.AddSecurityDefinition("ApiKey", openApiSecurityScheme);
                c.AddSecurityRequirement(new() { { openApiSecurityScheme, Array.Empty<string>() } });
            });

            services.AddHttpClient<StaticTokenCaller<GotifyServiceAppProvider>>();
            services.AddSingleton<GotifyServiceAppProvider>();
            services.AddSingleton<GotifyService>();
            services.AddHttpClient<AccessTokenCaller<JellyfinServiceAppProvider>>();
            services.AddSingleton<JellyfinServiceAppProvider>();
            services.AddHttpClient<AccessTokenCaller<JellyfinAuthenticationServiceAppProvider>>();
            services.AddSingleton<JellyfinAuthenticationServiceAppProvider>();
            services.AddSingleton<JellyfinService>();
            services.AddHttpClient<AnonymousCaller<HomeassistantServiceAppProvider>>();
            services.AddSingleton<HomeassistantServiceAppProvider>();
            services.AddSingleton<HomeAssistantService>();
            services.AddSingleton<LanguageService>();
            services.AddSingleton(typeof(LoggingService<>));

            services.AddSingleton<CastService>();
            services.AddHostedService(sp => sp.GetRequiredService<CastService>());

            services.AddAuthentication(options =>
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
                        if (path.StartsWithSegments("/receiverhub") &&
                            context.Request.Headers.TryGetValue("Authorization", out StringValues accessToken) &&
                            !string.IsNullOrEmpty(accessToken.ToString()) &&
                            string.Equals(Configuration["Services:HomehookApp:Token"], accessToken.ToString().Replace("Bearer ", ""), StringComparison.Ordinal))
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

            services.AddSignalR()
                .AddNewtonsoftJsonProtocol();

            JsonConvert.DefaultSettings = () => new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<ExceptionHandlerMiddleware>();

            app.UseSwagger(options =>
            {
                options.PreSerializeFilters.Add((swagger, httpReq) =>
                {
                    //Clear servers -element in swagger.json because it got the wrong port when hosted behind reverse proxy
                    swagger.Servers.Clear();
                });
            });
            app.UseSwaggerUI(options => 
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Homehook v1");
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<ReceiverHub>("/receiverhub");
                endpoints.MapSwagger();
            });
        }
    }
}
