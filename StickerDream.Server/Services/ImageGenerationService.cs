using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickerDream.Server.Services;

/// <summary>
/// Generates images using Google Gemini Imagen API
/// </summary>
public class ImageGenerationService(HttpClient httpClient, GeminiConfig config, ILogger<ImageGenerationService> logger) : IImageGenerationService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly GeminiConfig _config = config;
    private readonly ILogger<ImageGenerationService> _logger = logger;

    public async Task<byte[]> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating image for prompt: {Prompt}", prompt);

        var requestBody = new
        {
            prompt = $"A black and white kids coloring page.\n<image-description>\n{prompt}\n</image-description>\n{prompt}",
            config = new
            {
                numberOfImages = 1,
                aspectRatio = "9:16"
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/imagen-4.0-generate-001:generateImages?key={_config.ApiKey}")
        {
            Content = JsonContent.Create(requestBody)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ImageGenerationResponse>(cancellationToken: cancellationToken);
        
        if (result?.GeneratedImages == null || result.GeneratedImages.Count == 0)
        {
            throw new InvalidOperationException("No images were generated");
        }

        var imageBytes = result.GeneratedImages[0].ImageBytes;
        if (string.IsNullOrEmpty(imageBytes))
        {
            throw new InvalidOperationException("Image bytes are empty");
        }

        return Convert.FromBase64String(imageBytes);
    }

    private class ImageGenerationResponse
    {
        [JsonPropertyName("generatedImages")]
        public List<GeneratedImage> GeneratedImages { get; set; } = [];
    }

    private class GeneratedImage
    {
        [JsonPropertyName("imageBytes")]
        public string ImageBytes { get; set; } = string.Empty;
    }
}
