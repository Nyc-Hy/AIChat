# Agent Harness

Agent Harness 是 AIChat 的核心编排层，负责运行模型/工具循环。

## 生命周期

```text
RunStarted
    │
    ▼
┌─────────────────────────────┐
│  AgentRunner.RunAsync()     │◄──── 将验证失败反馈给模型
│  ├─ 向模型发送消息          │      （自动修复）
│  ├─ 模型返回响应            │
│  ├─ 如果有 tool_calls:      │
│  │   ├─ 执行每个工具        │
│  │   ├─ 记录步骤            │
│  │   └─ 回到循环            │
│  └─ 如果没有 tool_calls:    │
│      └─ 完成                │
└─────────────────────────────┘
    │
    ▼
记录文件变更（快照 + 哈希）
    │
    ▼
运行验证命令（如已配置）
    │
    ├─ 全部通过 ──► RunCompleted(Completed)
    │
    └─ 存在失败 ──► 将失败摘要反馈给模型
                     ├─ 回到 AgentRunner（最多 MaxAutoFixRounds 次）
                     └─ 修复轮数耗尽后 RunCompleted(Failed)
```

## 关键类型

| 类型 | 用途 |
|---|---|
| `AgentHarness` | 编排运行、发送事件、记录状态 |
| `AgentRunner` | 无状态模型/工具循环 |
| `AgentHarnessRunRequest` | 输入：会话、目标、设置和上下文 |
| `AgentRunContext` | 运行时配置：项目路径、工具、权限 |
| `AgentRun` | 持久化运行记录 |
| `AgentStep` | 单次工具调用或模型响应 |
| `AgentFileChange` | 带快照和哈希的文件变更 |
| `AgentVerification` | 验证命令结果 |

## 事件

Harness 通过 `IAsyncEnumerable` 发送 `AgentHarnessEvent`：

| 事件 | 含义 |
|---|---|
| `RunStarted` | 运行开始并创建 `AgentRun` |
| `StepAdded` | 新步骤已记录 |
| `ContentDelta` | 模型文本增量 |
| `ToolCall` | 模型请求工具调用 |
| `ToolApprovalRequired` | 等待用户审批 |
| `ToolApprovalRejected` | 用户拒绝工具调用 |
| `ToolResult` | 工具执行完成 |
| `RawProviderEvent` | 用于调试的原始协议事件 |
| `RunCompleted` | 运行结束（成功、失败或取消） |

## 自动修复循环

启用 `AutoVerifyAgentRuns` 后：

1. 初始 Agent 运行完成后，Harness 检查 `VerificationCommands`。
2. 逐个执行验证命令，并解析错误输出。
3. 如果存在失败，将失败摘要注入会话。
4. 使用更新后的 transcript 再次调用 `AgentRunner.RunAsync()`。
5. 模型读取失败信息并尝试修复。
6. 最多重复到 `MaxAutoFixRounds`，默认 3 轮。

`AgentRunner` 是无状态的，没有可变实例字段，因此可以在自动修复中多次复用。

## 无状态设计

可变状态只存在于：

- `AgentRun`：持久化领域模型。
- `AgentHarness`：编排状态。
- `sessionAllowedTools`：每次运行内的本地工具允许集合。

这使 Harness 可以在自动修复循环中安全地多次调用 Runner。
