using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScrumUpdate.Web.Data;

namespace ScrumUpdate.Web.Services.Atlassian;

public sealed record JiraConnectionStatus(bool Connected, DateTime? AccessTokenExpiresAtUtc, string? CloudId);

public sealed class AtlassianOAuthService(
    ChatDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IOptions<AtlassianOAuthOptions> options,
    LocalUserContext localUserContext,
    ILogger<AtlassianOAuthService> logger)
{
    const string OAuthStateCookieName = "atlassian_oauth_state";
    const string OAuthVerifierCookieName = "atlassian_oauth_verifier";
    const string OAuthReturnUrlCookieName = "atlassian_oauth_return_url";

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly AtlassianOAuthOptions atlassianOptions = options.Value;

    public IResult BeginLogin(HttpContext httpContext, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(atlassianOptions.ClientId) || string.IsNullOrWhiteSpace(atlassianOptions.ClientSecret))
        {
            logger.LogError("Atlassian OAuth is not configured. Missing client id or client secret.");
            return Results.Problem("Atlassian OAuth is not configured. Set AtlassianOAuth:ClientId and AtlassianOAuth:ClientSecret.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var state = LocalUserContext.CreateSecureRandomToken();
        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        };

        httpContext.Response.Cookies.Append(OAuthStateCookieName, state, cookieOptions);
        httpContext.Response.Cookies.Append(OAuthVerifierCookieName, codeVerifier, cookieOptions);
        httpContext.Response.Cookies.Append(OAuthReturnUrlCookieName, normalizedReturnUrl, cookieOptions);

        var callbackUrl = BuildCallbackUrl(httpContext);
        var scope = string.Join(' ', atlassianOptions.Scopes.Distinct(StringComparer.Ordinal));

        var query = new Dictionary<string, string?>
        {
            ["audience"] = "api.atlassian.com",
            ["client_id"] = atlassianOptions.ClientId,
            ["scope"] = scope,
            ["redirect_uri"] = callbackUrl,
            ["state"] = state,
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var authorizeUrl = QueryHelpers.AddQueryString("https://auth.atlassian.com/authorize", query);
        return Results.Redirect(authorizeUrl);
    }

    public async Task<IResult> HandleCallbackAsync(HttpContext httpContext, string? code, string? state, string? error, string? errorDescription, CancellationToken cancellationToken)
    {
        var returnUrl = GetCookie(httpContext, OAuthReturnUrlCookieName) ?? "/";
        var expectedState = GetCookie(httpContext, OAuthStateCookieName);
        var codeVerifier = GetCookie(httpContext, OAuthVerifierCookieName);
        ClearOAuthHandshakeCookies(httpContext);

        if (!string.IsNullOrWhiteSpace(error))
        {
            logger.LogWarning("Atlassian OAuth callback failed. Error={Error}, Description={Description}", error, errorDescription);
            return Results.Redirect(QueryHelpers.AddQueryString(returnUrl, "jiraAuth", "failed"));
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(expectedState) || !FixedTimeEquals(state, expectedState) || string.IsNullOrWhiteSpace(codeVerifier))
        {
            logger.LogWarning("Invalid Atlassian OAuth callback state or verifier.");
            return Results.BadRequest("Invalid OAuth callback state.");
        }

        var tokenResponse = await ExchangeCodeForTokenAsync(httpContext, code, codeVerifier, cancellationToken);
        if (tokenResponse is null)
        {
            return Results.Redirect(QueryHelpers.AddQueryString(returnUrl, "jiraAuth", "failed"));
        }

        var localUserId = localUserContext.GetOrCreateLocalUserId(httpContext);
        var cloudId = await GetPreferredCloudIdAsync(tokenResponse.AccessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(cloudId))
        {
            logger.LogWarning("OAuth succeeded but no Jira cloud resource could be resolved for local user {LocalUserId}.", localUserId);
            return Results.Redirect(QueryHelpers.AddQueryString(returnUrl, "jiraAuth", "failed"));
        }

        var authenticatedUserId = await GetAuthenticatedUserIdAsync(tokenResponse.AccessToken, cloudId, cancellationToken);
        if (string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            logger.LogWarning("OAuth succeeded but Atlassian account id could not be resolved for local user {LocalUserId}.", localUserId);
            return Results.Redirect(QueryHelpers.AddQueryString(returnUrl, "jiraAuth", "failed"));
        }

        await SaveOrUpdateTokenAsync(localUserId, authenticatedUserId, tokenResponse, cloudId, cancellationToken);
        return Results.Redirect(QueryHelpers.AddQueryString(returnUrl, "jiraAuth", "connected"));
    }

    public async Task<IResult> DisconnectAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var localUserId = localUserContext.GetOrCreateLocalUserId(httpContext);
        var token = await dbContext.JiraOAuthTokens.FirstOrDefaultAsync(x => x.LocalUserId == localUserId, cancellationToken);
        if (token is not null)
        {
            dbContext.JiraOAuthTokens.Remove(token);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Redirect("/");
    }

    public async Task<JiraConnectionStatus> GetConnectionStatusAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var localUserId = localUserContext.GetOrCreateLocalUserId(httpContext);
        var token = await dbContext.JiraOAuthTokens.AsNoTracking().FirstOrDefaultAsync(x => x.LocalUserId == localUserId, cancellationToken);
        return token is null
            ? new JiraConnectionStatus(false, null, null)
            : new JiraConnectionStatus(!string.IsNullOrWhiteSpace(token.AuthenticatedUserId), token.AccessTokenExpiresAtUtc, token.CloudId);
    }

    public async Task<IReadOnlyList<JiraIssueSummary>> GetMyOpenIssuesAsync(HttpContext httpContext, int maxResults, CancellationToken cancellationToken)
    {
        var connectedContext = await GetConnectedUserContextAsync(httpContext, cancellationToken);

        var cappedMaxResults = Math.Clamp(maxResults, 1, 100);
        var jql = "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC";
        var query = new Dictionary<string, string?>
        {
            ["jql"] = jql,
            ["maxResults"] = cappedMaxResults.ToString(),
            ["fields"] = "summary,status,updated,project"
        };

        var relative = QueryHelpers.AddQueryString("/ex/jira/" + connectedContext.CloudId + "/rest/api/3/search/jql", query);
        var client = httpClientFactory.CreateClient("AtlassianApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connectedContext.AccessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Failed to fetch Jira issues. Status={StatusCode}, Body={Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException("Failed to fetch Jira issues.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var issues = new List<JiraIssueSummary>();
        if (!json.RootElement.TryGetProperty("issues", out var issuesElement) || issuesElement.ValueKind != JsonValueKind.Array)
        {
            return issues;
        }

        foreach (var issue in issuesElement.EnumerateArray())
        {
            var key = GetString(issue, "key") ?? string.Empty;
            var fields = issue.TryGetProperty("fields", out var fieldValue) ? fieldValue : default;
            var summary = GetString(fields, "summary") ?? string.Empty;
            var status = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("status", out var statusValue)
                ? GetString(statusValue, "name") ?? string.Empty
                : string.Empty;
            var updatedRaw = GetString(fields, "updated");
            var updated = DateTime.TryParse(updatedRaw, out var parsedDate) ? parsedDate.ToUniversalTime() : DateTime.MinValue;
            var projectName = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("project", out var projectValue)
                ? GetString(projectValue, "name") ?? string.Empty
                : string.Empty;

            issues.Add(new JiraIssueSummary(key, summary, status, updated, projectName));
        }

        return issues;
    }

    public async Task<JiraScrumContext> GetScrumContextForTodayAndYesterdayAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var connectedContext = await GetConnectedUserContextAsync(httpContext, cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);
        var yesterday = today.AddDays(-1);
        var windowStart = yesterday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var windowEnd = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var fields = "summary,status,updated,project,worklog,comment";
        var jql = "assignee = currentUser() AND statusCategory NOT IN (\"To Do\", \"Done\") ORDER BY updated DESC";
        var issues = await SearchIssuesAsync(
            connectedContext.CloudId,
            connectedContext.AccessToken,
            jql,
            maxResults: 50,
            fields,
            expand: "changelog",
            cancellationToken);

        var activeIssues = new List<JiraScrumIssue>();
        var worklogs = new List<JiraWorklogActivity>();
        var comments = new List<JiraCommentActivity>();
        var activities = new List<JiraChangeActivity>();

        foreach (var issue in issues)
        {
            var issueKey = GetString(issue, "key") ?? string.Empty;
            if (!issue.TryGetProperty("fields", out var issueFields) || issueFields.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var issueSummary = GetString(issueFields, "summary") ?? string.Empty;
            var issueStatus = issueFields.TryGetProperty("status", out var statusValue) ? GetString(statusValue, "name") ?? string.Empty : string.Empty;
            var issueUpdatedUtc = ParseDateTime(GetString(issueFields, "updated"));
            var issueProject = issueFields.TryGetProperty("project", out var projectValue) ? GetString(projectValue, "name") ?? string.Empty : string.Empty;
            activeIssues.Add(new JiraScrumIssue(issueKey, issueSummary, issueStatus, issueUpdatedUtc, issueProject));

            if (issueFields.TryGetProperty("worklog", out var worklogValue) &&
                worklogValue.ValueKind == JsonValueKind.Object &&
                worklogValue.TryGetProperty("worklogs", out var worklogItems) &&
                worklogItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var worklog in worklogItems.EnumerateArray())
                {
                    var authorId = worklog.TryGetProperty("author", out var authorValue) ? GetString(authorValue, "accountId") : null;
                    if (!string.Equals(authorId, connectedContext.AuthenticatedUserId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var startedUtc = ParseDateTime(GetString(worklog, "started"));
                    if (startedUtc < windowStart || startedUtc >= windowEnd)
                    {
                        continue;
                    }

                    var timeSpentSeconds = worklog.TryGetProperty("timeSpentSeconds", out var spentValue) && spentValue.ValueKind == JsonValueKind.Number
                        ? spentValue.GetInt32()
                        : 0;
                    var commentText = worklog.TryGetProperty("comment", out var commentValue)
                        ? ExtractAtlassianDocumentText(commentValue)
                        : string.Empty;

                    worklogs.Add(new JiraWorklogActivity(issueKey, issueSummary, startedUtc, timeSpentSeconds, commentText));
                }
            }

            if (issueFields.TryGetProperty("comment", out var commentContainer) &&
                commentContainer.ValueKind == JsonValueKind.Object &&
                commentContainer.TryGetProperty("comments", out var commentItems) &&
                commentItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var comment in commentItems.EnumerateArray())
                {
                    var authorId = comment.TryGetProperty("author", out var authorValue) ? GetString(authorValue, "accountId") : null;
                    if (!string.Equals(authorId, connectedContext.AuthenticatedUserId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var createdUtc = ParseDateTime(GetString(comment, "created"));
                    if (createdUtc < windowStart || createdUtc >= windowEnd)
                    {
                        continue;
                    }

                    var bodyText = comment.TryGetProperty("body", out var bodyValue)
                        ? ExtractAtlassianDocumentText(bodyValue)
                        : string.Empty;

                    comments.Add(new JiraCommentActivity(issueKey, issueSummary, createdUtc, bodyText));
                }
            }

            if (issue.TryGetProperty("changelog", out var changelogValue) &&
                changelogValue.ValueKind == JsonValueKind.Object &&
                changelogValue.TryGetProperty("histories", out var historyItems) &&
                historyItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var history in historyItems.EnumerateArray())
                {
                    var authorId = history.TryGetProperty("author", out var authorValue) ? GetString(authorValue, "accountId") : null;
                    if (!string.Equals(authorId, connectedContext.AuthenticatedUserId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var changedUtc = ParseDateTime(GetString(history, "created"));
                    if (changedUtc < windowStart || changedUtc >= windowEnd)
                    {
                        continue;
                    }

                    if (!history.TryGetProperty("items", out var changedItems) || changedItems.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var changedItem in changedItems.EnumerateArray())
                    {
                        var field = GetString(changedItem, "field") ?? string.Empty;
                        var fromValue = GetString(changedItem, "fromString") ?? string.Empty;
                        var toValue = GetString(changedItem, "toString") ?? string.Empty;
                        activities.Add(new JiraChangeActivity(issueKey, issueSummary, changedUtc, field, fromValue, toValue));
                    }
                }
            }
        }

        return new JiraScrumContext(
            GeneratedAtUtc: nowUtc,
            Yesterday: yesterday,
            Today: today,
            ActiveIssues: activeIssues.OrderByDescending(x => x.UpdatedUtc).ToList(),
            Worklogs: worklogs.OrderByDescending(x => x.StartedUtc).ToList(),
            Comments: comments.OrderByDescending(x => x.CreatedUtc).ToList(),
            Activities: activities.OrderByDescending(x => x.ChangedUtc).ToList());
    }

    async Task<ConnectedUserContext> GetConnectedUserContextAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var localUserId = localUserContext.GetOrCreateLocalUserId(httpContext);
        var token = await dbContext.JiraOAuthTokens.FirstOrDefaultAsync(x => x.LocalUserId == localUserId, cancellationToken);
        if (token is null)
        {
            throw new InvalidOperationException("Jira is not connected for this user.");
        }

        if (string.IsNullOrWhiteSpace(token.AuthenticatedUserId))
        {
            throw new InvalidOperationException("Connected Jira account is missing authenticated user identity. Reconnect your Jira account.");
        }

        var accessToken = await GetValidAccessTokenAsync(token, cancellationToken);
        var cloudId = token.CloudId;
        if (string.IsNullOrWhiteSpace(cloudId))
        {
            cloudId = await GetPreferredCloudIdAsync(accessToken, cancellationToken);
            token.CloudId = cloudId;
            token.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(cloudId))
        {
            throw new InvalidOperationException("No Jira Cloud site is accessible with this token.");
        }

        return new ConnectedUserContext(cloudId, accessToken, token.AuthenticatedUserId);
    }

    async Task<List<JsonElement>> SearchIssuesAsync(
        string cloudId,
        string accessToken,
        string jql,
        int maxResults,
        string fields,
        string? expand,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["jql"] = jql,
            ["maxResults"] = Math.Clamp(maxResults, 1, 100).ToString(CultureInfo.InvariantCulture),
            ["fields"] = fields
        };

        if (!string.IsNullOrWhiteSpace(expand))
        {
            query["expand"] = expand;
        }

        var relative = QueryHelpers.AddQueryString("/ex/jira/" + cloudId + "/rest/api/3/search/jql", query);
        var client = httpClientFactory.CreateClient("AtlassianApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Failed to search Jira issues. Status={StatusCode}, Body={Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException("Failed to fetch Jira issues.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("issues", out var issuesElement) || issuesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var issues = new List<JsonElement>();
        foreach (var issue in issuesElement.EnumerateArray())
        {
            issues.Add(issue.Clone());
        }

        return issues;
    }

    async Task SaveOrUpdateTokenAsync(string localUserId, string authenticatedUserId, AtlassianTokenResponse tokenResponse, string cloudId, CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(authenticatedUserId, cancellationToken);

        var record = await dbContext.JiraOAuthTokens.FirstOrDefaultAsync(x => x.LocalUserId == localUserId, cancellationToken);
        if (record is null)
        {
            record = new JiraOAuthToken
            {
                LocalUserId = localUserId
            };

            dbContext.JiraOAuthTokens.Add(record);
        }

        record.AuthenticatedUserId = authenticatedUserId;
        record.AccessToken = tokenResponse.AccessToken;
        record.RefreshToken = tokenResponse.RefreshToken;
        record.Scope = tokenResponse.Scope ?? string.Empty;
        record.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn - 60, 60));
        record.CloudId = cloudId;
        record.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    async Task EnsureUserExistsAsync(string authenticatedUserId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.AppUsers.AnyAsync(x => x.Id == authenticatedUserId, cancellationToken);
        if (existing)
        {
            return;
        }

        dbContext.AppUsers.Add(new AppUser
        {
            Id = authenticatedUserId,
            CreatedDateUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    async Task<string> GetValidAccessTokenAsync(JiraOAuthToken token, CancellationToken cancellationToken)
    {
        if (token.AccessTokenExpiresAtUtc > DateTime.UtcNow)
        {
            return token.AccessToken;
        }

        var refreshed = await RefreshAccessTokenAsync(token.RefreshToken, cancellationToken);
        if (refreshed is null)
        {
            throw new InvalidOperationException("Jira token refresh failed. Reconnect your Jira account.");
        }

        token.AccessToken = refreshed.AccessToken;
        token.RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? token.RefreshToken : refreshed.RefreshToken;
        token.Scope = refreshed.Scope ?? token.Scope;
        token.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(refreshed.ExpiresIn - 60, 60));
        token.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return token.AccessToken;
    }

    async Task<AtlassianTokenResponse?> ExchangeCodeForTokenAsync(HttpContext httpContext, string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var request = new
        {
            grant_type = "authorization_code",
            client_id = atlassianOptions.ClientId,
            client_secret = atlassianOptions.ClientSecret,
            code,
            redirect_uri = BuildCallbackUrl(httpContext),
            code_verifier = codeVerifier
        };

        return await SendTokenRequestAsync(request, cancellationToken);
    }

    async Task<AtlassianTokenResponse?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var request = new
        {
            grant_type = "refresh_token",
            client_id = atlassianOptions.ClientId,
            client_secret = atlassianOptions.ClientSecret,
            refresh_token = refreshToken
        };

        return await SendTokenRequestAsync(request, cancellationToken);
    }

    async Task<AtlassianTokenResponse?> SendTokenRequestAsync(object requestBody, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AtlassianAuth");
        using var response = await client.PostAsJsonAsync("/oauth/token", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Atlassian token request failed. Status={StatusCode}, Body={Body}", (int)response.StatusCode, body);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AtlassianTokenResponse>(JsonOptions, cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken) || payload.ExpiresIn <= 0)
        {
            logger.LogWarning("Atlassian token response was empty or invalid.");
            return null;
        }

        return payload;
    }

    async Task<string?> GetPreferredCloudIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AtlassianApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/oauth/token/accessible-resources");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Failed to resolve accessible Jira resources. Status={StatusCode}, Body={Body}", (int)response.StatusCode, body);
            return null;
        }

        var resources = await response.Content.ReadFromJsonAsync<List<AtlassianAccessibleResource>>(JsonOptions, cancellationToken) ?? [];

        return resources
            .FirstOrDefault(r => r.Scopes.Any(s => s.StartsWith("read:jira", StringComparison.OrdinalIgnoreCase)))?.Id
            ?? resources.FirstOrDefault()?.Id;
    }

    async Task<string?> GetAuthenticatedUserIdAsync(string accessToken, string cloudId, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AtlassianApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ex/jira/" + cloudId + "/rest/api/3/myself");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Failed to resolve Jira authenticated user id. Status={StatusCode}, Body={Body}", (int)response.StatusCode, body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return GetString(json.RootElement, "accountId");
    }

    static string? GetCookie(HttpContext httpContext, string cookieName)
    {
        return httpContext.Request.Cookies.TryGetValue(cookieName, out var value) ? value : null;
    }

    static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
        {
            return "/";
        }

        return returnUrl;
    }

    static string BuildCallbackUrl(HttpContext httpContext)
    {
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/auth/atlassian/callback";
    }

    static string CreateCodeVerifier()
    {
        return LocalUserContext.CreateSecureRandomToken(64);
    }

    static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind == JsonValueKind.String ? propertyValue.GetString() : propertyValue.ToString();
    }

    static DateTime ParseDateTime(string? value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return DateTime.MinValue;
        }

        return parsed.UtcDateTime;
    }

    static string ExtractAtlassianDocumentText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendAtlassianDocumentText(element, builder);
        return builder.ToString().Trim();
    }

    static void AppendAtlassianDocumentText(JsonElement element, StringBuilder builder)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var textValue) && textValue.ValueKind == JsonValueKind.String)
            {
                var text = textValue.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(text);
                }
            }

            if (element.TryGetProperty("content", out var contentValue) && contentValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in contentValue.EnumerateArray())
                {
                    AppendAtlassianDocumentText(child, builder);
                }
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                AppendAtlassianDocumentText(child, builder);
            }
        }
    }

    static void ClearOAuthHandshakeCookies(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(OAuthStateCookieName);
        httpContext.Response.Cookies.Delete(OAuthVerifierCookieName);
        httpContext.Response.Cookies.Delete(OAuthReturnUrlCookieName);
    }

    sealed class AtlassianTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

    }

    sealed class AtlassianAccessibleResource
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Scopes { get; set; } = [];
    }

    sealed record ConnectedUserContext(string CloudId, string AccessToken, string AuthenticatedUserId);
}
