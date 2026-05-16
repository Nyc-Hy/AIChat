using System.Text;
using System.Text.Json;

namespace AIChat.Application.Tools;

public static class ToolResultSummarizer
{
    public const int DefaultThreshold = 4000;
    private const int HeadLineCount = 40;
    private const int TailLineCount = 20;

    public static AgentToolResult Summarize(AgentToolResult result, int threshold = DefaultThreshold)
    {
        if (result.Content.Length <= threshold)
        {
            return result;
        }

        var summary = BuildSummary(result);
        return new AgentToolResult
        {
            ToolName = result.ToolName,
            Content = result.Content,
            IsError = result.IsError,
            Status = result.Status,
            FailureReason = result.FailureReason,
            ModelContent = summary,
            WasSummarized = true,
            ArtifactKind = "tool_result",
            Summary = summary
        };
    }

    private static string BuildSummary(AgentToolResult result)
    {
        var normalized = result.Content.ReplaceLineEndings("\n");
        var extracted = TryExtractShellOutput(normalized, out var shell)
            ? shell
            : normalized;
        var lines = extracted.Split('\n');
        var builder = new StringBuilder();
        builder.AppendLine($"工具 {result.ToolName} 返回了较大的输出，原文已保存为运行产物。");
        builder.AppendLine($"原始长度：{result.Content.Length} 字符；摘要来源：开头 {Math.Min(HeadLineCount, lines.Length)} 行 + 结尾 {Math.Min(TailLineCount, Math.Max(0, lines.Length - HeadLineCount))} 行。");

        if (TryExtractShellMetadata(normalized, out var metadata))
        {
            builder.AppendLine(metadata);
        }

        builder.AppendLine();
        builder.AppendLine("开头：");
        AppendLines(builder, lines.Take(HeadLineCount));

        if (lines.Length > HeadLineCount)
        {
            builder.AppendLine();
            builder.AppendLine("结尾：");
            AppendLines(builder, lines.Skip(Math.Max(HeadLineCount, lines.Length - TailLineCount)));
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendLines(StringBuilder builder, IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }
    }

    private static bool TryExtractShellOutput(string content, out string output)
    {
        output = "";
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var parts = new List<string>();
            AddProperty(parts, root, "stdout", "stdout");
            AddProperty(parts, root, "stderr", "stderr");
            AddProperty(parts, root, "output", "output");
            output = string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
            return !string.IsNullOrWhiteSpace(output);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractShellMetadata(string content, out string metadata)
    {
        metadata = "";
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var items = new List<string>();
            if (root.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String)
            {
                items.Add($"命令：{command.GetString()}");
            }

            if (root.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number)
            {
                items.Add($"退出码：{exitCode.GetInt32()}");
            }

            if (root.TryGetProperty("timedOut", out var timedOut) &&
                (timedOut.ValueKind == JsonValueKind.True || timedOut.ValueKind == JsonValueKind.False))
            {
                items.Add($"超时：{timedOut.GetBoolean()}");
            }

            metadata = string.Join("；", items);
            return items.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddProperty(List<string> parts, JsonElement root, string propertyName, string label)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            parts.Add($"{label}:\n{value.GetString()}");
        }
    }
}
