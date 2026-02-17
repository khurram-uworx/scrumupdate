namespace ScrumUpdate.Web.Services;

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";
    public string? ApiUrl { get; init; }
    public string Model { get; init; } = "gpt-4.1-mini";
    public string? ApiKey { get; init; }
}
