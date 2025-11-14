using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace StickerDream.Server.Services;

/// <summary>
/// Generates images using Google Gemini Imagen API
/// </summary>
public sealed class ImageGenerationService(
    HttpClient httpClient,
    IOptions<GeminiConfig> config,
    ILogger<ImageGenerationService> logger,
    TimeProvider timeProvider) : IImageGenerationService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly GeminiConfig _config = config.Value;
    private readonly ILogger<ImageGenerationService> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<byte[]> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var startTime = _timeProvider.GetTimestamp();
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

            var requestBody = new ImageGenerationRequest(
                enhancedPrompt,
                new ImageGenerationConfig(NumberOfImages: 1, AspectRatio: "9:16"));

            _logger.LogInformation(
                "Calling Google Gemini Imagen API. RequestId: {RequestId}, Model: imagen-4.0-generate-001, AspectRatio: 9:16",
                requestId);

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"v1beta/models/imagen-4.0-generate-001:generateImages?key={_config.ApiKey}")
            {
                Content = JsonContent.Create(requestBody, options: new(ImageGenerationJsonContext.Default))
            };

            var apiStartTime = _timeProvider.GetTimestamp();
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var apiDuration = _timeProvider.GetElapsedTime(apiStartTime);

            _logger.LogInformation(
                "Received response from Gemini API. RequestId: {RequestId}, StatusCode: {StatusCode}, DurationMs: {DurationMs}",
                requestId, response.StatusCode, apiDuration.TotalMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError(
                    "Gemini API returned error. RequestId: {RequestId}, StatusCode: {StatusCode}, Error: {Error}",
                    requestId, response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync(
                ImageGenerationJsonContext.Default.ImageGenerationResponse,
                cancellationToken).ConfigureAwait(false);
            
            if (result?.GeneratedImages is not { Count: > 0 } images)
            {
                _logger.LogError(
                    "No images generated in response. RequestId: {RequestId}, GeneratedImagesCount: {Count}",
                    requestId, result?.GeneratedImages?.Count ?? 0);
                throw new InvalidOperationException("No images were generated");
            }

            var imageBytes = images[0].ImageBytes;
            if (string.IsNullOrEmpty(imageBytes))
            {
                _logger.LogError("Image bytes are empty in response. RequestId: {RequestId}", requestId);
                throw new InvalidOperationException("Image bytes are empty");
            }

            var imageData = Convert.FromBase64String(imageBytes);
            var totalDuration = _timeProvider.GetElapsedTime(startTime);

            _logger.LogInformation(
                "Image generation completed successfully. RequestId: {RequestId}, ImageSizeBytes: {ImageSize}, TotalDurationMs: {DurationMs}, ApiDurationMs: {ApiDurationMs}",
                requestId, imageData.Length, totalDuration.TotalMilliseconds, apiDuration.TotalMilliseconds);

            return imageData;
        }
        catch (HttpRequestException ex)
        {
            var duration = _timeProvider.GetElapsedTime(startTime);
            _logger.LogError(ex,
                "HTTP error during image generation. RequestId: {RequestId}, DurationMs: {DurationMs}",
                requestId, duration.TotalMilliseconds);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            var duration = _timeProvider.GetElapsedTime(startTime);
            _logger.LogWarning(ex,
                "Image generation request cancelled. RequestId: {RequestId}, DurationMs: {DurationMs}",
                requestId, duration.TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            var duration = _timeProvider.GetElapsedTime(startTime);
            _logger.LogError(ex,
                "Unexpected error during image generation. RequestId: {RequestId}, DurationMs: {DurationMs}",
                requestId, duration.TotalMilliseconds);
            throw;
        }
    }
}
