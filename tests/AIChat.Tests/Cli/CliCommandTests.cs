namespace AIChat.Tests.Cli;

public sealed class CliCommandTests
{
    [Fact]
    public void Parse_UsesTuiWhenNoArgumentsAreProvided()
    {
        var command = CliCommand.Parse([]);

        Assert.Equal("tui", command.Name);
        Assert.Empty(command.Positionals);
    }

    [Fact]
    public void Parse_AcceptsDataDirectoryBeforeCommand()
    {
        var command = CliCommand.Parse(["--data-dir", "tmp-data", "doctor"]);

        Assert.Equal("doctor", command.Name);
        Assert.Equal("tmp-data", command.DataDirectory);
        Assert.Empty(command.Positionals);
    }

    [Fact]
    public void Parse_AcceptsDataDirectoryAfterCommand()
    {
        var command = CliCommand.Parse(["doctor", "--data-dir", "tmp-data"]);

        Assert.Equal("doctor", command.Name);
        Assert.Equal("tmp-data", command.DataDirectory);
        Assert.Empty(command.Positionals);
    }

    [Fact]
    public void Parse_KeepsCommandPositionalsAfterGlobalOptions()
    {
        var command = CliCommand.Parse(["--data-dir", "tmp-data", "context", "project", "overview", "--tokens", "300"]);

        Assert.Equal("context", command.Name);
        Assert.Equal(["project", "overview"], command.Positionals);
        Assert.Equal("300", command.GetOption("tokens"));
        Assert.Equal("tmp-data", command.DataDirectory);
    }

    [Fact]
    public void Parse_TreatsVersionAsCommand()
    {
        var command = CliCommand.Parse(["--version"]);

        Assert.Equal("--version", command.Name);
    }
}
