using AIChat.Abstractions.Configuration;
using AIChat.Application.Artifacts;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Prompting;

// Wave 7 (parity plan §7 Wave 7) third slice: the
// @-reference in the composer's prompt resolves to a
// project-level InputArtifact, which AgentRequest
// Factory's Build path threads into the system prompt
// via SystemPromptContext.InputArtifactRefs. The tests
// here verify that contract end-to-end without
// running a real agent — the agent-loop surface (the
// system prompt text) is what actually matters, and
// the SystemPromptBuilder is the unit that consumes
// the refs.
//
// If these tests break, the @-reference flow stops
// working even if the parser / Source registry are
// fine — the failure mode is "the user pastes
// @web:abc and the agent has no idea what they're
// referencing", which is the worst possible silent
// failure.
public class InputArtifactRefSystemPromptTests
{
    [Fact]
    public void Build_IncludesInputArtifactRefs_InSystemPrompt()
    {
        // The artifact's "ref id" is what
        // AgentRequestFactory's BuildPromptRefs
        // produces and the SystemPromptContext's
        // InputArtifactRefs list carries. The
        // SystemPromptBuilder appends them under
        // "输入 artifact:" with one bullet per ref
        // so the agent can address them by id
        // (e.g. "show me input-artifact:abc in full").
        var refs = new[]
        {
            "input-artifact:abc [web] https://example.com/article: 文章正文",
            "input-artifact:def [clipboard] 剪贴板快照: 一些内容",
        };
        var prompt = BuildPrompt(refs);
        Assert.Contains("输入 artifact", prompt);
        Assert.Contains("input-artifact:abc", prompt);
        Assert.Contains("input-artifact:def", prompt);
    }

    [Fact]
    public void Build_NoInputArtifactRefs_NoSourcesSection()
    {
        // The "输入 artifact" section is the agent's
        // cue that the user attached something. When
        // no refs are present, the section is
        // omitted — keeps the system prompt tight
        // for the no-attachment run.
        var prompt = BuildPrompt([]);
        Assert.DoesNotContain("输入 artifact", prompt);
    }

    [Fact]
    public void Build_TrimsContextRefsWhenListIsLong()
    {
        // AgentRequestFactory caps at 8 refs. The
        // SystemPromptBuilder takes whatever it's
        // given — the cap is upstream. Verify the
        // builder doesn't add its own implicit cap
        // (a follow-up slice that wants a smaller
        // cap can layer it on top of the factory's
        // 8-ref default).
        var refs = Enumerable.Range(0, 20)
            .Select(i => $"input-artifact:row{i} [text] 行 {i}")
            .ToArray();
        var prompt = BuildPrompt(refs);
        // All 20 should appear (no builder-side cap).
        Assert.Equal(20, refs.Count(r => prompt.Contains(r)));
    }

    private static string BuildPrompt(IReadOnlyList<string> inputArtifactRefs)
    {
        // The system prompt builder takes a context
        // shaped bag. We only care about the
        // InputArtifactRefs surface here; the rest
        // gets sensible defaults so the prompt
        // builds without a real ProjectWorkspace.
        var context = new SystemPromptContext
        {
            EnabledToolIds = ["read_file"],
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
            {
                ["read_file"] = ToolPermissionMode.AutoReadOnly,
            },
            ProjectName = "TestProject",
            ProjectPath = "",
            WorkspaceSummary = "测试工作区",
            ContextRefs = [],
            MemorySnippets = [],
            InputArtifactRefs = inputArtifactRefs,
            ExecutionMode = "Default",
            ModelProfileName = "TestModel",
        };
        var builder = new SystemPromptBuilder();
        return builder.Build(context);
    }
}
