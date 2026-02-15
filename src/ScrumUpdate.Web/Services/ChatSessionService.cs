using Microsoft.EntityFrameworkCore;
using ScrumUpdate.Web.Data;

namespace ScrumUpdate.Web.Services;

public class ChatSessionService
{
    readonly ChatDbContext dbContext;
    const string DefaultUserId = "default-user";

    public ChatSessionService(ChatDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<ChatSession> CreateSessionAsync()
    {
        var sessionCount = await dbContext.ChatSessions
            .Where(s => s.UserId == DefaultUserId)
            .CountAsync();

        var session = new ChatSession
        {
            UserId = DefaultUserId,
            Title = $"Chat {sessionCount + 1}",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        dbContext.ChatSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return session;
    }

    public async Task<List<ChatSession>> GetSessionsAsync()
    {
        return await dbContext.ChatSessions
            .Where(s => s.UserId == DefaultUserId)
            .OrderByDescending(s => s.UpdatedDate)
            .ToListAsync();
    }

    public async Task<ChatSession?> GetSessionAsync(int sessionId)
    {
        var session = await dbContext.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == DefaultUserId);

        if (session?.Messages != null)
        {
            // Keep order deterministic even when timestamps are equal.
            session.Messages = session.Messages
                .OrderBy(m => m.Timestamp)
                .ThenBy(m => m.Id)
                .ToList();
        }

        return session;
    }

    public async Task SaveMessageAsync(int sessionId, string role, string content)
    {
        var message = new ChatMessageEntity
        {
            ChatSessionId = sessionId,
            Role = NormalizeRole(role),
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        dbContext.ChatMessages.Add(message);

        var session = await dbContext.ChatSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.UpdatedDate = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SaveSessionAsync(int sessionId, IEnumerable<(string Role, string Content)> messages)
    {
        var session = await dbContext.ChatSessions.FindAsync(sessionId);
        if (session == null) return;

        var existingMessages = dbContext.ChatMessages.Where(m => m.ChatSessionId == sessionId);
        dbContext.ChatMessages.RemoveRange(existingMessages);

        foreach (var (role, content) in messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)))
        {
            var message = new ChatMessageEntity
            {
                ChatSessionId = sessionId,
                Role = NormalizeRole(role),
                Content = content,
                Timestamp = DateTime.UtcNow
            };
            dbContext.ChatMessages.Add(message);
        }

        session.UpdatedDate = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteSessionAsync(int sessionId)
    {
        var session = await dbContext.ChatSessions.FindAsync(sessionId);
        if (session != null && session.UserId == DefaultUserId)
        {
            dbContext.ChatSessions.Remove(session);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateSessionTitleAsync(int sessionId, string title)
    {
        var session = await dbContext.ChatSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Title = title;
            session.UpdatedDate = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }
    }

    static string NormalizeRole(string role)
    {
        return role?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
