# AIChat 代码解释与 Agent 学习路线

这份文档按“读代码时真正会经过的路径”来解释项目。AIChat 当前是一个项目级 LLM 聊天桌面 MVP，还不是完整 Agent；但它已经具备写 Agent 需要的几个核心基础：消息模型、上下文预算、模型 Provider 抽象、流式输出、调用记录和本地持久化。

## 1. 项目分层

```text
src/
  AIChat.App/                  WPF 界面、MVVM 状态、命令、窗口组合根
  AIChat.Domain/               纯领域模型：消息、会话、项目、上下文用量
  AIChat.Abstractions/         跨层接口和 DTO：Provider、Repository、Settings
  AIChat.Application/          应用服务：Provider 路由、上下文估算
  AIChat.Providers.OpenAI/     OpenAI-compatible 协议适配
  AIChat.Providers.Anthropic/  Anthropic 协议适配
  AIChat.Storage.Json/         JSON 本地存储实现
```

最重要的原则是：UI 不直接知道 HTTP 怎么发，Provider 不知道 WPF 怎么显示，存储层不关心用户点击了哪个按钮。Agent 项目也应该保持这种边界，否则功能一多会很快缠在一起。

## 2. 启动流程

入口在 `AIChat.App`：

1. `App.xaml.cs` 创建并显示 `MainWindow`。
2. `MainWindow.xaml.cs` 在构造函数里创建 `MainViewModel`。
3. `MainWindow` 同时手动组装依赖：
   - `JsonAppRepository`
   - `RoutedChatCompletionService`
   - `OpenAICompatibleChatProvider`
   - `AnthropicChatProvider`
   - `SimpleContextEstimator`
4. 窗口 `Loaded` 后调用 `MainViewModel.InitializeAsync()`。
5. `InitializeAsync()` 加载设置、修正规格、加载项目、选中默认会话。

这个地方叫“组合根”。现在手动 new 对象很适合学习；以后项目变大，可以换成依赖注入容器。

## 3. 核心领域模型

`AIChat.Domain` 里都是不依赖 UI、不依赖 HTTP 的模型。

`ChatMessage` 表示一条消息：

- `Role` 区分 `System`、`User`、`Assistant`
- `Content` 是消息正文
- `IsError` 用来保存失败回复
- `CreatedAt` 用来显示时间和排序

`Conversation` 表示一次会话：

- `Messages` 是真正发给模型的上下文来源
- `CallDetails` 是调试记录，不会进入模型上下文

`ChatRequest` 是发给 Provider 前的统一请求：

- `Model`
- `Messages`
- `Temperature`

`ChatDelta` 是 Provider 流式返回的统一片段。不同厂商的流式协议不同，但最终都被转成 `ChatDelta`，这样 UI 只需要追加 `Content`。

## 4. MVVM 怎么工作

WPF 界面主要由 `MainWindow.xaml` 和 ViewModel 驱动。

`ObservableObject` 提供 `INotifyPropertyChanged`。当 ViewModel 的属性变化时，XAML 绑定会自动刷新。

`RelayCommand` 把按钮点击转成 ViewModel 方法，比如：

- `SendCommand`
- `NewChatCommand`
- `OpenSettingsCommand`
- `StopCommand`
- `AddConfiguredProviderCommand`

`MainViewModel` 是当前 MVP 的主状态机。它负责：

- 当前项目和当前会话
- 草稿消息
- 是否正在发送
- 设置窗口状态
- 调用详情窗口状态
- 上下文用量
- Provider 配置
- 发送消息主流程

以后写 Agent 时，`MainViewModel` 不应该无限膨胀。Agent 的规划、工具调用、记忆管理最好逐步移到 `AIChat.Application` 里的服务中。

## 5. 一次发送消息的完整流程

核心方法是 `MainViewModel.SendAsync()`。

### 第一步：确保有会话

如果没有选中会话，但是有项目，就创建或选中一个会话。

### 第二步：检查模型设置

`NormalizeProviderSettings()` 会把旧设置或空设置修正到当前 Provider Catalog。

