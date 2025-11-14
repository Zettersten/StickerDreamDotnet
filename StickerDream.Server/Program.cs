using StickerDream.Server.Components;
using StickerDream.Server.Services;
using StickerDream.Server.Middleware;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add custom services
builder.Services.AddHttpClient<IImageGenerationService, ImageGenerationService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
});
builder.Services.AddScoped<IPrinterService, PrinterService>();

// Configure Gemini API key
var geminiApiKey = builder.Configuration["GEMINI_API_KEY"] 
    ?? throw new InvalidOperationException("GEMINI_API_KEY configuration is required");
builder.Services.AddSingleton(new GeminiConfig { ApiKey = geminiApiKey });

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseMiddleware<RequestLoggingMiddleware>();

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Start printer watcher
using (var scope = app.Services.CreateScope())
{
    var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();
    await printerService.StartWatchingPrintersAsync();
}

app.Run();
