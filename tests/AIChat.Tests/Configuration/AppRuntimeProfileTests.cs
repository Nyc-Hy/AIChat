using AIChat.Abstractions.Configuration;

namespace AIChat.Tests.Configuration;

public sealed class AppRuntimeProfileTests
{
    [Fact]
    public void ResolveDataDirectory_RequiresAbsolutePath()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AppRuntimeProfile.ResolveDataDirectory("relative/test-profile"));

        Assert.Contains(AppRuntimeProfile.IsolatedDataRootEnvironmentVariable, error.Message);
    }

    [Fact]
    public void ResolveDataDirectory_NormalizesAbsolutePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "AIChatProfile", "..", "isolated");

        var resolved = AppRuntimeProfile.ResolveDataDirectory(path);

        Assert.Equal(Path.GetFullPath(path), resolved);
    }
}
