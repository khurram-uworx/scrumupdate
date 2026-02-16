namespace ScrumUpdate.Web.Services.Atlassian;

public sealed record JiraIssueSummary(
    string Key,
    string Summary,
    string Status,
    DateTime UpdatedUtc,
    string ProjectName);
