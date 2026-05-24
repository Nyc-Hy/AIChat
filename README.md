# AIChat

AIChat 是一个面向 Vibe Coding 的跨平台代码编程助手。核心目标是让 DeepSeek、MiMo、MiniMAX 等模型在本地项目里获得接近 Claude Code / Claude Code Desktop 的编程协作体验：任务执行准确、快速、Token 消耗低，并尽可能命中稳定上下文缓存。

当前长期主线是跨平台 CLI/TUI + 后续 Web/Tauri Desktop。WPF 应用仍保留为 Windows 桌面入口，但不再是唯一产品形态。

本项目使用 [Apache License 2.0](LICENSE) 开源。

## 功能特性

- **跨平台 CLI**：Mac / Linux / Windows 均可运行核心 coding loop
- **项目级对话**：每个项目拥有独立的会话历史和设置
- **多模型提供商**：支持 OpenAI-compatible 和 Anthropic 协议
- **简化 Agent Loop**：默认单 Agent 工具循环，减少不必要的模型调用和 token 消耗
- **14 个内置工具**：文件读写编辑、搜索、补丁、Git、构建、测试、Shell
- **工具权限模型**：禁用、只读自动执行、每次确认、本会话允许
- **项目级权限覆盖**：每个项目可覆盖全局工具权限
- **Agent 运行历史**：浏览、筛选、重试和继续历史运行
- **验证系统**：可配置构建/测试命令；自动验证和自动修复默认关闭，可作为高级能力启用
- **上下文工程**：文件索引、预算化上下文包、固定上下文项
- **变更控制**：基于快照和哈希的冲突检测与安全回滚

## 跨平台 CLI

列出支持的模型：

```bash
dotnet run --project src/AIChat.Cli -- models
dotnet run --project src/AIChat.Cli -- models --provider deepseek
```

配置模型提供商：

```bash
dotnet run --project src/AIChat.Cli -- config set-provider --provider deepseek --api-key "$DEEPSEEK_API_KEY" --model deepseek-chat
dotnet run --project src/AIChat.Cli -- config show
dotnet run --project src/AIChat.Cli -- config list
dotnet run --project src/AIChat.Cli -- config use --provider deepseek --model deepseek-chat
```

初始化当前项目：

```bash
dotnet run --project src/AIChat.Cli -- init --project .
dotnet run --project src/AIChat.Cli -- projects list
```

执行一次代码任务：

```bash
dotnet run --project src/AIChat.Cli -- ask "解释这个模块的职责" --project .
dotnet run --project src/AIChat.Cli -- ask "修复 failing tests" --project . --mode standard --yes
dotnet run --project src/AIChat.Cli -- ask "分析复杂重构方案" --project . --mode deep
dotnet run --project src/AIChat.Cli -- doctor
```

进入交互式 TUI Beta：

```bash
dotnet run --project src/AIChat.Cli -- tui --project . --mode standard
```

TUI 内置命令：

```text
/help
/mode fast|standard|deep
/yes
/plain
/no-write
/verify
/status
/exit
```

默认情况下，写入、Shell、构建/测试和 Git mutation 工具不会自动批准；需要显式传入 `--yes`。

执行模式：

| 模式 | 用途 | 默认行为 |
|---|---|---|
| `fast` | 问答、小定位、小修 | 6 轮工具预算，不自动验证 |
| `standard` | 默认 coding loop | 16 轮工具预算，单 Agent |
| `deep` | 复杂任务 | 40 轮工具预算，启用 planner 和验证 |

模型策略：

- DeepSeek：工具 JSON 稳定化、thinking/reasoning 参数策略、修复任务提示。
- MiMo：长上下文项目理解、稳定前缀和低 token quick path。
- MiniMAX：interleaved thinking 策略、短 action loop、工具参数收敛。

## Windows 桌面应用

```powershell
dotnet run --project src\AIChat.App\AIChat.App.csproj
```

首次启动后，进入设置页，选择模型提供商模板，填写 API Key，并添加到已配置提供商列表。

## 测试

```powershell
dotnet build AIChat.sln --no-restore -m:1
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1
```

GitHub Actions 会在每个 Pull Request 上运行同样的构建和测试。

## Agent 模式

当当前模型支持工具调用时，AIChat 会进入 Agent 模式。默认产品路径是简化单 Agent loop：

1. 接收用户目标和项目上下文
2. 选择必要上下文
3. 调用工具读取、修改和验证代码
4. 输出变更、验证和下一步摘要
5. 记录运行结果，便于复盘

