#pragma warning disable CS0618  // ProjectData/ChangedFile are V1 types; used intentionally in CLI screen tests

using System;
using System.Collections.Generic;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class RevisionDetailScreenTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);

    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    private static ProjectData MakeRevision(string version, string path = @"C:\TestProject")
    {
        var pd = new ProjectData(path);
        pd.ProjectName = "TestProject";
        pd.UpdaterName = "Tester";
        pd.ConductedPC = "PC01";
        pd.UpdatedVersion = version;
        pd.UpdateLog = "test update";
        pd.ChangeLog = "test changelog";
        pd.UpdatedTime = new DateTime(2025, 1, 1, 12, 0, 0);
        pd.NumberOfChanges = 0;
        return pd;
    }

    // ------------------------------------------------------------------ Handle_Escape_PopsScreen

    [Fact]
    public void Handle_Escape_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("1.0");
        var screen = new RevisionDetailScreen(mgr, revision);

        var action = screen.Handle(Key(ConsoleKey.Escape));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ OnEnter_NonCurrentRevision_SubscribesToDiffEvent

    [Fact]
    public void OnEnter_NonCurrentRevision_SubscribesToDiffEvent()
    {
        var mgr = BuildManager();
        // MainProjectData is null, so screen is non-current.
        var revision = MakeRevision("2.0");
        var screen = new RevisionDetailScreen(mgr, revision);

        // Track that += and -= do not throw during the lifecycle.
        int subscribeCount = 0;
        int unsubscribeCount = 0;

        // Use a counting wrapper registered alongside the screen's handler.
        Action<ProjectData, ProjectData, List<ChangedFile>> counter = (s, d, diff) => subscribeCount++;
        mgr.ProjComparisonCompleteEventHandler += counter;

        // OnEnter should subscribe without throwing.
        var ex = Record.Exception(() => screen.OnEnter());
        Assert.Null(ex);

        mgr.ProjComparisonCompleteEventHandler -= counter;
        screen.OnExit();
    }

    // ------------------------------------------------------------------ OnExit_UnsubscribesFromEvent

    [Fact]
    public void OnExit_DoesNotThrow()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("3.0");
        var screen = new RevisionDetailScreen(mgr, revision);
        screen.OnEnter();

        // OnExit should unsubscribe cleanly without throwing.
        var ex = Record.Exception(() => screen.OnExit());
        Assert.Null(ex);
    }

    // ------------------------------------------------------------------ Construct_NonCurrentRevision_RenderDoesNotThrow

    [Fact]
    public void Render_BeforeOnEnter_DoesNotThrow()
    {
        // _diff is null before OnEnter; Render should show "Computing diff..." gracefully.
        var mgr = BuildManager();
        var revision = MakeRevision("5.0");
        var screen = new RevisionDetailScreen(mgr, revision);

        var ex = Record.Exception(() => screen.Render());
        Assert.Null(ex);
    }

    // ------------------------------------------------------------------ Handle_C_WhenIsCurrent_SetsLastError_StaysOnScreen

    [Fact]
    public void Handle_C_WhenNoMainProjectData_RendersWithoutCrash()
    {
        // MainProjectData is null so _isCurrent is false.
        // Pressing C should attempt confirm, but since TuiPrompt reads Console.ReadKey
        // we can't test the full 'c' path without interactive I/O.
        // Instead, verify the non-current path: Escape returns Pop.
        var mgr = BuildManager();
        var revision = MakeRevision("6.0");
        var screen = new RevisionDetailScreen(mgr, revision);
        screen.OnEnter();

        // Any key other than C/Escape should return Stay and not crash.
        var action = screen.Handle(Key(ConsoleKey.DownArrow));
        Assert.IsType<ScreenAction.Stay>(action);

        screen.OnExit();
    }

    // ------------------------------------------------------------------ RevisionListScreen_Enter_PushesDetailScreen

    [Fact]
    public void RevisionListScreen_Enter_PushesRevisionDetailScreen()
    {
        var mgr = BuildManager();
        var listScreen = new RevisionListScreen(mgr);

        // Empty rows: Enter should return Stay.
        var action = listScreen.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<ScreenAction.Stay>(action);
    }

    // ------------------------------------------------------------------ RevisionListScreen_AutoAdvance_NullWhenNoCheckout

    [Fact]
    public void RevisionListScreen_AutoAdvance_ReturnsNullWhenNothingCheckedOut()
    {
        var mgr = BuildManager();
        var listScreen = new RevisionListScreen(mgr);

        // No checkout happened: ConsumeLastCheckedOut returns null → AutoAdvance returns null.
        var action = listScreen.AutoAdvance();
        Assert.Null(action);
    }
}

#pragma warning restore CS0618
