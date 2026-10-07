using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using Spectre.Console;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

/// <summary>
/// The CLI version is surfaced inside the TUI (not only behind --version): the frame header
/// carries it on every screen, including the no-project landing menu.
/// </summary>
public class VersionStampTests
{
    private static readonly Regex AnsiEscape = new(@"\x1b\[[0-9;?]*[A-Za-z]");

    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    private static string Compose(Screen screen) =>
        string.Join("\n", FrameRenderer.Compose(new List<Screen> { screen }, 100, 30,
                new FrameRenderer.Target(ansi: true, ColorSystem.Standard))
            .Select(l => AnsiEscape.Replace(l, "")));

    [Fact]
    public void MainScreen_Frame_ShowsTheCliVersion()
    {
        Assert.Contains(CliVersion.Banner, Compose(new MainScreen(BuildManager())));
    }

    [Fact]
    public void TopMenuScreen_Frame_ShowsTheCliVersion()
    {
        Assert.Contains(CliVersion.Banner, Compose(new TopMenuScreen(loaded: false)));
    }

    [Fact]
    public void MainScreen_Frame_DoesNotLeakMarkupTags()
    {
        // Styled text must come out as plain text once markup is parsed; a leaked
        // "[grey]" would mean a tag was escaped instead of applied.
        string output = Compose(new MainScreen(BuildManager()));

        Assert.DoesNotContain("[grey]", output);
        Assert.DoesNotContain("[aqua", output);
    }
}
