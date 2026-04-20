# Copilot Instructions for ScrumUpdate

> **IMPORTANT**: Review `LEARNINGS.md` before making changes to state management or session flow.
>
> **Architecture baseline**: `README.md` describes product goals and integration setup.

## Project Context
- **Framework**: Blazor Interactive Server (`net10.0`)
- **Persistence**: EF Core InMemory provider (development-oriented)
- **AI abstraction**: `Microsoft.Extensions.AI` + `IChatClient`
- **Primary integrations**: Atlassian OAuth (Jira) + optional Gemini/Claude/OpenAI providers

## Current High-Level Structure

```text
src/ScrumUpdate.Web/
├── Components/
│   ├── Layout/MainLayout.razor
│   ├── ChatSessionList.razor
│   └── Pages/Chat/*
├── Services/
│   ├── ChatSessionService.cs
│   ├── JiraScrumUpdateDraftService.cs
│   ├── ScrumUpdateTools.cs
│   ├── ScrumGenerator.cs
│   ├── HttpCurrentUserContext.cs
│   └── Atlassian/*
├── Data/ChatDbContext.cs
└── Program.cs

src/ScrumUpdate.Tests/
└── NUnit tests for service/workflow behavior
```

## Architecture Notes (Current)
- There is **no ChatViewModel/ChatStateService event bus** in the current implementation.
- Session selection and new-chat coordination are handled by `MainLayout.razor` via cascading values (`CurrentSessionId`, `NewChatVersion`, `OnSessionChanged`).
- `Chat.razor` owns in-page conversation state, handles streaming responses, and persists session messages through `ChatSessionService`.
- Session persistence is keyed by `(UserId, ScrumDate)` through EF constraints and `GetOrCreateSessionForScrumUpdateAsync`.

## Service Responsibilities
- **`ChatSessionService`**: session CRUD, message persistence, metadata serialization/deserialization, and ensuring app user records exist.
- **`AtlassianOAuthService`**: OAuth handshake, token storage/refresh, Jira data retrieval for draft context.
- **`JiraScrumUpdateDraftService`**: maps Jira worklogs/comments/changelog into `GeneratedScrumUpdate` text fields.
- **`ScrumUpdateTools`**: exposes tool-callable methods to the model and captures generated drafts for reliable persistence metadata.
- **`HttpCurrentUserContext`**: maps local browser user to authenticated Jira user identity.

## Coding Conventions
- Private fields are `camelCase` (no underscore prefix).
- Public types/members are `PascalCase`.
- Keep async APIs truly async and pass `CancellationToken` through IO paths.
- Keep DB access inside `ChatSessionService`/domain services rather than directly in UI components.
- Avoid broad architectural rewrites unless explicitly requested.

## Blazor Conventions in This Repo
- Use `[Parameter]` and `EventCallback<T>` for component contracts.
- Use cascading parameters where the layout must coordinate state across multiple descendants.
- Cancel active streaming operations before session switching/reset flows.
- Keep system prompt/tool wiring in one place (`Chat.razor`) to avoid drift.

## Testing Guidance
- Add or update NUnit tests in `src/ScrumUpdate.Tests` for behavior changes.
- Prefer service-level and workflow-level tests when UI behavior is driven by persistence/session rules.
- Validate that message ordering and scrum-update persistence remain deterministic.

## Common Gotchas
- InMemory DB resets on restart.
- Missing Jira auth context will throw from `HttpCurrentUserContext`; layout should gate access first.
- Tool-captured scrum metadata is preferred; assistant-text parsing exists as fallback and should not be the primary path.
- Updating docs that reference removed files (for example `ChatViewModel`) is part of keeping contributor guidance accurate.

## Contributing to LEARNINGS.md
When a change reveals a recurring trap or ambiguous design area, add a concise entry to `LEARNINGS.md` with:
1. What went wrong / was unclear
2. Why it happened
3. How to avoid it next time
4. A minimal DO/DON'T example when useful
