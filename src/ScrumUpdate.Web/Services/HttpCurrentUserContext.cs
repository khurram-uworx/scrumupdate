using ScrumUpdate.Web.Services.Atlassian;
using ScrumUpdate.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ScrumUpdate.Web.Services;

public sealed class HttpCurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    LocalUserContext localUserContext,
    ChatDbContext dbContext) : ICurrentUserContext
{
    string? userId;

    public string GetRequiredUserId()
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Unable to resolve the current user because no active HTTP context is available.");

        var localUserId = localUserContext.GetOrCreateLocalUserId(httpContext);
        var token = dbContext.JiraOAuthTokens
            .AsNoTracking()
            .FirstOrDefault(x => x.LocalUserId == localUserId);

        if (token is null || string.IsNullOrWhiteSpace(token.AuthenticatedUserId))
        {
            throw new InvalidOperationException("Jira/Atlassian authentication is required before using Scrum Update.");
        }

        userId = token.AuthenticatedUserId;
        return userId;
    }
}
