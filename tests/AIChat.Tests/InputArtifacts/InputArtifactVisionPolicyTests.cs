using AIChat.Application.Artifacts;
using AIChat.Domain.Artifacts;

namespace AIChat.Tests.Artifacts;

public sealed class InputArtifactVisionPolicyTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "AIChatVisionPolicyTests", Guid.NewGuid().ToString("N"));

    public InputArtifactVisionPolicyTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Evaluate_WhenModelDoesNotSupportVision_ReturnsModelStatus()
    {
        var artifact = ImageArtifact("a.png", CreateFile("a.png", 4));

        var decision = InputArtifactVisionPolicy.EvaluateSingle(artifact, modelSupportsVision: false);

        Assert.True(decision.IsImage);
        Assert.False(decision.CanSend);
        Assert.Equal(InputArtifactVisionStatus.ModelDoesNotSupportVision, decision.Status);
        Assert.Equal("当前模型不支持图片输入，仅作为附件引用", decision.StatusText);
    }

    [Fact]
    public void Evaluate_WithSendableImage_ReturnsMediaTypeAndPath()
    {
        var path = CreateFile("screen.png", 4);
        var artifact = ImageArtifact("screen.png", path);

        var decision = InputArtifactVisionPolicy.EvaluateSingle(artifact, modelSupportsVision: true);

        Assert.True(decision.CanSend);
        Assert.Equal("image/png", decision.MediaType);
        Assert.Equal(path, decision.StoredPath);
        Assert.Equal(4, decision.SizeBytes);
    }

    [Fact]
    public void Evaluate_EnforcesImageCountLimit()
    {
        var artifacts = Enumerable.Range(1, 4)
            .Select(index => ImageArtifact($"screen-{index}.png", CreateFile($"screen-{index}.png", 4), day: index))
            .ToList();

        var decisions = InputArtifactVisionPolicy.Evaluate(artifacts, modelSupportsVision: true);

        Assert.Equal(3, decisions.Count(decision => decision.CanSend));
        Assert.Single(decisions, decision => decision.Status == InputArtifactVisionStatus.TooManyImages);
    }

    [Fact]
    public void Evaluate_EnforcesTotalSizeLimit()
    {
        var first = ImageArtifact("first.png", CreateFile("first.png", 4 * 1024 * 1024), day: 2);
        var second = ImageArtifact("second.png", CreateFile("second.png", 4 * 1024 * 1024), day: 1);
        var third = ImageArtifact("third.png", CreateFile("third.png", 1), day: 0);

        var decisions = InputArtifactVisionPolicy.Evaluate([first, second, third], modelSupportsVision: true);

        Assert.Equal(2, decisions.Count(decision => decision.CanSend));
        Assert.Single(decisions, decision => decision.Status == InputArtifactVisionStatus.TotalSizeTooLarge);
    }

    [Fact]
    public void Evaluate_MissingStoredFileReturnsMissingStatus()
    {
        var artifact = ImageArtifact("missing.png", Path.Combine(_tempDir, "missing.png"));

        var decision = InputArtifactVisionPolicy.EvaluateSingle(artifact, modelSupportsVision: true);

        Assert.Equal(InputArtifactVisionStatus.MissingStoredFile, decision.Status);
        Assert.False(decision.CanSend);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private static InputArtifact ImageArtifact(string fileName, string path, int day = 1)
    {
        return new InputArtifact
        {
            Kind = InputArtifactKind.Screenshot,
            FileName = fileName,
            MimeType = "image/png",
            CreatedAt = DateTimeOffset.Parse($"2026-01-{day + 1:00}T00:00:00Z"),
            Metadata = { ["storedPath"] = path }
        };
    }
}
