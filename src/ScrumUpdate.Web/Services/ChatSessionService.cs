using Microsoft.EntityFrameworkCore;
using ScrumUpdate.Web.Data;
using System.Text.Json;

namespace ScrumUpdate.Web.Services;

public class ChatSessionService
{
    static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.Web);

    readonly ChatDbContext dbContext;
    readonly ICurrentUserContext currentUserContext;

    public ChatSessionService(ChatDbContext dbContext, ICurrentUserContext currentUserContext)
    {
        this.dbContext = dbContext;
        this.currentUserContext = currentUserContext;
    }

    public async Task<ChatSession> GetOrCreateSessionForScrumUpdateAsync(GeneratedScrumUpdate scrumUpdate)
    {
        var userId = currentUserContext.GetRequiredUserId();
        await EnsureUserExistsAsync(userId);

        var existingSession = await dbContext.ChatSessions
            .Include(s => s.DayWiseScrumUpdate)
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ScrumDate == scrumUpdate.ScrumDate);

        if (existingSession != null)
        {
            if (existingSession.DayWiseScrumUpdate == null)
            {
                existingSession.DayWiseScrumUpdate = new DayWiseScrumUpdate
                {
                    UserId = userId,
                    GeneratedTime = scrumUpdate.GeneratedTimeUtc,
                    WhatIDidYesterday = scrumUpdate.WhatIDidYesterday,
                    WhatIPlanToDoToday = scrumUpdate.WhatIPlanToDoToday,
                    Blocker = scrumUpdate.Blocker
                };
            }
            else
            {
                existingSession.DayWiseScrumUpdate.UserId = userId;
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
            UserId = userId,
            Title = $"Scrum Update {scrumUpdate.ScrumDate:yyyy-MM-dd}",
            ScrumDate = scrumUpdate.ScrumDate,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            DayWiseScrumUpdate = new DayWiseScrumUpdate
            {
                UserId = userId,
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
        var userId = currentUserContext.GetRequiredUserId();

        return await dbContext.ChatSessions
            .Where(s => s.UserId == userId)
            .Include(s => s.DayWiseScrumUpdate)
            .OrderByDescending(s => s.UpdatedDate)
            .ToListAsync();
    }

    public async Task<ChatSession?> GetSessionAsync(int sessionId)
    {
        var userId = currentUserContext.GetRequiredUserId();

        var session = await dbContext.ChatSessions
            .Include(s => s.Messages)
            .Include(s => s.DayWiseScrumUpdate)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);

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

    public async Task SaveMessageAsync(int sessionId, string role, string content, ChatMessageMetadata? metadata = null)
    {
        var userId = currentUserContext.GetRequiredUserId();
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session == null)
        {
            return;
        }

        var message = new ChatMessageEntity
        {
            ChatSessionId = sessionId,
            UserId = userId,
            Role = NormalizeRole(role),
            Content = content,
            MetadataJson = SerializeMetadata(metadata),
            Timestamp = DateTime.UtcNow
        };

        dbContext.ChatMessages.Add(message);
        session.UpdatedDate = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
    }

    public async Task SaveSessionAsync(int sessionId, IEnumerable<(string Role, string Content)> messages)
    {
        await SaveSessionAsync(sessionId, messages.Select(m => (m.Role, m.Content, Metadata: (ChatMessageMetadata?)null)));
    }

    public async Task SaveSessionAsync(int sessionId, IEnumerable<(string Role, string Content, ChatMessageMetadata? Metadata)> messages)
    {
        var userId = currentUserContext.GetRequiredUserId();
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session == null) return;

        var existingMessages = dbContext.ChatMessages.Where(m => m.ChatSessionId == sessionId && m.UserId == userId);
        dbContext.ChatMessages.RemoveRange(existingMessages);

        foreach (var (role, content, metadata) in messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)))
        {
            var message = new ChatMessageEntity
            {
                ChatSessionId = sessionId,
                UserId = userId,
                Role = NormalizeRole(role),
                Content = content,
                MetadataJson = SerializeMetadata(metadata),
                Timestamp = DateTime.UtcNow
            };
            dbContext.ChatMessages.Add(message);
        }

        session.UpdatedDate = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteSessionAsync(int sessionId)
    {
        var userId = currentUserContext.GetRequiredUserId();
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session != null)
        {
            dbContext.ChatSessions.Remove(session);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateSessionTitleAsync(int sessionId, string title)
    {
        var userId = currentUserContext.GetRequiredUserId();
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session != null)
        {
            session.Title = title;
            session.UpdatedDate = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }
    }

    async Task EnsureUserExistsAsync(string userId)
    {
        var existing = await dbContext.AppUsers.AnyAsync(u => u.Id == userId);
        if (existing)
        {
            return;
        }

        dbContext.AppUsers.Add(new AppUser
        {
            Id = userId,
            CreatedDateUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    static string NormalizeRole(string role)
    {
        return role?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public static ChatMessageMetadata? DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChatMessageMetadata>(metadataJson, MetadataSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? SerializeMetadata(ChatMessageMetadata? metadata)
    {
        if (metadata == null)
        {
            return null;
        }

        return JsonSerializer.Serialize(metadata, MetadataSerializerOptions);
    }
}
