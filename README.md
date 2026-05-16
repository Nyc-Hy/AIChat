# AIChat

AIChat 是一个基于 .NET 8 / WPF 的桌面应用，用于项目级 LLM 对话和本地代码 Agent 工作流。

本项目使用 [Apache License 2.0](LICENSE) 开源。

## 功能特性

- **项目级对话**：每个项目拥有独立的会话历史和设置
- **多模型提供商**：支持 OpenAI-compatible 和 Anthropic 协议
- **Agent Harness**：模型/工具循环，包含规划、执行、验证和自动修复
- **14 个内置工具**：文件读写编辑、搜索、补丁、Git、构建、测试、Shell
- **工具权限模型**：禁用、只读自动执行、每次确认、本会话允许
- **项目级权限覆盖**：每个项目可覆盖全局工具权限
- **审计日志**：记录工具调用、审批、拒绝和运行生命周期
- **Agent 运行历史**：浏览、筛选、重试和继续历史运行
- **验证系统**：可配置构建/测试命令，并支持自动修复循环
- **上下文工程**：文件索引、预算化上下文包、固定上下文项
- **变更控制**：基于快照和哈希的冲突检测与安全回滚

## 运行

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

当当前模型支持工具调用时，AIChat 会自动进入 Agent 模式。Agent 会：

1. 接收用户目标和项目上下文
2. 创建计划，并在详情面板中展示
3. 调用工具读取、修改和验证代码
4. 运行验证命令，并在失败时尝试自动修复
5. 记录所有变更和快照，便于安全回滚

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

1. 自动运行验证命令
2. 如果验证失败，将失败摘要反馈给模型
3. 模型尝试修复问题
4. 最多重复到配置的修复轮数，默认 3 轮

## 架构

```text
src/
  AIChat.App/                  WPF Shell、MVVM 状态、组合根
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

- [架构说明](docs/ARCHITECTURE.md)
- [Agent Harness](docs/AGENT_HARNESS.md)
- [工具安全模型](docs/TOOL_SECURITY.md)
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

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained false
```

该命令生成依赖 .NET 8 Runtime 的发布包。

如果需要自包含发布包：

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained true
```
