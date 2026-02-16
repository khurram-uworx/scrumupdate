using Microsoft.AspNetCore.Http;
using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;
using ScrumUpdate.Web.Services.Atlassian;

namespace ScrumUpdate.Tests;

[TestFixture]
public class HttpCurrentUserContextTests
{
    ChatDbContext dbContext = null!;

    [SetUp]
    public void Setup()
    {
        dbContext = TestDatabaseFixture.CreateTestDbContext();
    }

    [TearDown]
    public void TearDown()
    {
        dbContext.Dispose();
    }

    [Test]
    public void GetRequiredUserId_ThrowsWhenJiraIsNotConnected()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var localUserContext = new LocalUserContext();

        var sut = new HttpCurrentUserContext(accessor, localUserContext, dbContext);

        Assert.That(() => sut.GetRequiredUserId(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void GetRequiredUserId_ReturnsAuthenticatedAtlassianUserId()
    {
        var httpContext = new DefaultHttpContext();
        var localUserId = "local-browser-user-1";
        httpContext.Request.Headers.Cookie = $"scrumupdate_user={localUserId}";
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var localUserContext = new LocalUserContext();
        var authenticatedUserId = "atlassian-account-123";

        dbContext.AppUsers.Add(new AppUser
        {
            Id = authenticatedUserId,
            CreatedDateUtc = DateTime.UtcNow
        });
        dbContext.JiraOAuthTokens.Add(new JiraOAuthToken
        {
            LocalUserId = localUserId,
            AuthenticatedUserId = authenticatedUserId,
            AccessToken = "token",
            RefreshToken = "refresh",
            Scope = "read:jira-work",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            CloudId = "cloud",
            UpdatedAtUtc = DateTime.UtcNow
        });
        dbContext.SaveChanges();

        var sut = new HttpCurrentUserContext(accessor, localUserContext, dbContext);

        var result = sut.GetRequiredUserId();

        Assert.That(result, Is.EqualTo(authenticatedUserId));
    }
}
