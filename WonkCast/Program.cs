using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using WonkCast;
using WonkCast.Common.Services;
using WonkCast.Middleware;
using WonkCast.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol();

builder.Services.AddAuthentication(options =>
{
    // Identity made Cookie authentication the default.
    // However, we want JWT Bearer Auth to be the default.
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v2", new OpenApiInfo { Title = "WonkCast", Version = "v2" });

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

builder.Services.AddHttpClient<StaticTokenCaller<GotifyServiceAppProvider>>();
builder.Services.AddHttpClient<AccessTokenCaller<JellyfinServiceAppProvider>>();
builder.Services.AddHttpClient<AccessTokenCaller<JellyfinAuthenticationServiceAppProvider>>();

builder.Services.AddSingleton<GotifyServiceAppProvider>();
builder.Services.AddSingleton<JellyfinServiceAppProvider>();
builder.Services.AddSingleton<JellyfinAuthenticationServiceAppProvider>();

builder.Services.AddSingleton<GotifyService>();
builder.Services.AddSingleton<JellyfinService>();

builder.Services.AddSingleton<LanguageService>();

builder.Services.AddSingleton(typeof(LoggingService<>));

builder.Services.AddSingleton<CastService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CastService>());

WebApplication app = builder.Build();

app.UseSwagger(options =>
{
    options.PreSerializeFilters.Add((swagger, httpReq) =>
    {
        //Clear servers -element in swagger.json because it got the wrong port when hosted behind reverse proxy
        swagger.Servers.Clear();
    });
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseMiddleware<ExceptionHandlerMiddleware>();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "WonkCast v2");
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapSwagger();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();