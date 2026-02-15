using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;

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
        sessionService = new ChatSessionService(dbContext);
    }

    [TearDown]
    public void TearDown()
    {
        dbContext.Dispose();
    }

    [Test]
    public async Task CreateSessionAsync_IncrementsSessionTitle()
    {
        var first = await sessionService.CreateSessionAsync();
        var second = await sessionService.CreateSessionAsync();

        Assert.That(first.Title, Is.EqualTo("Chat 1"));
        Assert.That(second.Title, Is.EqualTo("Chat 2"));
    }

    [Test]
    public async Task SaveSessionAsync_ReplacesExistingMessages()
    {
        var session = await sessionService.CreateSessionAsync();

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
        var session = await sessionService.CreateSessionAsync();

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
    public async Task TwoSessions_KeepIndependentMessageHistoryWhenSwitching()
    {
        var chat1 = await sessionService.CreateSessionAsync();
        await sessionService.SaveSessionAsync(chat1.Id,
        [
            ("User", "hi"),
            ("Assistant", "hello there")
        ]);

        var chat2 = await sessionService.CreateSessionAsync();
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
}
