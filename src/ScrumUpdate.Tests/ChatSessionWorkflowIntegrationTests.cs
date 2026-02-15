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
    public async Task Setup()
    {
        dbContext = TestDatabaseFixture.CreateTestDbContext();
        sessionService = new ChatSessionService(dbContext);
        workflow = new TestChatWorkflow(sessionService, new DummyChatClient());
        await workflow.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        dbContext.Dispose();
    }

    [Test]
    public async Task SessionSwitching_RestoresEachSessionConversation()
    {
        var chat1Id = workflow.CurrentSessionId;
        await workflow.SendAsync("hi");

        await workflow.CreateNewChatAsync();
        var chat2Id = workflow.CurrentSessionId;
        await workflow.SendAsync("hey there");

        await workflow.LoadSessionAsync(chat1Id);
        var chat1Texts = workflow.UserAssistantMessages.Select(m => m.Text).ToArray();
        Assert.That(chat1Texts,
            Is.EqualTo(new[] { "hi", "I am dummy AI and don't know how to respond" }));

        await workflow.LoadSessionAsync(chat2Id);
        var chat2Texts = workflow.UserAssistantMessages.Select(m => m.Text).ToArray();
        Assert.That(chat2Texts,
            Is.EqualTo(new[] { "hey there", "I am dummy AI and don't know how to respond" }));
    }

    sealed class TestChatWorkflow
    {
        const string SystemPrompt = "You are a helpful assistant.";

        readonly ChatSessionService sessionService;
        readonly IChatClient chatClient;
        readonly ChatOptions chatOptions = new();
        readonly List<ChatMessage> messages = [];

        ChatSession? currentSession;

        public int CurrentSessionId => currentSession?.Id ?? throw new InvalidOperationException("No active session.");

        public IReadOnlyList<ChatMessage> UserAssistantMessages =>
            messages.Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant).ToList();

        public TestChatWorkflow(ChatSessionService sessionService, IChatClient chatClient)
        {
            this.sessionService = sessionService;
            this.chatClient = chatClient;
        }

        public async Task InitializeAsync()
        {
            messages.Clear();
            messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
            currentSession = await sessionService.CreateSessionAsync();
        }

        public async Task SendAsync(string userText)
        {
            messages.Add(new ChatMessage(ChatRole.User, userText));

            var responseText = string.Empty;
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
            {
                responseText += update.Text;
            }

            messages.Add(new ChatMessage(ChatRole.Assistant, responseText));
            await SaveCurrentAsync();
        }

        public async Task CreateNewChatAsync()
        {
            await SaveCurrentAsync();
            messages.Clear();
            messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
            currentSession = await sessionService.CreateSessionAsync();
            chatOptions.ConversationId = null;
        }

        public async Task LoadSessionAsync(int sessionId)
        {
            await SaveCurrentAsync();

            var session = await sessionService.GetSessionAsync(sessionId);
            if (session == null)
            {
                throw new InvalidOperationException($"Session {sessionId} not found.");
            }

            currentSession = session;
            messages.Clear();
            messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));

            foreach (var stored in session.Messages.OrderBy(m => m.Timestamp).ThenBy(m => m.Id))
            {
                var role = stored.Role.ToLowerInvariant() switch
                {
                    "user" => ChatRole.User,
                    "assistant" => ChatRole.Assistant,
                    _ => ChatRole.System
                };

                messages.Add(new ChatMessage(role, stored.Content));
            }
        }

        async Task SaveCurrentAsync()
        {
            if (currentSession == null)
            {
                return;
            }

            var toPersist = messages
                .Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant)
                .Select(m => (m.Role.ToString(), m.Text ?? string.Empty))
                .Where(m => !string.IsNullOrWhiteSpace(m.Item2))
                .ToList();

            if (toPersist.Count == 0)
            {
                return;
            }

            await sessionService.SaveSessionAsync(currentSession.Id, toPersist);
        }
    }
}
