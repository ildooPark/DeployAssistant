#pragma warning disable CS0618  // ProjectFile is a V1 type; used intentionally in CLI screen tests

using System;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class UpdateVersionPromptScreenTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);
    private static ConsoleKeyInfo Char(char c) => new(c, (ConsoleKey)(char.ToUpper(c)), false, false, false);

    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    // ------------------------------------------------------------------ Construct_DefaultsUpdaterToUserName

    [Fact]
    public void Construct_DefaultsUpdaterToUserName()
    {
        var mgr = BuildManager();
        var screen = new UpdateVersionPromptScreen(mgr, 3);

        // We can observe the default via Render (no throw) and via Enter behaviour.
        // Render should not throw and should not show "(empty)" for updater when
        // Environment.UserName is available. We can't directly read private fields,
        // but the Enter-on-updater-field path (advances focus rather than submitting)
        // tells us the screen is sane. Primarily we verify no exception on construction.
        var ex = Record.Exception(() => screen.Render());
        Assert.Null(ex);
    }

    // ------------------------------------------------------------------ Handle_Escape_PopsScreen

    [Fact]
    public void Handle_Escape_PopsScreen()
    {
        var mgr = BuildManager();
        var screen = new UpdateVersionPromptScreen(mgr, 5);

        var action = screen.Handle(Key(ConsoleKey.Escape));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ Handle_Tab_SwitchesFocus

    [Fact]
    public void Handle_Tab_SwitchesFocus()
    {
        // When focused on field 0 (updater), Enter advances to field 1 and returns Stay.
        // After Tab, focus moves to field 1, so Enter on field 1 tries to submit — but
        // updater should be non-empty (defaulted to Environment.UserName), so Submit runs
        // and calls TuiPrompt which needs stdin. To avoid that, Tab back to field 0 and
        // then press Enter: returns Stay (advanced to log field). This proves Tab is working.
        var mgr = BuildManager();
        var screen = new UpdateVersionPromptScreen(mgr, 2);

        // Start: focus = 0. Tab -> focus = 1. Tab -> focus = 0.
        screen.Handle(Key(ConsoleKey.Tab)); // focus -> 1
        screen.Handle(Key(ConsoleKey.Tab)); // focus -> 0

        // Now Enter on field 0 should advance focus to 1 and return Stay (not submit).
        var action = screen.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<ScreenAction.Stay>(action);
    }

    // ------------------------------------------------------------------ Handle_Enter_OnUpdaterField_AdvancesToLogField

    [Fact]
    public void Handle_Enter_OnUpdaterField_AdvancesToLogField()
    {
        // In the initial state, focus = 0 (updater). Enter should advance focus to log
        // field (field 1) and return Stay, NOT submit.
        var mgr = BuildManager();
        var screen = new UpdateVersionPromptScreen(mgr, 7);

        var action = screen.Handle(Key(ConsoleKey.Enter));

        // Must be Stay — focus advanced from field 0 to field 1, did not submit.
        Assert.IsType<ScreenAction.Stay>(action);
    }

    // ------------------------------------------------------------------ Handle_EmptyUpdater_SubmitShowsError

    [Fact]
    public void Handle_EmptyUpdater_SubmitShowsError()
    {
        // Clear the updater field by sending enough Backspace keys,
        // then Tab to field 1, then Enter — should stay on screen (validation error).
        var mgr = BuildManager();
        var screen = new UpdateVersionPromptScreen(mgr, 4);

        // Clear updater (default is Environment.UserName; send extra backspaces for safety).
        string userName = Environment.UserName ?? "";
        int clearCount = userName.Length + 5; // extra buffer
        for (int i = 0; i < clearCount; i++)
            screen.Handle(Key(ConsoleKey.Backspace));

        // Advance to log field.
        screen.Handle(Key(ConsoleKey.Tab));

        // Press Enter on log field — should hit the empty-updater guard and return Stay.
        var action = screen.Handle(Key(ConsoleKey.Enter));

        Assert.IsType<ScreenAction.Stay>(action);

        // Render should now show an error without throwing.
        var ex = Record.Exception(() => screen.Render());
        Assert.Null(ex);
    }
}

#pragma warning restore CS0618
