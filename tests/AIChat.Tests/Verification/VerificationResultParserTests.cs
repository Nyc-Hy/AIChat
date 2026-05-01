using AIChat.Application.Verification;

namespace AIChat.Tests.Verification;

public sealed class VerificationResultParserTests
{
    [Fact]
    public void Summarize_ReturnsEmptyForEmptyOutput()
    {
        Assert.Equal("", VerificationResultParser.Summarize(""));
        Assert.Equal("", VerificationResultParser.Summarize("   "));
    }

    [Fact]
    public void Summarize_ExtractsErrorLines()
    {
        var output = """
            Build started
            src/App.cs(10,5): error CS1001: Unexpected token
            src/Util.cs(20,3): error CS0246: Type not found
            Build FAILED
            """;

        var summary = VerificationResultParser.Summarize(output);

        Assert.Contains("error CS1001", summary);
        Assert.Contains("error CS0246", summary);
        Assert.Contains("Build FAILED", summary);
        Assert.DoesNotContain("Build started", summary);
    }

    [Fact]
    public void Summarize_ExtractsWarningLinesWhenNoErrors()
    {
        var output = """
            Build started
            src/App.cs(10,5): warning CS0219: Variable assigned but never used
            src/Util.cs(20,3): warning CS0168: Variable declared but never used
            Build succeeded
            """;

        var summary = VerificationResultParser.Summarize(output);

        Assert.Contains("warning CS0219", summary);
        Assert.Contains("warning CS0168", summary);
        Assert.DoesNotContain("Build started", summary);
    }

    [Fact]
    public void Summarize_ReturnsTailWhenNoErrorsOrWarnings()
    {
        var output = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"Line {i}"));

        var summary = VerificationResultParser.Summarize(output, maxLines: 5);

        Assert.Contains("Line 30", summary);
        Assert.Contains("Line 26", summary);
        Assert.DoesNotContain("Line 1", summary);
    }

    [Fact]
    public void Summarize_LimitsOutputToMaxLines()
    {
        var lines = Enumerable.Range(1, 50).Select(i => $"src/File.cs(1,1): error CS{i}: Error {i}");
        var output = string.Join('\n', lines);

        var summary = VerificationResultParser.Summarize(output, maxLines: 5);

        var summaryLines = summary.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.True(summaryLines.Count <= 6); // 5 lines + possible "..."
    }

    [Fact]
    public void Summarize_HandlesTestFailureOutput()
    {
        var output = """
            Starting test execution, please wait...
            A total of 1 test files matched the specified pattern.
            Failed MyTest1 [42 ms]
            Error Message:
             Assert.Equal() Failure: Values differ
            Stack Trace:
             at MyTests.Class.Test() in Test.cs:line 15

            Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
            """;

        var summary = VerificationResultParser.Summarize(output);

        Assert.Contains("Failed", summary);
    }

    [Fact]
    public void Summarize_PrefersErrorsOverWarnings()
    {
        var output = """
            src/App.cs(10,5): warning CS0219: Unused variable
            src/App.cs(20,5): error CS1001: Unexpected token
            Build FAILED
            """;

        var summary = VerificationResultParser.Summarize(output, maxLines: 2);

        Assert.Contains("error CS1001", summary);
        // Warning should not appear when errors fill the budget
        Assert.DoesNotContain("warning CS0219", summary);
    }
}
