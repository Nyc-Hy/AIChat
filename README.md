# AIChat

AIChat is a .NET WPF desktop MVP for project-scoped LLM conversations and early code-agent workflows.

## MVP scope

- Project list with conversations nested under each project
- Conversation history isolated per project
- Custom-drawn WPF window with flat rounded modern UI
- OpenAI-compatible protocol implementation
- Anthropic protocol implementation
- Provider routing abstraction for provider-specific customization
- Fixed provider/model selection catalog for model-specific customization
- Configured model providers; users add the 小米 MIMO (TokenPlan) template by entering only the API key
- Local JSON persistence under `%APPDATA%\AIChat`
- Settings window for provider, base URL, API key, model, tool selection, and model context limit
- Internal code-agent default temperature set to `0.3`
- Combined context usage ring in the composer area
- No demo conversations or sample messages on first launch
- Agent loop that can expose enabled tools to compatible models and feed tool results back into the transcript
- Project-scoped tools for listing files, reading files, searching text, writing files, editing files, and running guarded shell commands
- Tool permission modes for disabled, read-only auto execution, per-call confirmation, and session approval
- System prompt and conversation context builders that keep model instructions centralized and reserve context headroom
- xUnit test project covering prompt/context behavior and high-risk tool safety checks

## Run

```powershell
dotnet run --project src\AIChat.App\AIChat.App.csproj
```

On first launch, open Settings, choose 小米 MIMO (TokenPlan), enter the API key, and add it to the configured provider list.

## Test

```powershell
dotnet test AIChat.sln
```

## Architecture

```text
src/
  AIChat.App/                  WPF shell, MVVM state, custom controls, composition root
  AIChat.Domain/               Pure domain models: chat, projects, context usage
  AIChat.Abstractions/         Contracts and DTOs used across boundaries
  AIChat.Application/          Application-level routing and context estimation
  AIChat.Providers.OpenAI/     OpenAI-compatible protocol implementation
  AIChat.Providers.Anthropic/  Anthropic protocol implementation
  AIChat.Storage.Json/         Local JSON repository implementation
tests/
  AIChat.Tests/                Unit tests for application services and agent tools
```

The current version has the first agent slice wired in. It is still conservative: tools are project-scoped, write/shell actions are guarded, and the next major work is improving observability and expanding tests before adding broader automation.
