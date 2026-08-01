using System.Text.Json;
using AIChat.Application.Security;
using AIChat.Application.Tools;
using AIChat.Application.Verification;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Run-step / run-fact recording — split out from the main
// AgentHarness partial so the orchestration file stays focused
// on the run loop. These helpers all live in service of
// appending to an AgentRun's per-run collections: AgentStep
// creation / completion (AddRunningStep, AddCompletedStep,
// CompleteToolStep), per-tool-result bookkeeping
// (RecordFileChanges, RecordVerification, RecordArtifact,
// RecordMutationGuardrail, RecordPlan), and the small
// utilities that feed them (Truncate, IsMutationTool,
// IsVerificationTool, the JSON parsing helpers ParseChanged
// Files / ParseChangedFile / ParseVerification, the JSON
// property accessors GetString / GetInt / GetBool, and the two
// local record types ChangedFileInfo / VerificationInfo).
public sealed partial class AgentHarness
{
    private static void RecordMutationGuardrail(
        AgentRun run,
        ChatToolCall? toolCall,
        AgentToolPreview? preview,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            toolResult.IsError ||
            preview?.Risk == AgentToolRisk.ReadOnly ||
            !IsMutationTool(toolResult.ToolName))
        {
            return;
        }

        run.MutationToolSucceeded = true;
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private static bool IsMutationTool(string toolName)
    {
        return toolName is "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit";
    }


    private static AgentStep AddRunningStep(
        AgentRun run,
        int number,
        AgentStepType type,
        string title,
        string input,
        string toolCallId = "",
        string toolName = "")
    {
        var step = new AgentStep
        {
            RunId = run.Id,
            Number = number,
            Type = type,
            Title = title,
            Input = SensitiveDataRedactor.RedactText(input),
            ToolCallId = toolCallId,
            ToolName = toolName,
            StartedAt = DateTimeOffset.Now
        };
        run.Steps.Add(step);
        return step;
    }

    private static AgentStep AddCompletedStep(
        AgentRun run,
        int number,
        AgentStepType type,
        string title,
        string input,
        string output)
    {
        var now = DateTimeOffset.Now;
        var step = new AgentStep
        {
            RunId = run.Id,
            Number = number,
            Type = type,
            Status = AgentStepStatus.Completed,
            Title = title,
            Input = SensitiveDataRedactor.RedactText(input),
            Output = SensitiveDataRedactor.RedactText(output),
            StartedAt = now,
            CompletedAt = now
        };
        run.Steps.Add(step);
        return step;
    }

    private static void CompleteToolStep(
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        string output,
        bool isError)
    {
        if (toolCall is null || !stepByToolCallId.TryGetValue(toolCall.Id, out var step))
        {
            return;
        }

        step.Output = SensitiveDataRedactor.RedactText(output);
        step.IsError = isError;
        step.Status = isError ? AgentStepStatus.Failed : AgentStepStatus.Completed;
        step.CompletedAt = DateTimeOffset.Now;
    }

    private static void RecordFileChanges(
        AgentRun run,
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        AgentToolPreview? preview,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            toolResult.IsError ||
            preview is null ||
            preview.Risk == AgentToolRisk.ReadOnly ||
            string.IsNullOrWhiteSpace(preview.DiffText))
        {
            return;
        }

        var stepId = stepByToolCallId.TryGetValue(toolCall.Id, out var step)
            ? step.Id
            : "";

