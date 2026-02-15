# Learnings: Multi-Session Chat Architecture Issues

**This is a living document and learning ledger.** Each Copilot agent session should:
1. Review this document before making changes to state management or event handling
2. Add new learnings when encountering challenges, mistakes, or ambiguities
3. Keep entries succinct with clear "DO" and "DON'T" examples
4. Focus on patterns that will help future agents avoid the same pitfalls

See `.github/copilot-instructions.md` for contribution guidelines.

---

## Root Cause Analysis

### Why Did These Issues Occur?

#### 1. **Incomplete Event-Driven Architecture**
- **What Happened**: ChatStateService defined events (`OnSessionCreated`, `OnSessionSelected`, `OnNewChatRequested`) but the business logic never fired them.
- **Why**: The service interface was designed without ensuring all call sites would invoke the notifications.
- **Impact**: Components couldn't react to state changes because events were silent.
- **Lesson**: Design event contracts explicitly, then audit all mutation points to ensure they fire events.

#### 2. **Missing Dependency Injection**
- **What Happened**: ChatViewModel was created in Chat.razor without ChatStateService injected.
- **Why**: During initial design, ChatStateService was conceptual; the ViewModel didn't have a hard requirement.
- **Impact**: ViewModel couldn't notify the service layer of state changes.
- **Lesson**: If a service defines events, all classes that can trigger those events must have the service injected with compile-time enforcement.

#### 3. **Multiple Sources of Truth**
- **What Happened**: 
  - `MainLayout.currentSessionId` tracked active session
  - `ChatViewModel._currentSession` tracked active session
  - `ChatSessionList.CurrentSessionId` parameter also tracked it
- **Why**: No explicit single source of truth established during architecture phase.
- **Impact**: Race conditions, state divergence, sidebar out of sync.
- **Lesson**: Define SOT (single source of truth) explicitly in architecture docs. In MVC/MVVM, it should be the ViewModel layer.

#### 4. **Lack of Explicit Data Flow**
- **What Happened**: 
  - Components knew how to read state but not how to communicate state changes
  - Event subscriptions were partial (Chat subscribed to ViewModels events, but MainLayout didn't)
- **Why**: No documented contract for "when session changes, notify here"
- **Impact**: Components acted independently, creating inconsistent state
- **Lesson**: Document the exact data flow path: Action → ViewModel → Service Event → UI Update

#### 5. **Test-First Implementation Gap**
- **What Happened**: Tests passed, but integration between components was broken
- **Why**: Tests only validated ViewModel in isolation, not the component→ViewModel→ service→component cycle
- **Impact**: Component synchronization bugs weren't caught until manual testing
- **Lesson**: Integration tests should verify the full event cycle (user action → event fires → all subscribers receive it)

---

## Key Takeaways

### Pattern: Event Bus for Multi-Component Communication

**When building multi-component Blazor UIs that share state:**

1. **Design the event contract FIRST** - Define all events that indicate state mutations
2. **Enforce in code IMMEDIATELY** - Use DI to inject the service where it's needed
3. **Audit all mutation points** - Every place that changes shared state must call `Notify*()`
4. **Subscribe comprehensively** - All components that care about state must listen
5. **Test the integration** - Unit tests validate logic; integration tests validate events fire and are handled

✅ **Pattern**: Mutation → Event → All Subscribers Notified → UI Updates

### Anti-Patterns to Avoid

**❌ Incomplete Event Implementation**: Define event but don't fire it everywhere, or fire it with no listeners.
- **✓ Fix**: Audit all mutation points and verify all call `Notify*()`.

**❌ Multiple Sources of Truth**: Component A thinks session is 1, Component B thinks it's 2.
- **✓ Fix**: Single source in ViewModel layer, propagate via events to UI.

**❌ Partial Event Subscriptions**: Some components listen to ViewModel events, others to service events, others manage their own state.
- **✓ Fix**: All components listen to same event bus (ChatStateService).

**❌ Unidirectional Data Flow**: Parent tells child "session changed" but child doesn't tell parent it finished loading.
- **✓ Fix**: Bidirectional via events (child notifies completion).

## Specific Code Patterns

### ✅ CORRECT: Full Event Cycle

**ViewModel fires event when state changes:**
```csharp
async Task CreateNewSessionAsync()
{
    currentSession = await sessionService.CreateSessionAsync();
    await chatStateService.NotifySessionCreated(currentSession.Id);  // Always fire
}
```

**UI component subscribes in OnInitialized & unsubscribes in Dispose:**
```csharp
protected override async Task OnInitializedAsync()
{
    ChatStateService.OnSessionCreated += OnSessionCreatedAsync;
}

async ValueTask IAsyncDisposable.DisposeAsync()
{
    ChatStateService.OnSessionCreated -= OnSessionCreatedAsync;
}
```

**All subscribers receive the event:**
```
CreateNewChat() 
  → ChatViewModel.CreateNewSessionAsync()
    → await _chatStateService.NotifySessionCreated(id)
      → MainLayout.OnSessionCreatedAsync()
      → ChatSessionList.HandleSessionCreated()
```

## Architecture Decision: Event Bus Over Direct References

### Why Event Bus Pattern?
```csharp
// ✓ CORRECT: Event-driven via service
ChatStateService.NotifySessionSelected(sessionId);
// MainLayout and Chat both listen, no direct coupling
```

**Benefits**:
- Loose coupling through event contract
- Components don't know about each other
- Easy to add new subscribers
- Easy to test (mock the events)
- Declarative intent ("session selected" not "call these methods")

**Alternative anti-pattern** (❌ Wrong):
```csharp
// Direct coupling - don't do this
Chat chat = GetComponentReference<Chat>();
await chat.UpdateSession(sessionId);
```

## Design Checklist for Event-Driven Blazor

- [ ] **Define Events**: List all state mutations (create, delete, update, select, etc.)
- [ ] **Define Subscribers**: Who needs to know about each state change?
- [ ] **Inject Service**: Does the change originator have the service injected?
- [ ] **Fire Events**: Audit all mutation points, verify all call `Notify*()`
- [ ] **Subscribe**: Audit all interested components, verify all subscribe in `OnInitialized`
- [ ] **Unsubscribe**: Audit all subscribers, verify all unsubscribe in `Dispose`
- [ ] **Test Integration**: Test the full cycle (action → event → subscribers notified)

---

## References

- See `.github/copilot-instructions.md` for coding conventions and patterns
- Review code comments in `ChatViewModel.cs`, `Chat.razor`, and `MainLayout.razor`

