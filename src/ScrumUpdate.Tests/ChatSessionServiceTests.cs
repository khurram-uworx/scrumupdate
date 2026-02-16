using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ScrumUpdate.Tests;

[TestFixture]
public class ChatSessionServiceTests
{
    ChatDbContext dbContext = null!;
    ChatSessionService sessionService = null!;

    [SetUp]
    public void Setup()
    {
        dbContext = TestDatabaseFixture.CreateTestDbContext();
        sessionService = new ChatSessionService(dbContext, new FakeCurrentUserContext());
    }

    [TearDown]
    public void TearDown()
    {
        dbContext.Dispose();
    }

    [Test]
    public async Task GetOrCreateSessionForScrumUpdateAsync_ReusesSessionForSameDate()
    {
        var scrumDate = new DateOnly(2026, 2, 15);

        var first = await sessionService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(scrumDate));
        var second = await sessionService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(scrumDate));

        Assert.That(first.Id, Is.EqualTo(second.Id));
        Assert.That(await dbContext.ChatSessions.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task GetOrCreateSessionForScrumUpdateAsync_UpdatesRichDataWithLatestGeneration()
    {
        var scrumDate = new DateOnly(2026, 2, 15);
        var first = new GeneratedScrumUpdate
        {
            ScrumDate = scrumDate,
            GeneratedTimeUtc = new DateTime(2026, 2, 15, 9, 0, 0, DateTimeKind.Utc),
            WhatIDidYesterday = "Finished login page.",
            WhatIPlanToDoToday = "Start scrum update flow.",
            Blocker = "No blocker."
        };
        var second = new GeneratedScrumUpdate
        {
            ScrumDate = scrumDate,
            GeneratedTimeUtc = new DateTime(2026, 2, 15, 9, 30, 0, DateTimeKind.Utc),
            WhatIDidYesterday = "Finished login page and bug fixes.",
            WhatIPlanToDoToday = "Finalize scrum update flow.",
            Blocker = "Waiting for API key."
        };

        var session = await sessionService.GetOrCreateSessionForScrumUpdateAsync(first);
        await sessionService.GetOrCreateSessionForScrumUpdateAsync(second);

        var loaded = await sessionService.GetSessionAsync(session.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.DayWiseScrumUpdate, Is.Not.Null);
        Assert.That(loaded.DayWiseScrumUpdate!.GeneratedTime, Is.EqualTo(second.GeneratedTimeUtc));
        Assert.That(loaded.DayWiseScrumUpdate.WhatIDidYesterday, Is.EqualTo(second.WhatIDidYesterday));
        Assert.That(loaded.DayWiseScrumUpdate.WhatIPlanToDoToday, Is.EqualTo(second.WhatIPlanToDoToday));
        Assert.That(loaded.DayWiseScrumUpdate.Blocker, Is.EqualTo(second.Blocker));
    }

    [Test]
    public async Task SaveSessionAsync_ReplacesExistingMessages()
    {
        var session = await sessionService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(new DateOnly(2026, 2, 15)));

        await sessionService.SaveSessionAsync(session.Id,
        [
            ("User", "First")
        ]);

        await sessionService.SaveSessionAsync(session.Id,
        [
            ("User", "Second"),
            ("Assistant", "Reply")
        ]);

        var saved = await sessionService.GetSessionAsync(session.Id);

        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Messages.Count, Is.EqualTo(2));
        Assert.That(saved.Messages.Select(m => (m.Role, m.Content)),
            Is.EqualTo(new[] { ("user", "Second"), ("assistant", "Reply") }));
    }

    [Test]
    public async Task GetSessionAsync_ReturnsMessagesOrderedByTimestamp()
    {
        var session = await sessionService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(new DateOnly(2026, 2, 15)));

        await sessionService.SaveMessageAsync(session.Id, "User", "First");
        await Task.Delay(10);
        await sessionService.SaveMessageAsync(session.Id, "Assistant", "Second");

        var loaded = await sessionService.GetSessionAsync(session.Id);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Messages.Count, Is.EqualTo(2));
        Assert.That(loaded.Messages.First().Role, Is.EqualTo("user"));
        Assert.That(loaded.Messages.First().Content, Is.EqualTo("First"));
        Assert.That(loaded.Messages.Last().Role, Is.EqualTo("assistant"));
        Assert.That(loaded.Messages.Last().Content, Is.EqualTo("Second"));
    }

    [Test]
    public async Task TwoSessions_KeepIndependentMessageHistoryWhenSwitchingDates()
    {
        var chat1 = await sessionService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(new DateOnly(2026, 2, 14)));
        await sessionService.SaveSessionAsync(chat1.Id,
        [
            ("User", "hi"),
            ("Assistant", "hello there")
        ]);

        var chat2 = await sessionService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(new DateOnly(2026, 2, 15)));
        await sessionService.SaveSessionAsync(chat2.Id,
        [
            ("User", "hey there"),
            ("Assistant", "hi again")
        ]);

        var loadedChat1 = await sessionService.GetSessionAsync(chat1.Id);
        var loadedChat2 = await sessionService.GetSessionAsync(chat2.Id);

        Assert.That(loadedChat1, Is.Not.Null);
        Assert.That(loadedChat2, Is.Not.Null);

        Assert.That(loadedChat1!.Messages.Select(m => (m.Role, m.Content)),
            Is.EqualTo(new[] { ("user", "hi"), ("assistant", "hello there") }));

        Assert.That(loadedChat2!.Messages.Select(m => (m.Role, m.Content)),
            Is.EqualTo(new[] { ("user", "hey there"), ("assistant", "hi again") }));
    }

    [Test]
    public async Task SameScrumDate_IsolatedAcrossDifferentUsers()
    {
        var userAService = new ChatSessionService(dbContext, new FakeCurrentUserContext("atlassian-user-a"));
        var userBService = new ChatSessionService(dbContext, new FakeCurrentUserContext("atlassian-user-b"));
        var scrumDate = new DateOnly(2026, 2, 15);

        var a = await userAService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(scrumDate));
        var b = await userBService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(scrumDate));

        Assert.That(a.Id, Is.Not.EqualTo(b.Id));
        Assert.That(await dbContext.ChatSessions.CountAsync(), Is.EqualTo(2));

        var sessionsForA = await userAService.GetSessionsAsync();
        var sessionsForB = await userBService.GetSessionsAsync();
        Assert.That(sessionsForA.Select(s => s.Id), Is.EqualTo(new[] { a.Id }));
        Assert.That(sessionsForB.Select(s => s.Id), Is.EqualTo(new[] { b.Id }));
    }

    [Test]
    public async Task UserCannotReadOrWriteAnotherUsersSession()
    {
        var userAService = new ChatSessionService(dbContext, new FakeCurrentUserContext("atlassian-user-a"));
        var userBService = new ChatSessionService(dbContext, new FakeCurrentUserContext("atlassian-user-b"));
        var session = await userAService.GetOrCreateSessionForScrumUpdateAsync(CreateScrumUpdate(new DateOnly(2026, 2, 15)));

        await userAService.SaveMessageAsync(session.Id, "user", "my private update");
        var fromUserB = await userBService.GetSessionAsync(session.Id);
        await userBService.SaveMessageAsync(session.Id, "assistant", "should not be saved");

        Assert.That(fromUserB, Is.Null);

        var fromUserA = await userAService.GetSessionAsync(session.Id);
        Assert.That(fromUserA, Is.Not.Null);
        Assert.That(fromUserA!.Messages.Count, Is.EqualTo(1));
        Assert.That(fromUserA.Messages.First().Content, Is.EqualTo("my private update"));
    }

    static GeneratedScrumUpdate CreateScrumUpdate(DateOnly scrumDate)
    {
        return new GeneratedScrumUpdate
        {
            ScrumDate = scrumDate,
            GeneratedTimeUtc = scrumDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            WhatIDidYesterday = "Worked on session persistence.",
            WhatIPlanToDoToday = "Add scrum update tagging.",
            Blocker = "No blocker."
        };
    }
}
