using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace DeployAssistant.CLI.Tests;

public class CliVersionTests
{
    [Fact]
    public void Display_IsBareSemver()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), CliVersion.Display);
    }

    [Fact]
    public void Display_IsNotTheUnsetFallback()
    {
        // Guards the csproj: before <AssemblyVersion> was set, --version reported "0.0.0".
        Assert.NotEqual("0.0.0", CliVersion.Display);
    }

    [Fact]
    public void Display_TrimsRevisionFromTheAssemblyVersion()
    {
        var v = typeof(CliVersion).Assembly.GetName().Version;
        Assert.NotNull(v);
        Assert.Equal($"{v!.Major}.{v.Minor}.{v.Build}", CliVersion.Display);
    }

    [Fact]
    public void Display_TracksTheCliReleaseLine()
    {
        // CLI versions independently of the GUI (docs/release-process.md); bump both
        // this expectation and the csproj together when the CLI release line moves.
        Assert.Equal("2.0.0", CliVersion.Display);
    }

    [Fact]
    public void Banner_PrefixesTheVersionWithTheProductName()
    {
        Assert.Equal($"DeployAssistant CLI {CliVersion.Display}", CliVersion.Banner);
    }

    [Fact]
    public void Banner_ContainsTheSmokeTestedProductName()
    {
        // The CI smoke test greps stdout for "DeployAssistant" on the --version path.
        Assert.Contains("DeployAssistant", CliVersion.Banner);
    }

    [Fact]
    public void Banner_CarriesNoUnescapedSpectreMarkup()
    {
        // PR #19: literal brackets in a markup string crash the TUI at startup, and the
        // banner is embedded in markup by MainScreen / TopMenuScreen.
        Assert.DoesNotContain("[", CliVersion.Banner);
        Assert.DoesNotContain("]", CliVersion.Banner);
    }
}
