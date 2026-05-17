using System.Diagnostics;
using System.Text.Json;

namespace AIChat.Application.Plugins.Mcp;

public sealed class McpStdioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(
        McpStdioServerConfig config,
        CancellationToken cancellationToken = default)
    {
        return await RunSessionAsync<IReadOnlyList<McpToolDescriptor>>(config, async session =>
        {
            await session.InitializeAsync(cancellationToken);
            using var response = await session.SendRequestAsync("tools/list", null, cancellationToken);
            if (!response.RootElement.TryGetProperty("tools", out var toolsElement) ||
                toolsElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<McpToolDescriptor>();
            }

            var tools = new List<McpToolDescriptor>();
            foreach (var tool in toolsElement.EnumerateArray())
            {
                var name = tool.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var description = tool.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString() ?? ""
                    : "";
                var inputSchema = tool.TryGetProperty("inputSchema", out var inputSchemaElement)
                    ? inputSchemaElement.Clone()
                    : JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();
                tools.Add(new McpToolDescriptor(name, description, inputSchema));
            }

            return tools;
        }, cancellationToken);
    }

    public async Task<McpToolCallResult> CallToolAsync(
        McpStdioServerConfig config,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return await RunSessionAsync(config, async session =>
        {
            await session.InitializeAsync(cancellationToken);
            using var response = await session.SendRequestAsync("tools/call", new
            {
                name = toolName,
                arguments
            }, cancellationToken);
            var isError = response.RootElement.TryGetProperty("isError", out var isErrorElement) &&
                          isErrorElement.ValueKind is JsonValueKind.True;
            return new McpToolCallResult(FormatToolContent(response.RootElement), isError);
        }, cancellationToken);
    }

    private static async Task<T> RunSessionAsync<T>(
        McpStdioServerConfig config,
        Func<McpSession, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 120)));
        using var process = StartProcess(config);
        await using var session = new McpSession(process);
        try
        {
            return await action(session);
        }
        finally
        {
            TryClose(process);
        }
    }

    private static Process StartProcess(McpStdioServerConfig config)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.Command,
            WorkingDirectory = config.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in config.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static string FormatToolContent(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return result.GetRawText();
        }

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? "");
            }
            else
            {
                parts.Add(item.GetRawText());
            }
        }

        return string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static void TryClose(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // Cleanup should not hide the actual MCP result.
        }
    }

    private sealed class McpSession : IAsyncDisposable
    {
        private readonly Process _process;
        private int _nextId;

        public McpSession(Process process)
        {
            _process = process;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await SendRequestAsync("initialize", new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "AIChat", version = "0.1.0" }
            }, cancellationToken);
            await SendNotificationAsync("notifications/initialized", null, cancellationToken);
        }

        public async Task<JsonDocument> SendRequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            await WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            }, cancellationToken);

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    throw new InvalidOperationException("MCP server closed stdout before responding.");
                }

                var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("id", out var responseId) ||
                    responseId.ValueKind != JsonValueKind.Number ||
                    responseId.GetInt32() != id)
                {
                    document.Dispose();
                    continue;
                }

                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : error.GetRawText();
                    document.Dispose();
                    throw new InvalidOperationException($"MCP request `{method}` failed: {message}");
                }

                if (!document.RootElement.TryGetProperty("result", out var result))
                {
                    document.Dispose();
                    throw new InvalidOperationException($"MCP request `{method}` returned no result.");
                }

                return JsonDocument.Parse(result.GetRawText());
            }
        }

        private async Task SendNotificationAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            await WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters
            }, cancellationToken);
        }

        private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
