using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StickerDream.Server.Components;
using StickerDream.Server.Middleware;
using StickerDream.Server.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Gemini API - supports both environment variable and configuration section
builder.Services.AddOptions<GeminiConfig>()
    .BindConfiguration(GeminiConfig.SectionName)
    .PostConfigure<IConfiguration>((config, configuration) =>
    {
        // Support environment variable override
        var envApiKey = configuration["GEMINI_API_KEY"];
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            config = config with { ApiKey = envApiKey };
        }
    })
    .Validate(config => !string.IsNullOrWhiteSpace(config.ApiKey), "GEMINI_API_KEY configuration is required")
    .ValidateOnStart();

// Add HTTP client with resilience
builder.Services.AddHttpClient<IImageGenerationService, ImageGenerationService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// Add services
builder.Services.AddScoped<IPrinterService, PrinterService>();
builder.Services.AddHostedService<PrinterWatcherService>();

// Add output caching
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromMinutes(5)));
});

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("generate", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseRateLimiter();
app.UseOutputCache();

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
