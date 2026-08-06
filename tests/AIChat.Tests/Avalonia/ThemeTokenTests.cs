using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace AIChat.Tests.Avalonia;

public sealed class ThemeTokenTests
{
    [Fact]
    public void DarkTheme_OverridesEveryBaseBrushInstance()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var resources = Path.Combine(repositoryRoot, "src", "AIChat.App.Avalonia", "Resources");
        var light = XDocument.Load(Path.Combine(resources, "Tokens.axaml"));
        var dark = XDocument.Load(Path.Combine(resources, "Tokens.Dark.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        static HashSet<string> BrushKeys(XDocument document, XNamespace xNamespace) => document
            .Descendants()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .Select(element => (string?)element.Attribute(xNamespace + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

        var lightBrushes = BrushKeys(light, x);
        var darkBrushes = BrushKeys(dark, x);

        Assert.NotEmpty(lightBrushes);
        Assert.Empty(lightBrushes.Except(darkBrushes));
        Assert.All(light.Descendants().Where(element => element.Name.LocalName == "SolidColorBrush"), brush =>
            Assert.Contains("DynamicResource", (string?)brush.Attribute("Color") ?? ""));
    }

    [Fact]
    public void ThemeAwareBrushConsumers_UseDynamicResource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var appRoot = Path.Combine(repositoryRoot, "src", "AIChat.App.Avalonia");
        var staticBrushPattern = new Regex(
            @"\{StaticResource\s+[A-Za-z0-9]+Brush\}",
            RegexOptions.CultureInvariant);

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            Assert.DoesNotMatch(staticBrushPattern, File.ReadAllText(path));
        }
    }
}
