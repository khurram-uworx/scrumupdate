using Microsoft.EntityFrameworkCore;

namespace ScrumUpdate.Web.Data;

public class ChatMessageEntity
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public ChatSession ChatSession { get; set; } = null!;
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
    public DateTime GeneratedTime { get; set; }
    public string WhatIDidYesterday { get; set; } = string.Empty;
    public string WhatIPlanToDoToday { get; set; } = string.Empty;
    public string Blocker { get; set; } = string.Empty;
    public ChatSession ChatSession { get; set; } = null!;
}

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<DayWiseScrumUpdate> DayWiseScrumUpdates => Set<DayWiseScrumUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Title).IsRequired();
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
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.HasOne(e => e.ChatSession).WithMany(s => s.Messages).HasForeignKey(e => e.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DayWiseScrumUpdate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WhatIDidYesterday).IsRequired();
            entity.Property(e => e.WhatIPlanToDoToday).IsRequired();
            entity.Property(e => e.Blocker).IsRequired();
            entity.HasOne(e => e.ChatSession)
                .WithOne(s => s.DayWiseScrumUpdate)
                .HasForeignKey<DayWiseScrumUpdate>(e => e.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ChatSessionId).IsUnique();
        });
    }
}
