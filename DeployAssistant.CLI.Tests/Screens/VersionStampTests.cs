using System;
using System.IO;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using Spectre.Console;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

/// <summary>
/// The CLI version is surfaced inside the TUI (not only behind --version), so both the
/// project card and the top menu must render it.
/// </summary>
public class VersionStampTests
{
    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    /// <summary>Renders through a throwaway plain-text console so the output can be asserted on.</summary>
    private static string Capture(Action render)
    {
        var original = AnsiConsole.Console;
        var writer = new StringWriter();
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });
            render();
        }
        finally
        {
            AnsiConsole.Console = original;
        }
        return writer.ToString();
    }

    [Fact]
    public void MainScreen_Render_ShowsTheCliVersion()
    {
        var screen = new MainScreen(BuildManager());

        string output = Capture(screen.Render);

        Assert.Contains(CliVersion.Banner, output);
    }

    [Fact]
    public void TopMenuScreen_Render_ShowsTheCliVersion()
    {
        var screen = new TopMenuScreen(loaded: false);

        string output = Capture(screen.Render);

        Assert.Contains(CliVersion.Banner, output);
    }

    [Fact]
    public void MainScreen_Render_DoesNotLeakMarkupTags()
    {
        // A dim version line must come out as plain text once markup is parsed;
        // a leaked "[dim]" would mean the tag was escaped instead of applied.
        var screen = new MainScreen(BuildManager());

        string output = Capture(screen.Render);

        Assert.DoesNotContain("[dim]", output);
    }
}
