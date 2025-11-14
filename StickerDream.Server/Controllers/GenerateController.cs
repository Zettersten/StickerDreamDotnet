using Microsoft.AspNetCore.Mvc;
using StickerDream.Server.Services;

namespace StickerDream.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenerateController : ControllerBase
{
    private readonly IImageGenerationService _imageService;
    private readonly IPrinterService _printerService;
    private readonly ILogger<GenerateController> _logger;

    public GenerateController(
        IImageGenerationService imageService,
        IPrinterService printerService,
        ILogger<GenerateController> logger)
    {
        _imageService = imageService;
        _printerService = printerService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest(new { error = "Prompt is required" });
        }

        try
        {
            // Generate image
            var imageBytes = await _imageService.GenerateImageAsync(request.Prompt, cancellationToken);

            // Print image (non-blocking - continue even if printing fails)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _printerService.PrintImageAsync(imageBytes, new PrintOptions { FitToPage = true }, cancellationToken);
                    _logger.LogInformation("Image printed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Printing failed, but image was generated");
                }
            }, cancellationToken);

            // Return image
            return File(imageBytes, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record GenerateRequest(string Prompt);
