using Microsoft.AspNetCore.Mvc;
using StickerDream.Server.Services;

namespace StickerDream.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenerateController(
    IImageGenerationService imageService,
    IPrinterService printerService,
    ILogger<GenerateController> logger) : ControllerBase
{
    private readonly IImageGenerationService _imageService = imageService;
    private readonly IPrinterService _printerService = printerService;
    private readonly ILogger<GenerateController> _logger = logger;

    [HttpPost]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Received image generation request. RequestId: {RequestId}, ClientIp: {ClientIp}, PromptLength: {PromptLength}, HasPrompt: {HasPrompt}",
            requestId, clientIp, request.Prompt?.Length ?? 0, !string.IsNullOrWhiteSpace(request.Prompt));

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            _logger.LogWarning(
                "Invalid request - prompt is empty. RequestId: {RequestId}, ClientIp: {ClientIp}",
                requestId, clientIp);
            return BadRequest(new { error = "Prompt is required" });
        }

        _logger.LogInformation(
            "Processing image generation request. RequestId: {RequestId}, Prompt: {Prompt}",
            requestId, request.Prompt);

        try
        {
            // Generate image
            var imageBytes = await _imageService.GenerateImageAsync(request.Prompt, cancellationToken);

            _logger.LogInformation(
                "Image generated successfully. RequestId: {RequestId}, ImageSizeBytes: {ImageSize}",
                requestId, imageBytes.Length);

            // Print image (non-blocking - continue even if printing fails)
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "Starting print job in background. RequestId: {RequestId}, ImageSizeBytes: {ImageSize}",
                        requestId, imageBytes.Length);

                    await _printerService.PrintImageAsync(imageBytes, new PrintOptions(FitToPage: true), cancellationToken);
                    
                    _logger.LogInformation(
                        "Background print job completed successfully. RequestId: {RequestId}",
                        requestId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background print job failed, but image was generated. RequestId: {RequestId}",
                        requestId);
                }
            }, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Image generation request completed successfully. RequestId: {RequestId}, TotalDurationMs: {DurationMs}, ImageSizeBytes: {ImageSize}",
                requestId, stopwatch.ElapsedMilliseconds, imageBytes.Length);

            // Return image
            return File(imageBytes, "image/png");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Image generation request failed. RequestId: {RequestId}, Prompt: {Prompt}, DurationMs: {DurationMs}",
                requestId, request.Prompt, stopwatch.ElapsedMilliseconds);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record GenerateRequest(string Prompt);