Planner、Sub-agent、Benchmark、Memory、Plugin/MCP、审计详情等高级能力保留在代码中，但默认不进入主路径。

### 工具权限

每个工具都有一个权限模式：

| 模式 | 行为 |
|---|---|
| Disabled | 不暴露给模型 |
| Auto ReadOnly | 只读工具无需确认自动执行 |
| Confirm Each Time | 每次调用都需要用户确认 |
| Allow for Session | 首次确认后，本轮会话内自动允许 |

全局默认值在设置页的 Tools 区域配置。项目级覆盖规则可在同一面板中添加。

### Agent 运行历史

Agent 运行会随会话一起持久化。历史面板可用于：

- 浏览历史运行，并按状态筛选
- 查看执行步骤、文件变更和验证结果
- 从头重试失败运行
- 继续已停止或未完成的运行
- 复制 review packet 用于分享或调试

### 验证与自动修复

可以为每个项目配置验证命令，例如 `dotnet build`、`dotnet test`。Agent 修改文件后：

1. 用户或高级配置可以触发验证命令
2. 如果验证失败，失败摘要可反馈给模型
3. 模型尝试修复问题
4. 自动修复默认关闭，避免小任务产生额外 token 和不可预测修改

## 架构

```text
src/
  AIChat.App/                  WPF Shell、MVVM 状态、组合根
  AIChat.Cli/                  跨平台 CLI 入口
  AIChat.Domain/               纯领域模型（聊天、项目、审计、上下文）
  AIChat.Abstractions/         跨边界契约和 DTO
  AIChat.Application/          Agent Harness、工具、提示词、上下文、验证
  AIChat.Providers.OpenAI/     OpenAI-compatible 协议适配器
  AIChat.Providers.Anthropic/  Anthropic 协议适配器
  AIChat.Storage.Json/         本地 JSON 持久化（%APPDATA%\AIChat）
tests/
  AIChat.Tests/                工具、Harness、Provider、序列化等单元测试
```

### 分层规则

1. UI (`AIChat.App`) 负责 MVVM 状态和应用组合，不承载业务逻辑。
2. Domain (`AIChat.Domain`) 只放纯模型，不依赖其他项目。
3. Application (`AIChat.Application`) 负责 Agent 循环、工具和提示词。
4. Providers (`AIChat.Providers.*`) 将具体模型协议适配到统一的 `IChatProvider`。
5. Storage (`AIChat.Storage.Json`) 将领域对象持久化到本地 JSON。

## 文档

- [产品范围](docs/PRODUCT_SCOPE.md)
- [上线计划](docs/LAUNCH_PLAN.md)
- [发布检查清单](docs/RELEASE_CHECKLIST.md)
- [架构说明](docs/ARCHITECTURE.md)
- [Agent Harness](docs/AGENT_HARNESS.md)
- [工具安全模型](docs/TOOL_SECURITY.md)
- [插件系统](docs/PLUGIN_SYSTEM.md)
- [GitHub 工作流](docs/GITHUB_WORKFLOW.md)
- [A2A Adapter 设计](docs/A2A_ADAPTER_DESIGN.md)
- [开发路线图](docs/REMAINING_DEVELOPMENT_PLAN.md)
- [安全策略](SECURITY.md)
- [变更日志](CHANGELOG.md)

## 贡献

使用 GitHub Issues 跟踪工作，使用 Pull Request 进行评审，CI 作为合并门禁。
请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，并遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。

## 贡献者

- Nyc-Hy：项目维护者
- CodeX：AI 编码协作者

## 发布

跨平台 CLI：

```bash
dotnet publish src/AIChat.Cli/AIChat.Cli.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish src/AIChat.Cli/AIChat.Cli.csproj -c Release -r linux-x64 --self-contained true
dotnet publish src/AIChat.Cli/AIChat.Cli.csproj -c Release -r win-x64 --self-contained true
```

生成三个平台的 zip 包：

```powershell
pwsh scripts/publish-cli.ps1
```

脚本会生成：

- `artifacts/release/aichat-cli-osx-arm64.zip`
- `artifacts/release/aichat-cli-linux-x64.zip`
- `artifacts/release/aichat-cli-win-x64.zip`
- `artifacts/release/SHA256SUMS.txt`

也可以在 GitHub Actions 中手动运行 `Release CLI` 工作流，或推送 `v*` tag 生成三个平台的 CLI artifacts。CLI 发布产物中的可执行文件名为 `aichat`（Windows 为 `aichat.exe`）。

Windows WPF：

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained false
```

该命令生成依赖 .NET 8 Runtime 的发布包。

如果需要自包含发布包：

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained true
```
