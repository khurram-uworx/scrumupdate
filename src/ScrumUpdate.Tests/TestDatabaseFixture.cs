using Microsoft.EntityFrameworkCore;
using ScrumUpdate.Web.Data;

namespace ScrumUpdate.Tests;

/// <summary>
/// Helper class for setting up test databases with Entity Framework In-Memory provider.
/// </summary>
public class TestDatabaseFixture
{
    /// <summary>
    /// Creates an in-memory database context for testing.
    /// Each call creates a new isolated database.
    /// </summary>
    /// <returns>A configured ChatDbContext backed by in-memory storage.</returns>
    public static ChatDbContext CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ChatDbContext(options);
    }
}
