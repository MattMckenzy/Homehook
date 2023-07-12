using ElectronNET.API;
using ElectronNET.API.Entities;
using HomeDash.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseElectron(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

builder.Services.AddElectron();

WebApplication webApplication = builder.Build();

// Configure the HTTP request pipeline.
if (!webApplication.Environment.IsDevelopment())
{
    webApplication.UseExceptionHandler("/Error");
}

webApplication.UseStaticFiles();

webApplication.UseRouting();

webApplication.MapBlazorHub();
webApplication.MapFallbackToPage("/_Host");

_ = Task.Run(async () => await Electron.WindowManager.CreateWindowAsync(
    new BrowserWindowOptions
    {
        Transparent = true,
        Frame = false,
        Fullscreen = true
    }));

webApplication.Run();
