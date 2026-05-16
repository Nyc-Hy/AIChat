# AIChat 开发路线图

本文档是 AIChat 当前的规划入口。它取代了早期按阶段交接的旧说明；那些说明在早期开发中有帮助，但在优化工作完成后已经不再准确。

## 当前状态

AIChat 是一个基于 .NET 8 / WPF 的桌面应用，用于项目级 LLM 对话和本地代码 Agent 工作流。

目前稳定基础包括：

- WPF 桌面 Shell、MVVM、项目级会话、设置和持久化运行历史。
- OpenAI-compatible 和 Anthropic Provider 适配器，包括 tool-call 请求/响应处理。
- Agent Harness，支持模型/工具循环、规划、执行、验证、自动修复、重试和继续。
- 内置工具：文件读写编辑、搜索、补丁、Git 操作、构建/测试和 Shell 执行。
- 工具权限、项目级覆盖、审批流程、Shell 安全检查和项目路径保护。
- JSON 持久化、JSONL 审计日志、运行详情中的审计展示，以及可复制的 review packet。
- 项目文件索引、预算化上下文包、固定上下文项和文件类型摘要。
- 基于快照/哈希的文件变更跟踪，用于冲突感知回滚。
- 版本展示和发布说明。

## 维护优先级

在开始大型功能前，优先处理以下事项：

| 优先级 | 区域 | 目标 |
|---|---|---|
| 高 | GitHub 工作流 | 使用 Issues 跟踪计划，通过聚焦 PR 评审，并在合并前保持 CI 通过。 |
| 高 | `MainViewModel` 体积 | 继续把纯工作区、审计和 Agent 运行逻辑拆到小服务中。 |
| 高 | 测试覆盖 | 覆盖 Provider 协议解析、工具审批、审计一致性和工作区安全。 |
| 中 | 上下文质量 | 提升相关性评分、最近文件选择和增量索引，避免增加提示词噪声。 |
| 中 | 可观测性 | 通过更清晰的运行摘要、审计分组和验证输出，让 Agent 失败更容易排查。 |
| 中低 | 打包 | 在 framework-dependent 发布路径稳定后，改进安装包和发布体验。 |

## 未来功能

这些是较大的工作，应扩展现有 Harness、权限、审计和验证系统，而不是绕过它们。

### MCP Client 集成

在 `IExternalToolProvider` 后实现 `McpToolProvider`，让 AIChat 可以发现并使用外部 MCP Server 提供的工具。外部工具调用仍必须经过正常审批和审计链路。

### A2A Server

将 AIChat 暴露为可被外部系统调用的 Agent。入站请求必须使用与交互式运行相同的 Harness、权限模型、路径保护、审计日志和验证循环。详见 [A2A Adapter 设计](A2A_ADAPTER_DESIGN.md)。

### 多 Agent 队列

扩展当前单运行队列，支持排队或并发 Agent 运行。这需要工作区隔离、独立工具审批，以及每次运行清晰的审计归属。

### 上下文工程增强

- 基于最近编辑和会话主题的更智能文件相关性评分。
- 使用增量索引替代全量重扫。
- 面向大型仓库的更好提示词组织。

### 桌面体验

- 安装器和自动更新。
- 主题自定义。
- 常用 Agent 操作快捷键。

## 开发原则

1. 保持分层：UI 在 `AIChat.App`，Agent 编排在 `AIChat.Application`，领域模型在 `AIChat.Domain`，协议适配在 `AIChat.Providers.*`，持久化在 `AIChat.Storage.Json`。
2. 避免继续扩大 `MainViewModel.cs`，可复用逻辑应迁移到独立服务。
3. 保持改动小而聚焦。除非功能需要，不要把 UI、Provider、Harness 和 Storage 工作混在一起。
4. 工具和 Agent 变更必须考虑权限、审计、恢复和测试。
5. 文件写入、Shell 和 Git 修改功能必须保持保守，不能绕过 `ProjectPathGuard` 或审批机制。
6. 不要删除或回滚未提交的用户变更。开始前先检查 `git status --short`。

## 验证

代码变更：

```powershell
dotnet build AIChat.sln --no-restore -m:1 -v:minimal
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1 -v:minimal
git diff --check
```

仅文档变更：

```powershell
git diff --check
```