`CreateEffectiveSettings()` 会从当前选中的 `ConfiguredLlmProvider` 生成真正用于请求的 `AppSettings`。如果没有 API Key，就不会发请求。

### 第三步：写入用户消息

用户输入会变成 `ChatMessage`，加入当前 `Conversation`。

注意：它先加入会话，再构造 `ChatRequest`。这样本轮用户消息会被包含在发给模型的上下文里。

### 第四步：创建助手占位消息

代码先插入一条 Assistant 消息，内容是“正在连接模型...”。流式内容回来后，这条消息会被逐步替换和追加。

这个设计很重要：UI 上看到的是同一条 assistant 消息在增长，而不是每个 token 新增一条消息。

### 第五步：记录调用详情

`LlmCallDetail` 会保存：

- Provider 名称
- Model
- 请求时间
- 请求 JSON
- 最终响应 JSON
- 原始流式事件摘要

这是学习 Agent 非常有价值的地方。Agent 行为复杂后，你必须能追踪“当时给模型看了什么、模型返回了什么、工具又做了什么”。

### 第六步：构造 ChatRequest

`ChatRequest` 只保留 Provider 无关的数据。它不会包含 HTTP Header，也不会包含某家 API 的特殊字段。

### 第七步：调用聊天服务并流式读取

`_chatService.SendAsync(request, effectiveSettings, token)` 返回 `IAsyncEnumerable<ChatDelta>`。

`RoutedChatCompletionService` 会选出能处理当前设置的 Provider。比如 TokenPlan MIMO 的 `ProtocolId` 是 `openai`，所以会走 `OpenAICompatibleChatProvider`。

每个 `ChatDelta.Content` 会被 `AppendAssistantContentAsync()` 追加到 Assistant 消息。

### 第八步：收尾和持久化

不管成功、取消还是异常，`finally` 都会：

- 停止 streaming 状态
- 释放取消令牌
- 保存项目和会话
- 更新上下文用量

这是异步 UI 程序非常重要的习惯：网络调用失败也要让界面回到可操作状态。

## 6. Provider 抽象

Provider 相关接口在 `AIChat.Abstractions.Llm`。

`IChatCompletionService` 是 UI 面向的服务：

```csharp
IAsyncEnumerable<ChatDelta> SendAsync(ChatRequest request, AppSettings settings, CancellationToken cancellationToken = default);
```

`IChatProvider` 是具体厂商适配器：

- `CanHandle(settings)` 判断自己能不能处理
- `SendAsync(...)` 负责 HTTP 请求和流式解析

当前有两个实现：

- `OpenAICompatibleChatProvider`
- `AnthropicChatProvider`

Provider Catalog 现在包含小米 MIMO、DeepSeek、MiniMax。每个模型可以声明：

- `CapabilityLabel`：界面展示的人类可读能力摘要
- `Capabilities`：工具、思考、JSON 输出、交错思考等能力标记
- `Parameters`：只有该模型支持时才显示的执行参数

设置窗口的“执行模型参数”不是固定表单，而是从当前选中模型的 `Parameters` 动态生成。这样小米 MIMO 不显示 DeepSeek 参数，MiniMax 也不会出现 DeepSeek 的 `thinking` 设置。

两者的思想一样：

1. 检查 API Key。
2. 把 `ChatRequest` 映射成对应 API 的 JSON payload。
3. 发 HTTP 请求。
4. 读取 streaming response。
5. 把厂商事件转成 `ChatDelta`。

这就是写 LLM 应用时最常见的“协议适配器”模式。

## 7. OpenAI-Compatible Provider

文件：`AIChat.Providers.OpenAI/OpenAICompatibleChatProvider.cs`

关键点：

- endpoint 是 `{BaseUrl}/chat/completions`
- Header 使用 `Authorization: Bearer ...`
- Payload 包含 `model`、`temperature`、`stream`、`messages`
- 流式响应按 SSE 读取
- `data: [DONE]` 表示完成
- 内容通常在 `choices[].delta.content`

