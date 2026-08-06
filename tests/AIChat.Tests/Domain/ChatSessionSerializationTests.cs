using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Domain;

// T-DOM layer: ChatSession polymorphic 序列化。
// 验证 [JsonPolymorphic] + [JsonDerivedType] attribute 正确工作：
// Standalone / Project 序列化时写 $type 字段；反序列化时按 $type 路由。
public sealed class ChatSessionSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    [Fact]
    public void Serialize_Standalone_WritesTypeDiscriminator()
    {
        var session = new Standalone { Id = "s1", Title = "Quick Q" };

        var json = JsonSerializer.Serialize<ChatSession>(session, Options);

        Assert.Contains("\"$type\":\"standalone\"", json);
        Assert.Contains("\"id\":\"s1\"", json);
    }

    [Fact]
    public void Serialize_Project_WorkspacesIdInJson()
    {
        var session = new Project { Id = "p1", Title = "Project chat", WorkspaceId = "ws-1" };

        var json = JsonSerializer.Serialize<ChatSession>(session, Options);

        Assert.Contains("\"$type\":\"project\"", json);
        Assert.Contains("\"workspaceId\":\"ws-1\"", json);
    }

    [Fact]
    public void Deserialize_Standalone_RoutesToStandaloneKind()
    {
        var json = """{"$type":"standalone","id":"s1","title":"Quick Q"}""";

        var session = JsonSerializer.Deserialize<ChatSession>(json, Options);

        var standalone = Assert.IsType<Standalone>(session);
        Assert.Equal("s1", standalone.Id);
        Assert.Equal("Quick Q", standalone.Title);
    }

    [Fact]
    public void Deserialize_Project_RoutesToProjectKindAndReadsWorkspaceId()
    {
        var json = """{"$type":"project","id":"p1","title":"X","workspaceId":"ws-42"}""";

        var session = JsonSerializer.Deserialize<ChatSession>(json, Options);

        var project = Assert.IsType<Project>(session);
        Assert.Equal("p1", project.Id);
        Assert.Equal("ws-42", project.WorkspaceId);
    }

    [Fact]
    public void RoundTrip_PreservesKindAndFields()
    {
        var original = new Project
        {
            Id = "p1",
            Title = "Hello",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            WorkspaceId = "ws-1",
            Messages = [new ChatMessage { Role = ChatRole.User, Content = "hi" }],
        };

        var json = JsonSerializer.Serialize<ChatSession>(original, Options);
        var restored = JsonSerializer.Deserialize<ChatSession>(json, Options);

        var project = Assert.IsType<Project>(restored);
        Assert.Equal(original.Id, project.Id);
        Assert.Equal(original.Title, project.Title);
        Assert.Equal(original.WorkspaceId, project.WorkspaceId);
        Assert.Equal(original.UpdatedAt, project.UpdatedAt);
        Assert.Single(project.Messages);
        Assert.Equal("hi", project.Messages[0].Content);
    }

    [Fact]
    public void Deserialize_MissingTypeDiscriminator_ThrowsNotSupported()
    {
        // System.Text.Json + [JsonPolymorphic] 默认行为：abstract base class 缺
        // $type 字段时抛 NotSupportedException（fail-fast，不静默初始化 base）。
        // 写入端永远带 [JsonPolymorphic] 会写 $type；缺 $type 只能来自外部损坏
        // 的 JSON —— fail-fast 正是我们想要的。
        var json = """{"id":"s1","title":"X"}""";

        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Deserialize<ChatSession>(json, Options));
    }
}
