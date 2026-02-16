using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;

namespace ScrumUpdate.Tests;

[TestFixture]
public class ChatSessionWorkflowIntegrationTests
{
    ChatDbContext dbContext = null!;
    ChatSessionService sessionService = null!;
    TestChatWorkflow workflow = null!;

    [SetUp]
    public Task Setup()
    {
        dbContext = TestDatabaseFixture.CreateTestDbContext();
        sessionService = new ChatSessionService(dbContext, new FakeCurrentUserContext());
        workflow = new TestChatWorkflow(sessionService, new DummyChatClient());
        return Task.CompletedTask;
    }

    [TearDown]
    public void TearDown()
    {
        dbContext.Dispose();
    }

    [Test]
    public async Task NonScrumMessage_DoesNotCreateSession()
    {
        await workflow.SendAsync("hi");

        Assert.That(workflow.CurrentSessionId, Is.Null);
        Assert.That(await dbContext.ChatSessions.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ScrumThenRegenerate_AppendsMessagesAndKeepsLatestRichData()
    {
        await workflow.SendAsync("scrum update");
        var sessionId = workflow.CurrentSessionId;
        Assert.That(sessionId, Is.Not.Null);

        await workflow.SendAsync("regenerate");
        var loaded = await sessionService.GetSessionAsync(sessionId!.Value);
        var loadedMessages = loaded!.Messages.ToList();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loadedMessages.Count, Is.EqualTo(4));
        Assert.That(loadedMessages[1].Content, Does.StartWith("Scrum update for "));
        Assert.That(loadedMessages[3].Content, Does.StartWith("Scrum update for "));
        Assert.That(loadedMessages[3].Content, Is.Not.EqualTo(loadedMessages[1].Content));

        Assert.That(loaded.DayWiseScrumUpdate, Is.Not.Null);
        Assert.That(loadedMessages[3].Content, Does.Contain(loaded.DayWiseScrumUpdate!.WhatIDidYesterday));
        Assert.That(loadedMessages[3].Content, Does.Contain(loaded.DayWiseScrumUpdate.WhatIPlanToDoToday));
        Assert.That(loadedMessages[3].Content, Does.Contain(loaded.DayWiseScrumUpdate.Blocker));
    }

    sealed class TestChatWorkflow
    {
        readonly ChatSessionService sessionService;
        readonly DummyChatClient chatClient;
        readonly List<ChatMessage> messages = [];

        public int? CurrentSessionId { get; private set; }

        public TestChatWorkflow(ChatSessionService sessionService, DummyChatClient chatClient)
        {
            this.sessionService = sessionService;
            this.chatClient = chatClient;
        }

        public async Task SendAsync(string userText)
        {
            messages.Add(new ChatMessage(ChatRole.User, userText));

            var response = await chatClient.GetResponseAsync(messages);
            messages.Add(response.Messages.Single());

            var generatedScrumUpdate = chatClient.TryParseGeneratedScrumUpdateFromAssistantMessage(response.Messages.Single().Text ?? string.Empty);

            if (generatedScrumUpdate != null)
            {
                var session = await sessionService.GetOrCreateSessionForScrumUpdateAsync(generatedScrumUpdate);
                CurrentSessionId = session.Id;
                await sessionService.SaveSessionAsync(
                    session.Id,
                    messages
                        .Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant)
                        .Select(m => (m.Role.ToString(), m.Text ?? string.Empty)));
            }
        }
    }
}
