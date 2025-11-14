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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "Starting image generation request. RequestId: {RequestId}, Prompt: {Prompt}, PromptLength: {PromptLength}",
            requestId, prompt, prompt.Length);

        try
        {
            var enhancedPrompt = $"A black and white kids coloring page.\n<image-description>\n{prompt}\n</image-description>\n{prompt}";
            
            _logger.LogDebug(
                "Prepared enhanced prompt for Gemini API. RequestId: {RequestId}, EnhancedPromptLength: {EnhancedPromptLength}",
                requestId, enhancedPrompt.Length);

            var requestBody = new
            {
                prompt = enhancedPrompt,
                config = new
                {
                    numberOfImages = 1,
                    aspectRatio = "9:16"
                }
            };

            var apiUrl = $"v1beta/models/imagen-4.0-generate-001:generateImages?key={_config.ApiKey[..8]}...";
            _logger.LogInformation(
                "Calling Google Gemini Imagen API. RequestId: {RequestId}, Model: imagen-4.0-generate-001, AspectRatio: 9:16",
                requestId);

            var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/imagen-4.0-generate-001:generateImages?key={_config.ApiKey}")
            {
                Content = JsonContent.Create(requestBody)
            };

            var apiStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.SendAsync(request, cancellationToken);
            apiStopwatch.Stop();

            _logger.LogInformation(
                "Received response from Gemini API. RequestId: {RequestId}, StatusCode: {StatusCode}, DurationMs: {DurationMs}",
                requestId, response.StatusCode, apiStopwatch.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Gemini API returned error. RequestId: {RequestId}, StatusCode: {StatusCode}, Error: {Error}",
                    requestId, response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync<ImageGenerationResponse>(cancellationToken: cancellationToken);
            
            if (result?.GeneratedImages == null || result.GeneratedImages.Count == 0)
            {
                _logger.LogError(
                    "No images generated in response. RequestId: {RequestId}, GeneratedImagesCount: {Count}",
                    requestId, result?.GeneratedImages?.Count ?? 0);
                throw new InvalidOperationException("No images were generated");
            }

            var imageBytes = result.GeneratedImages[0].ImageBytes;
            if (string.IsNullOrEmpty(imageBytes))
            {
                _logger.LogError("Image bytes are empty in response. RequestId: {RequestId}", requestId);
                throw new InvalidOperationException("Image bytes are empty");
            }

            var imageData = Convert.FromBase64String(imageBytes);
            stopwatch.Stop();

            _logger.LogInformation(
                "Image generation completed successfully. RequestId: {RequestId}, ImageSizeBytes: {ImageSize}, TotalDurationMs: {DurationMs}, ApiDurationMs: {ApiDurationMs}",
                requestId, imageData.Length, stopwatch.ElapsedMilliseconds, apiStopwatch.ElapsedMilliseconds);

            return imageData;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "HTTP error during image generation. RequestId: {RequestId}, DurationMs: {DurationMs}",
                requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "Image generation request cancelled. RequestId: {RequestId}, DurationMs: {DurationMs}",
                requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Unexpected error during image generation. RequestId: {RequestId}, DurationMs: {DurationMs}",
                requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
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
