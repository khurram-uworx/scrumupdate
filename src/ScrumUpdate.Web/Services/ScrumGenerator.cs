using System.Globalization;

namespace ScrumUpdate.Web.Services;

public sealed class ScrumGenerator : IScrumUpdateGenerator
{
    int generationCounter;

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

    [Obsolete("Prefer deterministic metadata from tool execution. This parser is kept for fallback/legacy flows.")]
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
}
