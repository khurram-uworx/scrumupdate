namespace ScrumUpdate.Web.Services;

public sealed class GeneratedScrumUpdate
{
    public DateOnly ScrumDate { get; init; }
    public DateTime GeneratedTimeUtc { get; init; }
    public string WhatIDidYesterday { get; init; } = string.Empty;
    public string WhatIPlanToDoToday { get; init; } = string.Empty;
    public string Blocker { get; init; } = string.Empty;
}

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ScrumGenerationMetadata), typeDiscriminator: "scrum-generation")]
public abstract class ChatMessageMetadata
{
}

public sealed class ScrumGenerationMetadata : ChatMessageMetadata
{
    public required GeneratedScrumUpdate ScrumUpdate { get; init; }
    public DateTime CapturedAtUtc { get; init; }
}

public interface IScrumUpdateGenerator
{
    GeneratedScrumUpdate? TryGenerateScrumUpdateForMessage(string userMessage);
    GeneratedScrumUpdate? TryParseGeneratedScrumUpdateFromAssistantMessage(string assistantMessage);
}
