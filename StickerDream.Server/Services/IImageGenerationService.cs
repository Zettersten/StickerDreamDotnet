namespace StickerDream.Server.Services;

/// <summary>
/// Service for generating images using Google Gemini Imagen API
/// </summary>
public interface IImageGenerationService
{
    Task<byte[]> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default);
}
