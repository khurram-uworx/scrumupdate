namespace ScrumUpdate.Web.Services.Atlassian;

public sealed class AtlassianOAuthOptions
{
    public const string SectionName = "AtlassianOAuth";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = ["read:jira-work", "read:jira-user", "offline_access"];
}
