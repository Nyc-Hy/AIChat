using System.Text;
using AIChat.Domain.Chat;

namespace AIChat.Application.Chat;

// Renders a ChatSession to a Markdown transcript that the user can
// paste into a doc, share with a teammate, or attach to a bug.
// Format:
//   # <title>
//   _Created: <iso>. Messages: <count>._
//
//   ## User · <iso>
//   <content>
//
//   ## Assistant · <iso>
//   <content>
//   ### Tool call: <name> (<id>)
//   ```json
//   <args>
//   ```
//   ...
//
// The exporter is deliberately dependency-free so it is easy to
// unit-test (no I/O, no platform guards). The desktop host wraps it
// in a FilePicker-driven command that handles the actual file write
// and the "user clicked cancel" path.
public static class MarkdownConversationExporter
{
    public static string Export(ChatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(session.Title) ? "新对话" : session.Title;
        sb.Append("# ").Append(title).Append('\n');
        sb.Append("_Last updated: ")
          .Append(session.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))
          .Append(". Messages: ")
          .Append(session.Messages.Count)
          .Append(". Exported: ")
          .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
          .Append("._\n\n");

        var order = 0;
        foreach (var message in session.Messages)
        {
            order++;
            sb.Append("## ")
              .Append(RoleLabel(message.Role))
              .Append(" · #")
              .Append(order)
              .Append(" · ")
              .Append(message.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append('\n');

            if (message.IsError)
            {
                sb.Append("> ⚠️ 这条消息产生过错误。\n");
            }

            if (!string.IsNullOrEmpty(message.Content))
            {
                sb.Append('\n');
                sb.Append(message.Content.TrimEnd());
                sb.Append("\n\n");
            }

            foreach (var trace in message.ToolTraces ?? [])
            {
                sb.Append("### 工具调用 · ")
                  .Append(string.IsNullOrWhiteSpace(trace.ToolName) ? "(unnamed)" : trace.ToolName)
                  .Append('\n');
                if (!string.IsNullOrEmpty(trace.ArgumentsJson))
                {
                    sb.Append("```json\n").Append(trace.ArgumentsJson).Append("\n```\n");
                }
                if (!string.IsNullOrEmpty(trace.ResultContent))
                {
                    sb.Append("**结果**\n\n```\n").Append(trace.ResultContent).Append("\n```\n");
                }
            }

            if ((message.ToolTraces?.Count ?? 0) > 0)
            {
                sb.Append('\n');
            }
        }

        if (session.Messages.Count == 0)
        {
            sb.Append("_(此对话还没有任何消息)_\n");
        }

        return sb.ToString();
    }

    private static string RoleLabel(ChatRole role) => role switch
    {
        ChatRole.System => "System",
        ChatRole.User => "用户",
        ChatRole.Assistant => "助手",
        ChatRole.Tool => "工具",
        _ => role.ToString(),
    };
}
