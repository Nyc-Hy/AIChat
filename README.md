# AIChat

AIChat is a .NET WPF desktop MVP for project-scoped LLM conversations.

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

## Run

```powershell
dotnet run --project src\AIChat.App\AIChat.App.csproj
```

On first launch, open Settings, choose 小米 MIMO (TokenPlan), enter the API key, and add it to the configured provider list.

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
```

The first version intentionally avoids file editing, command execution, plugins, and agent tools. Those can be added behind the existing abstractions without changing the main product shape.
