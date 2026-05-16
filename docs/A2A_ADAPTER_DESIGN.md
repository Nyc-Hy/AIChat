# A2A Adapter 设计

## 概览

AIChat 是本地优先的桌面代码 Agent。A2A（Agent-to-Agent）适配器允许外部 Agent 请求 AIChat 执行项目级任务，例如读取/写入文件、构建、测试和 Shell 命令，同时保留现有安全保证。

## 核心原则

1. **所有外部请求都经过 Harness。** 不允许绕过 Agent 循环直接调用工具。
2. **权限执行保持一致。** 外部请求遵守同样的 `ToolPermissionMode`、审批流程和 Shell 沙箱。
3. **审计链路完整。** 每个外部请求都产生审计事件，并标记请求方 Agent ID。
4. **工作区保护无条件生效。** 无论请求来源如何，路径保护、冲突检测和回滚安全都必须适用。

## 架构

```text
External Agent (MCP/A2A)
        |
        v
  A2A Endpoint (HTTP/gRPC)
        |
        v
  A2A Request Validator
        |
        v
  AgentHarness.RunAsync()  ← 与交互式 UI 使用同一入口
        |
        v
  AgentRunner → ToolExecutionService → IAgentTool
        |
        v
  AuditLogRepository（事件标记外部 Agent ID）
```

## 请求流程

1. 外部 Agent 发送任务请求（目标、项目路径、约束）。
2. A2A endpoint 校验请求、解析项目并创建 `AgentHarnessRunRequest`。
3. 请求传入 `AgentHarness.RunAsync()`，并为外部 Agent 配置 `AgentRunContext`：
   - `ProjectPath` 来自请求解析结果。
   - `ToolPermissionModes` 来自项目级覆盖。
   - `RequestToolApprovalAsync` 默认设置为自动拒绝，或使用可配置策略。
4. Harness 运行标准 Agent 循环：规划、工具调用、验证。
5. 审计事件记录外部 Agent ID。
6. 将结果（成功/失败、文件变更、验证输出）返回外部 Agent。

## 安全模型

### 工具权限策略

外部请求使用可配置权限策略：

- **自动拒绝（默认）：** 所有写入和 Shell 工具都会被拒绝。外部 Agent 只能读取文件和检查项目。
- **自动批准并审计：** 写入和 Shell 工具自动批准，但每次调用都会记录。适合可信内部 Agent。
- **交互式审批：** 外部请求暂停并等待用户逐个审批工具调用，与交互式会话一致。

### 路径保护

现有 `ProjectPathGuard` 保证所有文件操作都留在项目目录内。外部 Agent 不能逃逸工作区。

### Shell 沙箱

现有 `ShellCommandTool` 的 blocklist 和 allowlist 继续适用。即使权限策略允许 Shell，外部 Agent 也不能执行破坏性命令。

### 速率限制

外部请求应加入速率限制以防滥用：

- 每个外部 Agent 的最大并发运行数。
- 每次运行的最大工具调用数（沿用 `MaxToolRounds`）。
- 每次运行的最大文件变更数。

## 数据模型扩展

```csharp
// AgentHarnessRunRequest 新字段
public string ExternalAgentId { get; init; } = "";

// AgentRun 新字段
public string ExternalAgentId { get; set; } = "";

// 新审计事件类型
public enum AuditEventType
{
    // ... existing types ...
    ExternalAgentRequest,
    ExternalAgentResponse
}
```

## MCP 集成

A2A 适配器可以暴露 MCP-compatible endpoint：

```csharp
public class McpToolProvider : IExternalToolProvider
{
    public string Id => "mcp-server";
    public string Name => "MCP Server";

    public async Task<IReadOnlyList<IAgentTool>> GetToolsAsync(CancellationToken ct)
    {
        // 连接 MCP server、发现工具，并包装为 IAgentTool
    }
}
```

MCP 工具通过 `AgentToolRegistry.RegisterExternalProvider()` 注册，并与内置工具一起可用。

## A2A 协议映射

| A2A 概念 | AIChat 映射 |
|---|---|
| Agent Card | AIChat 项目 + 已启用工具 |
| Task | `AgentHarnessRunRequest` |
| Artifact | `AgentFileChange` |
| Message | `ChatMessage` |
| Part | 工具调用参数/结果 |

## 实现阶段

1. **阶段 1（当前）：** 定义接口（`IExternalToolProvider`），registry 支持注册。暂不提供外部 endpoint。
2. **阶段 2：** 增加 `A2AEndpoint` 作为 HTTP listener，实现自动拒绝权限策略。
3. **阶段 3：** 增加可配置权限策略和速率限制。
4. **阶段 4：** 通过 `McpToolProvider` 增加 MCP server 集成。
5. **阶段 5：** 完整 A2A 协议支持，包括 Agent 发现和任务委托。

## 约束

- A2A 不能绕过工具权限。
- A2A 不能绕过工作区保护。
- A2A 不能绕过审计日志。
- A2A 不能绕过验证/自动修复循环。
- 所有外部请求都必须在 Agent 运行历史中可见。
