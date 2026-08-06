using System.Text.Json.Serialization;

namespace AIChat.Domain.Chat;

// Wave 1: 顶替 Conversation（plan §2.1 + 修正 #1 #4）。
//
// 判别联合：Standalone (无 project) 或 Project(workspaceId)。System.Text.Json
// polymorphic 序列化通过 [JsonPolymorphic] + [JsonDerivedType] attribute，序列化时
// 自动写 "$type": "standalone" / "$type": "project" 字段；反序列化时按
// 字段值路由到具体 subtype。
//
// init-only 属性在 UI 改字段时会强制 `with` 表达式（很啰嗦），所以所有 setter
// 改成 `set` 而不是 `init` —— UI 可以 `session.Title = "x"` 直接写，
// 跟旧 Conversation 的赋值风格保持一致。
//
// 旧 Conversation 保留为 [Obsolete]，UI 切换前仍能编译通过；Wave 2 完成后
// 删掉旧类型。
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Standalone), "standalone")]
[JsonDerivedType(typeof(Project), "project")]
public abstract class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新对话";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = [];
    // CallDetails / AgentRuns 不在 model context；learning / debugging / observability 用
    public List<LlmCallDetail> CallDetails { get; set; } = [];
    public List<AgentRun> AgentRuns { get; set; } = [];
}

// Standalone session：无 project context；只跑不需要项目工具的功能
public sealed class Standalone : ChatSession;

// Project session：绑到某个 WorkspaceProject.Id
public sealed class Project : ChatSession
{
    // 不写 constructor 让 JsonSerializer 走无参 ctor + setter；
    // Project 也支持 "switching workspace later"（Wave 3+）
    public string WorkspaceId { get; set; } = "";
}
