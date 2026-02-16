using ScrumUpdate.Web.Services;

namespace ScrumUpdate.Tests;

public sealed class FakeCurrentUserContext(string userId = "test-user") : ICurrentUserContext
{
    public string UserId { get; } = userId;

    public string GetRequiredUserId()
    {
        return UserId;
    }
}
