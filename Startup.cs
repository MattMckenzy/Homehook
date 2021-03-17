using Homehook.Middleware;
using Homehook.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;

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
            services.AddHttpClient<StaticTokenCaller<JellyfinServiceAppProvider>>();
            services.AddSingleton<JellyfinServiceAppProvider>();
            services.AddHttpClient<AccessTokenCaller<JellyfinAuthenticationServiceAppProvider>>();
            services.AddSingleton<JellyfinAuthenticationServiceAppProvider>();
            services.AddSingleton<JellyfinService>();
            services.AddHttpClient<StaticTokenCaller<HomeassistantServiceAppProvider>>();
            services.AddSingleton<HomeassistantServiceAppProvider>();
            services.AddSingleton<HomeAssistantService>();
            services.AddSingleton<LanguageService>();
            services.AddSingleton(typeof(LoggingService<>));
            services.AddSingleton<CastService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<ExceptionHandlerMiddleware>();

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Homehook v1"));

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
