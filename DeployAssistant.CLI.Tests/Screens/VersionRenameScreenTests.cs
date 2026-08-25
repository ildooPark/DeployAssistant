#pragma warning disable CS0618  // ProjectData is a V1 type; used intentionally in CLI screen tests

using System;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class VersionRenameScreenTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k, char c = '\0') =>
        new(c, k, false, false, false);

    private static ConsoleKeyInfo Char(char c) =>
        new(c, ConsoleKey.NoName, false, false, false);

    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    private static ProjectData MakeRevision(string version, string log = "test update")
    {
        var pd = new ProjectData(@"C:\TestProject");
        pd.ProjectName = "TestProject";
        pd.UpdaterName = "Tester";
        pd.ConductedPC = "PC01";
        pd.UpdatedVersion = version;
        pd.UpdateLog = log;
        pd.ChangeLog = "test changelog";
        pd.UpdatedTime = new DateTime(2025, 1, 1, 12, 0, 0);
        pd.NumberOfChanges = 0;
        return pd;
    }

    private static void Type(VersionRenameScreen screen, string text)
    {
        foreach (char c in text) screen.Handle(Char(c));
    }

    // ------------------------------------------------------------------ Construct_PrefillsBothFields

    [Fact]
    public void Construct_PrefillsFieldsWithTheCurrentValues()
    {
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0", "initial drop"));

        Assert.Equal("1.0", screen.NameText);
        Assert.Equal("initial drop", screen.LogText);
        Assert.Equal(0, screen.FocusedField);
    }

    // ------------------------------------------------------------------ Handle_Escape_PopsScreen

    [Fact]
    public void Handle_Escape_PopsScreen()
    {
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0"));

        Assert.IsType<ScreenAction.Pop>(screen.Handle(Key(ConsoleKey.Escape)));
    }

    // ------------------------------------------------------------------ Handle_Tab_MovesFocus

    [Fact]
    public void Handle_Tab_CyclesFocusBetweenTheTwoFields()
    {
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0"));

        screen.Handle(Key(ConsoleKey.Tab));
        Assert.Equal(1, screen.FocusedField);

        screen.Handle(Key(ConsoleKey.Tab));
        Assert.Equal(0, screen.FocusedField);
    }

    // ------------------------------------------------------------------ Handle_Typing_EditsTheFocusedField

    [Fact]
    public void Handle_Typing_EditsOnlyTheFocusedField()
    {
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0", "initial drop"));

        Type(screen, ".1");                     // field 0
        screen.Handle(Key(ConsoleKey.Tab));
        Type(screen, "!");                      // field 1

        Assert.Equal("1.0.1", screen.NameText);
        Assert.Equal("initial drop!", screen.LogText);
    }

    // ------------------------------------------------------------------ Handle_Enter_OnFirstField_AdvancesFocus

    [Fact]
    public void Handle_EnterOnFirstField_AdvancesToTheLogField()
    {
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0"));

        var action = screen.Handle(Key(ConsoleKey.Enter));

        Assert.IsType<ScreenAction.Stay>(action);
        Assert.Equal(1, screen.FocusedField);
        Assert.Equal(VersionRenameScreen.Phase.Edit, screen.CurrentPhase);
    }

    // ------------------------------------------------------------------ Submit_WithNoEdits_IsRejected

    [Fact]
    public void Submit_WithNothingEdited_ReportsNothingToChange()
    {
        // Both fields still hold the original values, so both are sent as null
        // ("leave alone") — the screen refuses before bothering the manager.
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0", "initial drop"));

        screen.Handle(Key(ConsoleKey.Enter));   // focus → log field
        var action = screen.Handle(Key(ConsoleKey.Enter));   // submit

        Assert.IsType<ScreenAction.Stay>(action);
        Assert.Equal(VersionRenameScreen.Phase.Edit, screen.CurrentPhase);
        Assert.Equal("Nothing to change.", screen.LastError);
    }

    // ------------------------------------------------------------------ Submit_WhenRejected_ShowsTheRealReason

    [Fact]
    public void Submit_WhenRequestRejected_RendersTheReportedReason()
    {
        // No metadata is loaded, so RequestRenameVersion fails with "No project is loaded."
        // delivered on VersionRenameCompleteEventHandler — that text must reach the screen
        // instead of a generic "rename failed".
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0"));

        Type(screen, ".1");                     // name becomes "1.0.1"
        screen.Handle(Key(ConsoleKey.Tab));     // focus → log field
        var action = screen.Handle(Key(ConsoleKey.Enter));   // submit

        Assert.IsType<ScreenAction.Stay>(action);
        Assert.Equal(VersionRenameScreen.Phase.Edit, screen.CurrentPhase);
        Assert.NotNull(screen.LastError);
        Assert.Contains("No project is loaded", screen.LastError!);
        Assert.Equal(0, screen.FocusedField);   // focus returns to the offending field
    }

    // ------------------------------------------------------------------ Submit_LogOnlyEdit_StillReachesTheManager

    [Fact]
    public void Submit_LogOnlyEdit_IsSentToTheManager()
    {
        // A log-only edit must not be short-circuited as "nothing to change".
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0", "initial drop"));

        screen.Handle(Key(ConsoleKey.Tab));     // focus → log field
        Type(screen, " (hotfix)");
        screen.Handle(Key(ConsoleKey.Enter));   // submit

        Assert.NotEqual("Nothing to change.", screen.LastError);
        Assert.Contains("No project is loaded", screen.LastError!);
    }

    // ------------------------------------------------------------------ Render_DoesNotThrow

    [Fact]
    public void Render_WithBracketedText_DoesNotCrashSpectreMarkup()
    {
        var mgr = BuildManager();
        var screen = new VersionRenameScreen(mgr, MakeRevision("1.0-[rc1]", "log with [[brackets]]"));

        Assert.Null(Record.Exception(() => screen.Render()));

        // Drive it into an error state and re-render — the message is user text too.
        screen.Handle(Key(ConsoleKey.Enter));
        screen.Handle(Key(ConsoleKey.Enter));
        Assert.Null(Record.Exception(() => screen.Render()));
    }

    // ------------------------------------------------------------------ RevisionDetailScreen_R_PushesRenameScreen

    [Fact]
    public void RevisionDetailScreen_R_PushesVersionRenameScreen()
    {
        var mgr = BuildManager();
        var detail = new RevisionDetailScreen(mgr, MakeRevision("2.0"));

        var action = detail.Handle(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));

        var push = Assert.IsType<ScreenAction.Push>(action);
        Assert.IsType<VersionRenameScreen>(push.Next);
    }
}

#pragma warning restore CS0618
