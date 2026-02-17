namespace ScrumUpdate.Web.Services;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string Model { get; init; } = "gemini-3-flash-preview"; //gemini-2.5-flash
    public string? ApiKey { get; init; }
}
