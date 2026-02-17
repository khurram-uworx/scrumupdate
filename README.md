Reimagining Enterprise / Business / Personal / Utility apps as conversationional UI with AI, System + Human coworking on Shared World Model
- Chat session (ChatGPT/Claude like) acting as Blackboard / Shared World Model

![The Blackbox Architecture](images/blackboard.png)

- Blackboard Architecture: A Classic AI Model for Collaborative Problem Solving
	- https://khurram-uworx.github.io/2026/02/15/Blackboard.html

# ScrumUpdate

Blazor chat app for generating day-wise scrum updates as PoC of Blackboard Architecture / Shared World Model

Current setup:
- .NET 10 Blazor Interactive Server
- EF Core InMemory database (dev only)
- `DummyChatClient` in Development
- Gemini (`Google.GenAI` via `IChatClient`) in non-Development environments

## Behavior

- Chat session is created only when a scrum update is generated.
- Scrum update is generated only when user message contains:
  - `scrum update`
  - `regenerate`
- `regenerate` creates a different scrum update message than the previous one.
- Generated scrum update is stored in two places:
  - Chat message history (all generated versions are preserved)
  - Rich session data (`DayWiseScrumUpdate`) where only the latest generated version is kept
- Sessions are unique by scrum date (`UserId + ScrumDate`).

## Data Model

- `ChatSession`
  - `ScrumDate`
  - `Messages`
  - `DayWiseScrumUpdate` (1:1)
- `DayWiseScrumUpdate`
  - `GeneratedTime`
  - `WhatIDidYesterday`
  - `WhatIPlanToDoToday`
  - `Blocker`

## Run

```powershell
dotnet run --project src/ScrumUpdate.Web
```

## Jira OAuth Integration

The app now supports Atlassian OAuth 2.0 (3LO) and Jira issue fetch endpoints.

Set credentials (recommended via user-secrets):

```powershell
dotnet user-secrets --project src/ScrumUpdate.Web set "AtlassianOAuth:ClientId" "<your-client-id>"
dotnet user-secrets --project src/ScrumUpdate.Web set "AtlassianOAuth:ClientSecret" "<your-client-secret>"
```

Atlassian app setup:
- Callback URL must include `https://localhost:<port>/auth/atlassian/callback`
- Required scopes:
  - `read:jira-work`
  - `read:jira-user`
  - `offline_access`

Implemented routes:
- `GET /auth/atlassian/login?returnUrl=/`
- `GET /auth/atlassian/callback`
- `GET /auth/atlassian/status`
- `GET /auth/atlassian/disconnect`

## Gemini Setup

Set API key (recommended via user-secrets):

```powershell
dotnet user-secrets --project src/ScrumUpdate.Web set "Gemini:ApiKey" "<your-google-api-key>"
```

Optional model override:

```powershell
dotnet user-secrets --project src/ScrumUpdate.Web set "Gemini:Model" "gemini-2.5-flash"
```

## Test

```powershell
dotnet test src/ScrumUpdate.Tests/ScrumUpdate.Tests.csproj
```
