namespace ScrumUpdate.Web.Services.Atlassian;

public sealed record JiraScrumIssue(
    string Key,
    string Summary,
    string Status,
    DateTime UpdatedUtc,
    string ProjectName);

public sealed record JiraWorklogActivity(
    string IssueKey,
    string IssueSummary,
    DateTime StartedUtc,
    int TimeSpentSeconds,
    string Comment);

public sealed record JiraCommentActivity(
    string IssueKey,
    string IssueSummary,
    DateTime CreatedUtc,
    string Comment);

public sealed record JiraChangeActivity(
    string IssueKey,
    string IssueSummary,
    DateTime ChangedUtc,
    string Field,
    string FromValue,
    string ToValue);

public sealed record JiraScrumContext(
    DateTime GeneratedAtUtc,
    DateOnly Yesterday,
    DateOnly Today,
    IReadOnlyList<JiraScrumIssue> ActiveIssues,
    IReadOnlyList<JiraWorklogActivity> Worklogs,
    IReadOnlyList<JiraCommentActivity> Comments,
    IReadOnlyList<JiraChangeActivity> Activities);
