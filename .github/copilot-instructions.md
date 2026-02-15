# Copilot Instructions for ScrumUpdate Chat Module

> **IMPORTANT**: Review `LEARNINGS.md` before making changes to state management or event handling. It documents critical design patterns and anti-patterns learned from previous issues.
> 
> **Architecture**: See `README.md` for complete system design, layering, and data flow documentation.

## Project Context
- **Framework**: Blazor with Interactive Server rendering (.NET 10)
- **Architecture**: MVVM with service-based state management
- **Database**: Entity Framework Core with in-memory provider
- **Goal**: Multi-session isolated chat UI where users can create, switch, and persist conversations

## File Organization

```
src/ScrumUpdate.Web/
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor
│   ├── Pages/Chat/
│   │   ├── Chat.razor
│   │   ├── ChatHeader.razor
│   │   └── ChatHeader.razor.css
│   ├── ChatSessionList.razor
│   ├── ChatMessageList.razor
│   ├── ChatInput.razor
│   └── ChatSuggestions.razor
├── ViewModels/
│   └── ChatViewModel.cs
├── Services/
│   ├── ChatSessionService.cs
│   ├── ChatStateService.cs
│   ├── ChatSessionManager.cs
│   └── DummyChatClient.cs
├── Data/
│   └── ChatDbContext.cs
└── Program.cs
```

## Key Concepts

### ChatStateService (Event Bus)
- **Shared service** injected into MainLayout and Chat components
- **Events**: `OnNewChatRequested`, `OnSessionSelected`, `OnSessionCreated`
- **Pattern**: Used for cross-component communication

### ChatViewModel (Business Logic)
- **Scoped service** - one instance per component
- **Public Properties**: `Messages`, `CurrentSession`, `CurrentResponseMessage`
- **Public Events**: `OnMessagesChanged`, `OnResponseMessageChanged`, `OnSessionChanged`
- **Responsibilities**: Message management, session switching, persistence, streaming

### Session Switching Pattern
```csharp
// CURRENT FLOW (has issues):
1. User clicks session in sidebar
2. ChatSessionList → MainLayout.OnSessionSelected() updates currentSessionId
3. ChatStateService.NotifySessionSelected() fires
4. Chat.OnLoadSessionAsync() called via event
5. ChatViewModel.LoadSessionAsync() loads from DB
// Problem: MainLayout.currentSessionId != ChatViewModel._currentSession synchronization

// DESIRED FLOW:
1-4. Same as above
5. ChatViewModel updates and notifies via event
6. MainLayout subscribes and updates currentSessionId
```

## Current Issues
See **LEARNINGS.md** for documented patterns and anti-patterns from previous issues. Review before making changes to state management or event handling.

## Coding Conventions Used

### Unique to This Codebase
- **Private fields**: `camelCase` without underscore prefix (no `_fieldName` pattern)
  - ✅ `int sessionCount;` not `int _sessionCount;`
  - Rationale: Leverage C# case-sensitivity; underscore prefix is noise
- **Event naming**: `On{Event}Async` for async event handlers

### Standard C# Patterns Applied
- **Public members**: `PascalCase` (properties, methods, events, classes)
- **Local variables & parameters**: `camelCase`
- **Null coalescing**: `?? throw new ArgumentNullException()`
- **Observable pattern**: Properties + Events for UI binding
- **Disposal**: `IAsyncDisposable` for cleanup

### Blazor Component Conventions
- **Parameters**: `[Parameter]` for parent-child input
- **Callbacks**: `EventCallback<T>` for child-to-parent events
- **Lifecycle**: `OnInitializedAsync()` for setup, unsubscribe in `DisposeAsync()`
- **Async/await**: All DB operations return `Task`/`Task<T>`, streaming uses `await foreach` with `CancellationToken`

## When Modifying Code

1. **Session state changes**: Must update both MainLayout and ChatViewModel
2. **New service methods**: Add corresponding ChatStateService events
3. **UI components**: Use event callbacks, avoid direct service calls
4. **Database operations**: Use ChatSessionService, not DbContext directly
5. **Streaming operations**: Always respect CancellationToken

## Testing
- See `src/ScrumUpdate.Tests/ChatViewModelIntegrationTests.cs`
- ViewModel is fully testable without UI dependencies
- Services use dependency injection for mockability

## Common Gotchas
- **In-memory database**: Data lost on app restart (OK for dev)
- **Streaming responses**: Must be cancelled before switching sessions
- **Event subscriptions**: Always unsubscribe in disposal to avoid memory leaks
- **StateHasChanged()**: Called automatically on event fires; manual calls only when needed
- **null! suppression**: Used when type is guaranteed non-null at runtime

## Contributing to LEARNINGS.md

**Important**: When you encounter challenges, ambiguities, or make mistakes while working with this codebase:

1. **Document what you learned** in `LEARNINGS.md` under an appropriate section
2. **Add it as a "Anti-Pattern to Avoid"** or create a new "Specific Code Pattern" section
3. **Include**:
   - What went wrong / what was confusing
   - Why it happened (root cause)
   - How to avoid it next time
   - Example code if applicable

4. **Format**: Keep it succinct and scannable, use bullet points and code blocks

5. **Goal**: Each Copilot agent session learns from previous mistakes, reducing iteration cycles

**Example of what to add**:
```markdown
## Common Mistake: Forgetting to Notify ChatStateService

When adding a new session operation, it's easy to forget to fire the corresponding event.

✅ **DO**: 
```csharp
private async Task MyNewSessionOp() {
    // ... business logic ...
    await _chatStateService.NotifySessionCreated(id);  // ← Always notify
}
```
✗ **DON'T**: Implement operation without any event notification
```

