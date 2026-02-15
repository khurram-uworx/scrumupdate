using Microsoft.Extensions.AI;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ScrumUpdate.Web.Services;

/// <summary>
/// A dummy implementation of IChatClient for testing without Ollama.
/// Generates scrum updates only for explicit commands.
/// </summary>
public class DummyChatClient : IChatClient, IScrumUpdateGenerator
{
    const string GenericResponse = "I am dummy AI and can generate scrum updates on request.";
    int generationCounter;

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = BuildAssistantResponse(chatMessages);

        // Simulate streaming by yielding the response character by character
        var delay = 10; // milliseconds between characters

        foreach (var character in response)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            await Task.Delay(delay, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, character.ToString());
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = BuildAssistantResponse(chatMessages);
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, response)]));
    }

    public GeneratedScrumUpdate? TryGenerateScrumUpdateForMessage(string userMessage)
    {
        if (!IsScrumCommand(userMessage))
        {
            return null;
        }

        var sequence = Interlocked.Increment(ref generationCounter);
        var generatedAtUtc = DateTime.UtcNow;
        var variants = GetVariant(sequence);

        return new GeneratedScrumUpdate
        {
            ScrumDate = DateOnly.FromDateTime(generatedAtUtc),
            GeneratedTimeUtc = generatedAtUtc,
            WhatIDidYesterday = $"{variants.WhatIDidYesterday} (v{sequence})",
            WhatIPlanToDoToday = $"{variants.WhatIPlanToDoToday} (v{sequence})",
            Blocker = $"{variants.Blocker} (v{sequence})"
        };
    }

    public GeneratedScrumUpdate? TryParseGeneratedScrumUpdateFromAssistantMessage(string assistantMessage)
    {
        if (string.IsNullOrWhiteSpace(assistantMessage))
        {
            return null;
        }

        var lines = assistantMessage
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 5 || !lines[0].StartsWith("Scrum update for ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var dateText = lines[0]["Scrum update for ".Length..];
        if (!DateOnly.TryParse(dateText, out var scrumDate))
        {
            return null;
        }

        var generatedTimeText = ExtractField(lines, "Generated at:");
        if (generatedTimeText == null || !DateTime.TryParse(generatedTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var generatedTimeUtc))
        {
            return null;
        }

        var yesterday = ExtractField(lines, "Yesterday:");
        var today = ExtractField(lines, "Today:");
        var blocker = ExtractField(lines, "Blocker:");
        if (yesterday == null || today == null || blocker == null)
        {
            return null;
        }

        return new GeneratedScrumUpdate
        {
            ScrumDate = scrumDate,
            GeneratedTimeUtc = generatedTimeUtc,
            WhatIDidYesterday = yesterday,
            WhatIPlanToDoToday = today,
            Blocker = blocker
        };
    }

    string BuildAssistantResponse(IEnumerable<ChatMessage> chatMessages)
    {
        var userMessage = ExtractLastUserText(chatMessages);
        var scrumUpdate = TryGenerateScrumUpdateForMessage(userMessage);
        return scrumUpdate == null ? GenericResponse : FormatScrumUpdateResponse(scrumUpdate);
    }

    static string ExtractLastUserText(IEnumerable<ChatMessage> chatMessages)
    {
        var lastUserMessage = chatMessages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUserMessage?.Text ?? string.Empty;
    }

    static bool IsScrumCommand(string userMessage)
    {
        var normalized = userMessage.Trim().ToLowerInvariant();
        return normalized.Contains("scrum update") || normalized.Contains("regenerate");
    }

    static (string WhatIDidYesterday, string WhatIPlanToDoToday, string Blocker) GetVariant(int sequence)
    {
        var variants = new[]
        {
            ("Finished initial multi-session persistence.", "Wire scrum-session generation by date.", "No blocker."),
            ("Completed scrum session tagging and validation fixes.", "Polish regenerate flow and add tests.", "Waiting on one PR review."),
            ("Refined chat persistence for repeated updates.", "Clean up prompts and session UX.", "Need product confirmation on wording.")
        };

        return variants[(sequence - 1) % variants.Length];
    }

    static string? ExtractField(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line == null)
        {
            return null;
        }

        return line[prefix.Length..].Trim();
    }

    static string FormatScrumUpdateResponse(GeneratedScrumUpdate scrumUpdate)
    {
        return $"""
            Scrum update for {scrumUpdate.ScrumDate:yyyy-MM-dd}
            Generated at: {scrumUpdate.GeneratedTimeUtc:O}

            Yesterday: {scrumUpdate.WhatIDidYesterday}
            Today: {scrumUpdate.WhatIPlanToDoToday}
            Blocker: {scrumUpdate.Blocker}
            """;
    }

    public ChatClientMetadata Metadata => new(nameof(DummyChatClient), new Uri("http://localhost"), "dummy");

    public object? GetService(Type serviceType, object? serviceKey) => this;

    public TService? GetService<TService>(object? key = null) where TService : class => this as TService;

    void IDisposable.Dispose() { }
}

