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

    public async Task<ChatSession> GetOrCreateSessionForScrumUpdateAsync(GeneratedScrumUpdate scrumUpdate)
    {
        var existingSession = await dbContext.ChatSessions
            .Include(s => s.DayWiseScrumUpdate)
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.UserId == DefaultUserId && s.ScrumDate == scrumUpdate.ScrumDate);

        if (existingSession != null)
        {
            if (existingSession.DayWiseScrumUpdate == null)
            {
                existingSession.DayWiseScrumUpdate = new DayWiseScrumUpdate
                {
                    GeneratedTime = scrumUpdate.GeneratedTimeUtc,
                    WhatIDidYesterday = scrumUpdate.WhatIDidYesterday,
                    WhatIPlanToDoToday = scrumUpdate.WhatIPlanToDoToday,
                    Blocker = scrumUpdate.Blocker
                };
            }
            else
            {
                existingSession.DayWiseScrumUpdate.GeneratedTime = scrumUpdate.GeneratedTimeUtc;
                existingSession.DayWiseScrumUpdate.WhatIDidYesterday = scrumUpdate.WhatIDidYesterday;
                existingSession.DayWiseScrumUpdate.WhatIPlanToDoToday = scrumUpdate.WhatIPlanToDoToday;
                existingSession.DayWiseScrumUpdate.Blocker = scrumUpdate.Blocker;
            }

            existingSession.UpdatedDate = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
            return existingSession;
        }

        var session = new ChatSession
        {
            UserId = DefaultUserId,
            Title = $"Scrum Update {scrumUpdate.ScrumDate:yyyy-MM-dd}",
            ScrumDate = scrumUpdate.ScrumDate,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            DayWiseScrumUpdate = new DayWiseScrumUpdate
            {
                GeneratedTime = scrumUpdate.GeneratedTimeUtc,
                WhatIDidYesterday = scrumUpdate.WhatIDidYesterday,
                WhatIPlanToDoToday = scrumUpdate.WhatIPlanToDoToday,
                Blocker = scrumUpdate.Blocker
            }
        };

        dbContext.ChatSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return session;
    }

    public async Task<List<ChatSession>> GetSessionsAsync()
    {
        return await dbContext.ChatSessions
            .Where(s => s.UserId == DefaultUserId)
            .Include(s => s.DayWiseScrumUpdate)
            .OrderByDescending(s => s.UpdatedDate)
            .ToListAsync();
    }

    public async Task<ChatSession?> GetSessionAsync(int sessionId)
    {
        var session = await dbContext.ChatSessions
            .Include(s => s.Messages)
            .Include(s => s.DayWiseScrumUpdate)
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
