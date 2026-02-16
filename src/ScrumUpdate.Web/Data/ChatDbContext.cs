using Microsoft.EntityFrameworkCore;

namespace ScrumUpdate.Web.Data;

public class AppUser
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedDateUtc { get; set; }
    public ICollection<ChatSession> ChatSessions { get; set; } = [];
    public ICollection<ChatMessageEntity> ChatMessages { get; set; } = [];
    public ICollection<DayWiseScrumUpdate> DayWiseScrumUpdates { get; set; } = [];
    public ICollection<JiraOAuthToken> JiraOAuthTokens { get; set; } = [];
}

public class ChatMessageEntity
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public ChatSession ChatSession { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}

public class ChatSession
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateOnly ScrumDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
    public DayWiseScrumUpdate? DayWiseScrumUpdate { get; set; }
    public ICollection<ChatMessageEntity> Messages { get; set; } = [];
}

public class DayWiseScrumUpdate
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime GeneratedTime { get; set; }
    public string WhatIDidYesterday { get; set; } = string.Empty;
    public string WhatIPlanToDoToday { get; set; } = string.Empty;
    public string Blocker { get; set; } = string.Empty;
    public ChatSession ChatSession { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}

public class JiraOAuthToken
{
    public int Id { get; set; }
    public string LocalUserId { get; set; } = string.Empty;
    public string AuthenticatedUserId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string? CloudId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public AppUser User { get; set; } = null!;
}

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<DayWiseScrumUpdate> DayWiseScrumUpdates => Set<DayWiseScrumUpdate>();
    public DbSet<JiraOAuthToken> JiraOAuthTokens => Set<JiraOAuthToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).IsRequired();
            entity.Property(e => e.CreatedDateUtc).IsRequired();
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.HasOne<AppUser>()
                .WithMany(u => u.ChatSessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Messages).WithOne(m => m.ChatSession).HasForeignKey(m => m.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.DayWiseScrumUpdate)
                .WithOne(d => d.ChatSession)
                .HasForeignKey<DayWiseScrumUpdate>(d => d.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.ScrumDate }).IsUnique();
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.ChatMessages)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ChatSession).WithMany(s => s.Messages).HasForeignKey(e => e.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.ChatSessionId, e.Timestamp });
        });

        modelBuilder.Entity<DayWiseScrumUpdate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.WhatIDidYesterday).IsRequired();
            entity.Property(e => e.WhatIPlanToDoToday).IsRequired();
            entity.Property(e => e.Blocker).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.DayWiseScrumUpdates)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ChatSession)
                .WithOne(s => s.DayWiseScrumUpdate)
                .HasForeignKey<DayWiseScrumUpdate>(e => e.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ChatSessionId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.ChatSessionId }).IsUnique();
        });

        modelBuilder.Entity<JiraOAuthToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LocalUserId).IsRequired();
            entity.Property(e => e.AuthenticatedUserId).IsRequired();
            entity.Property(e => e.AccessToken).IsRequired();
            entity.Property(e => e.RefreshToken).IsRequired();
            entity.Property(e => e.Scope).IsRequired();
            entity.Property(e => e.AccessTokenExpiresAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.JiraOAuthTokens)
                .HasForeignKey(e => e.AuthenticatedUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.LocalUserId).IsUnique();
            entity.HasIndex(e => e.AuthenticatedUserId);
        });
    }
}
