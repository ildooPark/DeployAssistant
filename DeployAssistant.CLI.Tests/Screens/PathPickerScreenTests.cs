using System;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.CLI.Screens;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class PathPickerScreenTests
{
    private static ConsoleKeyInfo Char(char c) => new(c, (ConsoleKey)c, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);

    [Fact]
    public void Tab_OffersCandidatesFromProvider()
    {
        var screen = new PathPickerScreen(
            PathPickerScreen.Mode.Switch,
            _ => new[] { "alpha", "beta" });

        foreach (char c in @"C:\") screen.Handle(Char(c));
        screen.Handle(Key(ConsoleKey.Tab));

        Assert.True(screen.IsInCandidateMode);
        Assert.Equal(new[] { "alpha", "beta" }, screen.CurrentCandidates);
    }

    [Fact]
    public void Esc_OutsideCandidateMode_RequestsPop()
    {
        var screen = new PathPickerScreen(
            PathPickerScreen.Mode.Switch,
            _ => Array.Empty<string>());

        var action = screen.Handle(Key(ConsoleKey.Escape));
        Assert.IsType<DeployAssistant.CLI.Engine.ScreenAction.Pop>(action);
    }
}
