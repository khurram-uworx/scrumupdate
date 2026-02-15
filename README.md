# ScrumUpdate Chat Module

Multi-session Blazor chat with event-driven state management. MVVM + ChatStateService event bus for loose component coupling.

**Tech Stack**: Blazor Interactive Server (.NET 10), EF Core (in-memory), 20 integration tests

**Run**: `dotnet run --project src/ScrumUpdate.Web` | **Test**: `dotnet test`

## Architecture

### Layering

| Layer | Classes | Responsibility |
|-------|---------|-----------------|
| **UI** | MainLayout, Chat, ChatSessionList | Render state, subscribe to events |
| **ViewModel** | ChatViewModel | Owns Messages, CurrentSession; fires local events |
| **Service** | ChatStateService, ChatSessionService | Cross-component events, DB ops |
| **Data** | ChatDbContext, ChatSession, ChatMessageEntity | Persistence |

### Single Source of Truth

**ChatViewModel** owns mutable state:
```csharp
ChatSession? currentSession;
List<ChatMessage> messages;
public ChatSession? CurrentSession => currentSession;
public IReadOnlyList<ChatMessage> Messages => messages.AsReadOnly();
public event Action? OnMessagesChanged, OnSessionChanged;
```

### Event Bus: ChatStateService

Coordinates state changes across components:

| Event | Fired By | Subscribed By | Trigger |
|-------|----------|---------------|---------|
| `OnSessionCreated` | ChatViewModel | MainLayout, ChatSessionList | New session created |
| `OnSessionSelected` | ChatViewModel | MainLayout | Session switched |
| `OnNewChatRequested` | Chat | MainLayout | New chat initiated |

**Pattern**: ViewModel → ChatStateService → Components → StateHasChanged()

### Data Flows

**Session Creation**:
```
ChatHeader.OnNewChat() → Chat.OnNewChatAsync() 
→ ChatViewModel.CreateNewChatAsync() → ChatSessionService.CreateSessionAsync() 
→ ChatStateService.NotifySessionCreated(id) → MainLayout/ChatSessionList re-render
```

**Session Switching**:
```
ChatSessionList click → MainLayout.OnSessionSelected(id) 
→ ChatStateService.NotifySessionSelected(id) → Chat.OnLoadSessionAsync(id) 
→ ChatViewModel.LoadSessionAsync(id) → ChatSessionService.GetSessionAsync(id) 
→ Load messages → MainLayout/ChatSessionList re-render
```

### File Structure
```
src/ScrumUpdate.Web/
├── Components/Layout/MainLayout.razor
├── Components/Pages/Chat/Chat.razor
├── Components/{ChatSessionList,MessageList,Input,Suggestions}.razor
├── ViewModels/ChatViewModel.cs
├── Services/{ChatStateService,ChatSessionService,ChatSessionManager}.cs
├── Data/ChatDbContext.cs
└── Program.cs
```

## Design Decisions

**Event Bus over component coupling**: Loose coupling, scalable subscriptions, testable, decoupled from UI

**ViewModel fires service events**: Ensures all mutations notify immediately via single entry point

**Scoped ChatStateService**: One instance per component tree; scoped ChatViewModel per page

## Key Patterns

### Subscription/Cleanup
```csharp
protected override async Task OnInitializedAsync() {
    ChatStateService.OnSessionCreated += OnSessionCreatedAsync;  // Subscribe
    await viewModel.InitializeAsync();
}

async ValueTask IAsyncDisposable.DisposeAsync() {
    ChatStateService.OnSessionCreated -= OnSessionCreatedAsync;  // Unsubscribe
}
```

### Streaming with Cancellation
```csharp
CancellationTokenSource? responseCancel;

public async Task AddUserMessageAndGetResponseAsync(ChatMessage msg) {
    responseCancel = new();
    await foreach (var update in _chatClient.GetStreamingResponseAsync(..., responseCancel.Token)) {
        currentResponseMessage.Content += update.Text;
        NotifyResponseMessageChanged();
    }
}

public void CancelResponse() => _responseCancel?.Cancel();  // Cancel on session switch
```

### Safe Session Switching
```csharp
public async Task LoadSessionAsync(int sessionId) {
    CancelAnyCurrentResponse();       // ← Cancel before loading
    await SaveCurrentSessionAsync();  // ← Save state
    var session = await _sessionService.GetSessionAsync(sessionId);
    // ... load new session
    await _chatStateService.NotifySessionSelected(sessionId);
}
```

## Database Schema
```csharp
public class ChatSession {
    public int Id { get; set; }
    public string UserId { get; set; }
    public string Title { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
    public ICollection<ChatMessageEntity> Messages { get; set; }
}

public class ChatMessageEntity {
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string Role { get; set; }  // "User", "Assistant", "System"
    public string Content { get; set; }
    public DateTime Timestamp { get; set; }
}
```

## Configuration

**DI Setup** (Program.cs):
```csharp
builder.Services.AddScoped<ChatStateService>();
builder.Services.AddScoped<ChatSessionService>();
IChatClient chatClient = new DummyChatClient();
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseInMemoryDatabase("ChatDatabase"));
```

**Replace AI Client**:
- **Ollama**: `new OllamaApiClient(new Uri("http://localhost:11434"), "llama2")`
- **Azure OpenAI**: `new AzureOpenAIClient(endpoint, credentials).AsChatClient("gpt-4")`

**Upgrade DB** (from in-memory to SQL):
```csharp
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlServer(config["ConnectionStrings:DefaultConnection"]));
// Then: dotnet ef migrations add InitialCreate && dotnet ef database update
```

## Tests
20 integration tests in `src/ScrumUpdate.Tests/ChatViewModelIntegrationTests.cs`:
- Session CRUD, switching, message isolation
- Persistence, event notifications, streaming cancellation

```powershell
dotnet test
```

## Documentation
- **LEARNINGS.md** - Design decisions, anti-patterns, learned lessons
- **ISSUES.md** - Issue tracker with resolution notes
- **.github/copilot-instructions.md** - Code conventions & patterns
