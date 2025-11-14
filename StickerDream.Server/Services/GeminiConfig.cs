namespace StickerDream.Server.Services;

/// <summary>
/// Configuration for Google Gemini API
/// </summary>
public sealed class GeminiConfig
{
    public const string SectionName = "Gemini";
    
    public required string ApiKey { get; init; }
}
