using System.Text.Json.Serialization;

namespace StickerDream.Server.Services;

/// <summary>
/// JSON source generator context for image generation API requests/responses
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ImageGenerationRequest))]
[JsonSerializable(typeof(ImageGenerationResponse))]
internal sealed partial class ImageGenerationJsonContext : JsonSerializerContext
{
}

internal sealed record ImageGenerationRequest(
    string Prompt,
    ImageGenerationConfig Config);

internal sealed record ImageGenerationConfig(
    int NumberOfImages = 1,
    string AspectRatio = "9:16");

internal sealed record ImageGenerationResponse(
    List<GeneratedImage> GeneratedImages);

internal sealed record GeneratedImage(
    string ImageBytes);