代码里还兼容了 `reasoning_content`，这是一些推理模型可能返回的字段。

## 8. Anthropic Provider

文件：`AIChat.Providers.Anthropic/AnthropicChatProvider.cs`

关键点：

- endpoint 是 `{BaseUrl}/v1/messages`
- Header 使用 `x-api-key`
- Anthropic 把 system prompt 单独放在 `system` 字段
- 普通 messages 里只保留 user/assistant
- 文本增量在 `content_block_delta` 事件的 `delta.text`

这说明为什么 Provider 抽象重要：同样是聊天，不同厂商的请求和响应形状差别很大。

## 9. 上下文估算

文件：`AIChat.Application/Context/SimpleContextEstimator.cs`

当前估算方式很简单：

```text
estimatedTokens = ceil(totalChars / 3.6)
```

这不是精确 tokenizer，但能让 UI 先具备“上下文预算”的概念。

Agent 里上下文预算会更重要，因为除了聊天历史，还会塞入：

- 系统指令
- 项目摘要
- 文件片段
- 工具结果
- 计划步骤
- 错误日志

以后可以把 `SimpleContextEstimator` 替换成真正 tokenizer，而 UI 和 ViewModel 不需要大改。

## 10. 本地持久化

文件：`AIChat.Storage.Json/JsonAppRepository.cs`

数据保存在：

```text
%APPDATA%\AIChat
```

主要有：

- `settings.json`
- `projects.json`

`MainViewModel` 只依赖 `IAppRepository`，不知道底层是 JSON。以后要换成 SQLite，也只需要新增一个 Repository 实现。

## 11. 调用详情为什么重要

调用详情窗口显示每次请求和响应的 JSON。

这是学习 Agent 的第一块观测面板。因为 Agent 不是“问一句答一句”，它通常是：

1. 用户提出目标。
2. 模型生成计划。
3. Agent 选择工具。
4. 工具执行。
5. 结果回填给模型。
6. 模型继续推理。
7. 多轮循环直到完成。

如果没有调用记录，你很难知道 Agent 在哪一步偏了。

当前 `LlmCallDetail` 只记录模型调用。下一阶段可以扩展出：

- `ToolCallDetail`
- `AgentStep`
- `PlanRevision`
- `MemoryWrite`

## 12. 从聊天应用演进到 Agent

当前项目现在已经具备了一个最小 Agent 底座：模型可以看到已启用工具，选择工具调用，应用执行只读工具，再把结果喂回模型继续生成。

可以按这个顺序学习和扩展：

### 阶段 A：普通聊天

当前项目已经完成：

- 用户输入
- 会话历史
- Provider 调用
- 流式显示
- 本地保存

### 阶段 B：系统提示词

新增一个系统消息或系统 prompt 构建器，让模型知道：

- 当前项目是什么
- 用户希望 AIChat 扮演什么角色
- 回复风格和边界是什么

可以新增：

```text
AIChat.Application/Prompting/SystemPromptBuilder.cs
```

### 阶段 C：上下文组装器

不要永远把全部历史塞给模型。新增 Context Builder：

```text
AIChat.Application/Context/ConversationContextBuilder.cs
```

它负责选择：

- 最近几轮消息
- 项目摘要
- 必要文件片段
- 工具执行结果

### 阶段 D：工具抽象

Agent 和普通聊天最大的区别是“能行动”。

当前项目已经新增：

```csharp
public interface IAgentTool
{
    string Id { get; }
    ChatToolDefinition Definition { get; }
    Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken);
}
```

第一批工具可以很保守：

- 读取项目文件
- 搜索文本
- 列目录

当前实现已经落地这些工具：

- `list_files`
- `read_file`
- `search_text`
- `write_file`
- `edit_file`
- `run_shell`

这些工具都通过 `ProjectPathGuard` 限制在当前项目路径下。`write_file` 和 `edit_file` 还会阻止写入 `.git`、`.vs`、`bin`、`obj`、`artifacts`、`TestResults` 等目录。`run_shell` 用于构建、测试、查看状态，并会阻断常见破坏性命令片段。