        foreach (var changedFile in ParseChangedFiles(toolResult.Content))
        {
            run.FileChanges.Add(new AgentFileChange
            {
                RunId = run.Id,
                StepId = stepId,
                ToolCallId = toolCall.Id,
                ToolName = toolResult.ToolName,
                Path = changedFile.Path,
                DiffText = SensitiveDataRedactor.RedactText(ExtractDiffForPath(preview.DiffText, changedFile.Path)),
                OldChars = changedFile.OldChars,
                NewChars = changedFile.NewChars,
                ContentSnapshot = changedFile.ContentSnapshot,
                PostChangeHash = changedFile.PostChangeHash,
                CreatedAt = DateTimeOffset.Now
            });
        }
    }

    private static void RecordVerification(
        AgentRun run,
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            !IsVerificationTool(toolResult.ToolName))
        {
            return;
        }

        var stepId = stepByToolCallId.TryGetValue(toolCall.Id, out var step)
            ? step.Id
            : "";
        var parsed = ParseVerification(toolResult);
        var safeOutput = SensitiveDataRedactor.RedactText(parsed.Output);
        var isSuccess = !toolResult.IsError && parsed.ExitCode == 0 && !parsed.TimedOut;
        run.Verifications.Add(new AgentVerification
        {
            RunId = run.Id,
            StepId = stepId,
            ToolCallId = toolCall.Id,
            ToolName = toolResult.ToolName,
            Command = parsed.Command,
            ExitCode = parsed.ExitCode,
            TimedOut = parsed.TimedOut,
            IsSuccess = isSuccess,
            Output = safeOutput,
            Summary = VerificationResultParser.Summarize(safeOutput),
            CreatedAt = DateTimeOffset.Now
        });
    }

    private static void RecordArtifact(
        AgentRun run,
        Dictionary<string, AgentStep> stepByToolCallId,
        ChatToolCall? toolCall,
        AgentToolResult toolResult)
    {
        if (toolCall is null ||
            !toolResult.WasSummarized ||
            string.IsNullOrWhiteSpace(toolResult.Content) ||
            run.Artifacts.Any(artifact => string.Equals(artifact.ToolCallId, toolCall.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var stepId = stepByToolCallId.TryGetValue(toolCall.Id, out var step)
            ? step.Id
            : "";

        run.Artifacts.Add(new AgentArtifact
        {
            RunId = run.Id,
            StepId = stepId,
            ToolCallId = toolCall.Id,
            ToolName = toolResult.ToolName,
            Kind = string.IsNullOrWhiteSpace(toolResult.ArtifactKind) ? "tool_result" : toolResult.ArtifactKind,
            Summary = SensitiveDataRedactor.RedactText(toolResult.Summary),
            Content = SensitiveDataRedactor.RedactText(toolResult.Content),
            CreatedAt = DateTimeOffset.Now,
            Metadata =
            {
                ["contentLength"] = toolResult.Content.Length.ToString(),
                ["modelContentLength"] = toolResult.ContentForModel.Length.ToString(),
                ["wasSummarized"] = "true"
            }
        });
    }

    private static bool IsVerificationTool(string toolName)
    {
        return string.Equals(toolName, "run_build", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "run_test", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "run_shell", StringComparison.OrdinalIgnoreCase);
    }

    private static void RecordPlan(AgentRun run, ChatToolCall? toolCall, AgentToolResult toolResult)
    {
        if (toolCall is null ||
            !string.Equals(toolCall.Name, "update_plan", StringComparison.OrdinalIgnoreCase) ||
            toolResult.IsError)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(toolResult.Content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var summary = root.TryGetProperty("summary", out var summaryElement) &&
                          summaryElement.ValueKind == JsonValueKind.String
                ? summaryElement.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(summary) && !root.TryGetProperty("itemCount", out _))
            {
                return;
            }

            // Parse items from the original tool call arguments, not the result
            var planItems = new List<AgentPlanItem>();
            try
            {
                using var argsDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
                var argsRoot = argsDoc.RootElement;
                if (argsRoot.TryGetProperty("items", out var itemsElement) &&
                    itemsElement.ValueKind == JsonValueKind.Array)
                {
                    var order = 0;
                    foreach (var itemElement in itemsElement.EnumerateArray())
                    {
                        var title = itemElement.TryGetProperty("title", out var titleElement) &&
                                    titleElement.ValueKind == JsonValueKind.String
                            ? titleElement.GetString() ?? ""
                            : "";

                        if (string.IsNullOrWhiteSpace(title))
                        {
                            continue;
                        }

                        var statusText = itemElement.TryGetProperty("status", out var statusElement) &&
                                         statusElement.ValueKind == JsonValueKind.String
                            ? statusElement.GetString() ?? ""
                            : "";

                        var notes = itemElement.TryGetProperty("notes", out var notesElement) &&
                                    notesElement.ValueKind == JsonValueKind.String
                            ? notesElement.GetString() ?? ""
                            : "";

                        planItems.Add(new AgentPlanItem
                        {
                            Title = title,
                            Status = UpdatePlanTool.ParseStatus(statusText),
                            Notes = notes,
                            Order = order++
                        });
                    }
                }
            }
            catch (JsonException)
            {
                // If arguments can't be parsed, still update the summary
            }

            if (run.Plan is null)
            {
                run.Plan = new AgentPlan
                {
                    RunId = run.Id,
                    Summary = summary,
                    Items = planItems,
                    CreatedAt = DateTimeOffset.Now,
                    UpdatedAt = DateTimeOffset.Now
                };
            }
            else
            {
                run.Plan.Summary = summary;
                run.Plan.UpdatedAt = DateTimeOffset.Now;

                // Match existing items by title, update status/notes; add new items
                var existingByTitle = run.Plan.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                    .ToDictionary(item => item.Title, item => item, StringComparer.OrdinalIgnoreCase);

                foreach (var newItem in planItems)
                {
                    if (existingByTitle.TryGetValue(newItem.Title, out var existing))
                    {
                        existing.Status = newItem.Status;
                        existing.Notes = newItem.Notes;
                    }
                    else
                    {
                        newItem.Order = run.Plan.Items.Count;
                        run.Plan.Items.Add(newItem);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed plan update results
        }
    }

    private static VerificationInfo ParseVerification(AgentToolResult toolResult)
    {
        try
        {
            using var document = JsonDocument.Parse(toolResult.Content);
            var root = document.RootElement;
            return new VerificationInfo(
                GetString(root, "command"),
                GetInt(root, "exitCode", toolResult.IsError ? 1 : 0),
                GetBool(root, "timedOut"),
                GetString(root, "output"));
        }
        catch (JsonException)
        {
            return new VerificationInfo(toolResult.ToolName, toolResult.IsError ? 1 : 0, false, toolResult.Content);
        }
    }

    private static string ExtractDiffForPath(string diffText, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var expectedHeader = $"--- a/{normalizedPath}";
        var lines = diffText.ReplaceLineEndings("\n").Split('\n');
        var current = new List<string>();
        var isCurrentMatch = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                if (isCurrentMatch && current.Count > 0)
                {
                    return string.Join(Environment.NewLine, current).TrimEnd();
                }

                current.Clear();
                isCurrentMatch = string.Equals(line, expectedHeader, StringComparison.OrdinalIgnoreCase);
            }

            if (current.Count > 0 || line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                current.Add(line);
            }
        }

        return isCurrentMatch && current.Count > 0
            ? string.Join(Environment.NewLine, current).TrimEnd()
            : diffText;
    }

    private static IReadOnlyList<ChangedFileInfo> ParseChangedFiles(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (root.TryGetProperty("changedFiles", out var changedFiles) &&
                changedFiles.ValueKind == JsonValueKind.Array)
            {
                return changedFiles
                    .EnumerateArray()
                    .Select(ParseChangedFile)
                    .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                    .ToList();
            }

            var single = ParseChangedFile(root);
            return string.IsNullOrWhiteSpace(single.Path) ? [] : [single];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ChangedFileInfo ParseChangedFile(JsonElement element)
    {
        return new ChangedFileInfo(
            GetString(element, "path"),
            GetInt(element, "oldChars"),
            GetInt(element, "newChars", GetInt(element, "chars")),
            GetString(element, "contentSnapshot"),
            GetString(element, "postChangeHash"));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : defaultValue;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
    }

    private sealed record ChangedFileInfo(string Path, int OldChars, int NewChars, string ContentSnapshot, string PostChangeHash);
    private sealed record VerificationInfo(string Command, int ExitCode, bool TimedOut, string Output);
}
