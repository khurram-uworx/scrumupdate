namespace ScrumUpdate.Web.Services;

public sealed class GeneratedScrumUpdate
{
    public DateOnly ScrumDate { get; init; }
    public DateTime GeneratedTimeUtc { get; init; }
    public string WhatIDidYesterday { get; init; } = string.Empty;
    public string WhatIPlanToDoToday { get; init; } = string.Empty;
    public string Blocker { get; init; } = string.Empty;
}

public interface IScrumUpdateGenerator
{
    GeneratedScrumUpdate? TryGenerateScrumUpdateForMessage(string userMessage);
    GeneratedScrumUpdate? TryParseGeneratedScrumUpdateFromAssistantMessage(string assistantMessage);
}
