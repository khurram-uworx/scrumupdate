using ScrumUpdate.Web.Services.Atlassian;

namespace ScrumUpdate.Web.Services;

public sealed class JiraScrumUpdateDraftService(
    IHttpContextAccessor httpContextAccessor,
    AtlassianOAuthService atlassianOAuthService)
{
    public async Task<GeneratedScrumUpdate?> TryGenerateAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        JiraScrumContext jiraContext;
        try
        {
            jiraContext = await atlassianOAuthService.GetScrumContextForTodayAndYesterdayAsync(httpContext, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var yesterdayItems = BuildYesterdayItems(jiraContext);
        var todayItems = BuildTodayItems(jiraContext);

        return new GeneratedScrumUpdate
        {
            ScrumDate = jiraContext.Today,
            GeneratedTimeUtc = jiraContext.GeneratedAtUtc,
            WhatIDidYesterday = yesterdayItems.Count == 0
                ? "No Jira updates found yesterday."
                : string.Join("; ", yesterdayItems),
            WhatIPlanToDoToday = todayItems.Count == 0
                ? "Continue work on active Jira issues."
                : string.Join("; ", todayItems),
            Blocker = "No blocker."
        };
    }

    static List<string> BuildYesterdayItems(JiraScrumContext jiraContext)
    {
        var items = new List<string>();

        foreach (var worklog in jiraContext.Worklogs.Where(x => DateOnly.FromDateTime(x.StartedUtc) == jiraContext.Yesterday))
        {
            var entry = $"{worklog.IssueKey}: logged {FormatDuration(worklog.TimeSpentSeconds)}";
            if (!string.IsNullOrWhiteSpace(worklog.Comment))
            {
                entry += $" ({TrimTo(worklog.Comment, 80)})";
            }

            items.Add(entry);
        }

        foreach (var comment in jiraContext.Comments.Where(x => DateOnly.FromDateTime(x.CreatedUtc) == jiraContext.Yesterday))
        {
            items.Add($"{comment.IssueKey}: commented '{TrimTo(comment.Comment, 80)}'");
        }

        foreach (var activity in jiraContext.Activities.Where(x => DateOnly.FromDateTime(x.ChangedUtc) == jiraContext.Yesterday))
        {
            items.Add($"{activity.IssueKey}: updated {activity.Field} from '{activity.FromValue}' to '{activity.ToValue}'");
        }

        return items.Take(8).ToList();
    }

    static List<string> BuildTodayItems(JiraScrumContext jiraContext)
    {
        var items = new List<string>();

        foreach (var issue in jiraContext.ActiveIssues)
        {
            items.Add($"{issue.Key}: continue {issue.Summary} ({issue.Status})");
        }

        foreach (var activity in jiraContext.Activities.Where(x => DateOnly.FromDateTime(x.ChangedUtc) == jiraContext.Today))
        {
            items.Add($"{activity.IssueKey}: follow up on {activity.Field} changes");
        }

        return items.Take(8).ToList();
    }

    static string FormatDuration(int seconds)
    {
        if (seconds <= 0)
        {
            return "0m";
        }

        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;

        if (hours > 0 && minutes > 0)
        {
            return $"{hours}h {minutes}m";
        }

        if (hours > 0)
        {
            return $"{hours}h";
        }

        return $"{minutes}m";
    }

    static string TrimTo(string text, int length)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }
}