测试方式：

1. 打开设置窗口的“工具”页。
2. 勾选想测试的工具，例如 `list_files`、`read_file`、`write_file`。
3. 保存设置。
4. 在聊天里明确要求模型使用工具，例如：

```text
请使用 list_files 查看当前项目根目录有哪些源码文件。
```

```text
请使用 write_file 在 docs/tool-test.md 写入一段测试文本，然后用 read_file 读回来确认。
```

```text
请使用 run_shell 执行 dotnet --version，并告诉我输出。
```

### 阶段 E：Agent 循环

Agent 循环已经放在：

```text
AIChat.Application/Agents/AgentRunner.cs
```

基本逻辑是：

```text
while not done:
  build context
  ask model what to do next
  if model wants to answer:
    return final answer
  if model wants a tool:
    execute tool
    append tool result
```

这个循环就是 Agent 的心脏。

当前 `AgentRunner` 的具体行为是：

1. 读取设置中启用的工具 ID。
2. 把这些工具的 JSON schema 放进 `ChatRequest.Tools`。
3. 通过 `IChatCompletionService` 调用模型。
4. 如果模型返回普通文本，就流式显示。
5. 如果模型返回 tool call，就执行对应 `IAgentTool`。
6. 把工具结果作为 `ChatRole.Tool` 消息追加到临时 transcript。
7. 再次调用模型，让模型基于工具结果继续回答。
8. 最多执行 4 轮工具调用，避免无限循环。

### 阶段 F：可观测性

把每一步都存下来：

- 第几步
- 模型看到了什么
- 模型决定调用什么工具
- 工具参数是什么
- 工具结果是什么
- 最终回答是什么

当前 `LlmCallDetail` 已经是这个方向的起点。

## 13. 推荐阅读顺序

第一次读代码可以按这个顺序：

1. `AIChat.Domain/Chat/ChatMessage.cs`
2. `AIChat.Domain/Chat/Conversation.cs`
3. `AIChat.Abstractions/Llm/IChatProvider.cs`
4. `AIChat.Application/Llm/Routing/RoutedChatCompletionService.cs`
5. `AIChat.Providers.OpenAI/OpenAICompatibleChatProvider.cs`
6. `AIChat.App/ViewModels/MainViewModel.cs` 的 `SendAsync()`
7. `AIChat.Storage.Json/JsonAppRepository.cs`
8. `AIChat.App/MainWindow.xaml.cs`
9. `AIChat.App/MainWindow.xaml`

这条线会带你从“数据是什么”一路走到“用户点发送后到底发生了什么”。

## 14. 当前代码里的几个关键设计思想

### 面向接口

`MainViewModel` 不直接 new Provider，而是依赖 `IChatCompletionService` 和 `IAppRepository`。这让替换实现变简单。

### Provider 适配

不同模型厂商差异很大，但 UI 不应该知道这些差异。Provider 负责翻译协议。

### 流式输出

模型输出不是一次性返回，而是一段段返回。代码用 `IAsyncEnumerable<ChatDelta>` 表达这个过程。

### 状态和副作用分离

Domain 模型是状态，Provider/Repository 是副作用，ViewModel 是协调者。Agent 项目也要尽量保持这个分离。

### 可观测性优先

调用详情不是附属功能。它是后续学习 Agent、调试 Agent 的地基。

## 15. 下一步建议

最适合的下一步不是继续扩大工具数量，而是让 Agent 过程更可见、更可控：

1. 增加 `SystemPromptBuilder`，告诉模型什么时候该用工具、什么时候直接回答。
2. 增加 `ConversationContextBuilder`，控制哪些历史消息进入模型上下文。
3. 增加专门的 Agent Step 面板，而不是只把 tool call 塞进 raw events。
4. 给工具增加权限分级，为以后写文件、执行命令做准备。
5. 增加测试项目，覆盖路径保护、工具参数解析、Provider tool call 解析。

这样你会一步一步看到：普通聊天应用怎样变成能规划、能读项目、能调用工具、能解释自己执行过程的 Agent。
