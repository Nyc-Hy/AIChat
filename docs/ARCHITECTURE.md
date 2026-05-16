# 架构说明

## 分层图

```text
┌─────────────────────────────────────────────────┐
│                   AIChat.App                     │
│  WPF Shell · MVVM · Composition Root · XAML      │
├─────────────────────────────────────────────────┤
│              AIChat.Application                  │
│  Agent Harness · Tools · Prompting · Context     │
│  Verification · Routing · Workspace              │
├─────────────────────────────────────────────────┤
│  AIChat.Providers.OpenAI │ AIChat.Providers.Anthropic │
│  Protocol Adapters (IChatProvider)               │
├─────────────────────────────────────────────────┤
│              AIChat.Abstractions                 │
│  Contracts · DTOs · Configuration                │
├─────────────────────────────────────────────────┤
│               AIChat.Domain                      │
│  Pure POCOs · Chat · Projects · Audit · Context  │
├─────────────────────────────────────────────────┤
│             AIChat.Storage.Json                  │
│  Local JSON Persistence (%APPDATA%\AIChat)       │
└─────────────────────────────────────────────────┘
```

## 依赖规则

- **App** 依赖 Application、Abstractions、Domain、Providers、Storage。
- **Application** 依赖 Abstractions、Domain。
- **Providers** 依赖 Abstractions、Domain。
- **Abstractions** 不依赖其他项目。
- **Domain** 不依赖其他项目。
- **Storage** 依赖 Domain、Abstractions。

Domain 是最内层。任何项目都不应依赖 App。

## 核心抽象

| 接口 | 位置 | 用途 |
|---|---|---|
| `IChatProvider` | Abstractions | 具体模型协议适配器 |
| `IAgentTool` | Application | 工具定义和执行入口 |
| `IAppRepository` | Abstractions | 设置和项目持久化 |
| `IContextEstimator` | Abstractions | Token 数估算 |
| `IExternalToolProvider` | Application | 未来 MCP/A2A 外部工具来源 |

## 数据流

```text
User Input
    │
    ▼
MainViewModel.SendAsync()
    │
    ├─ 构建上下文（文件索引、工作区摘要、固定上下文项）
    ├─ 构建系统提示词（规则、工具、上下文包）
    ├─ 创建 ChatRequest
    │
    ▼
AgentHarness.RunAsync()
    │
    ├─ AgentRunner.RunAsync() ──► IChatProvider.SendAsync()
    │       │
    │       ▼
    │   模型返回 tool_calls
    │       │
    │       ▼
    │   ToolExecutionService.ExecuteAsync()
    │       │
    │       ├─ 检查权限模式
    │       ├─ 必要时请求用户审批
    │       ├─ 执行 IAgentTool
    │       └─ 将结果返回模型
    │
    ├─ 记录步骤、文件变更和计划更新
    ├─ 运行验证命令（如已配置）
    └─ 向 UI 发送事件
```

## 持久化

所有数据默认保存在本机 `%APPDATA%\AIChat\` 下：

- `settings.json`：应用设置，包括 Provider、工具和权限。
- `projects.json`：项目工作区、会话和 Agent 运行记录。
- `audit/<project-id>.jsonl`：每个项目的审计事件日志。

除配置的 LLM API 请求外，数据不会离开本机。
