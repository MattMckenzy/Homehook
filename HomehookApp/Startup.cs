using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace HomehookApp
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();

            JsonConvert.DefaultSettings = () =>
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.Use(async (context, next) =>
            {
                if (!IsIntranet(context.Request.Host.Host))
                {
                    // Forbidden http status code
                    context.Response.StatusCode = 403;
                    return;
                }

                await next.Invoke();
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }

        private static bool IsIntranet(string host)
        {
            return Regex.IsMatch(host, "(localhost)|(127\\.0\\.0\\.1)|(^192\\.168\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])$)|" +
                "(^172\\.([1][6-9]|[2][0-9]|[3][0-1])\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])$)|" +
                "(^10\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])\\.([0-9]|[0-9][0-9]|[0-1][0-9][0-9]|2[0-5][0-5])$)");
        }
    }
}
