#pragma warning disable CS0618  // ProjectData/ProjectFile are V1 types; used intentionally in CLI screen tests

using System;
using System.Collections.Generic;
using System.Reflection;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class CheckoutGateScreenTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k, char c = '\0') =>
        new(c, k, false, false, false);

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

    private static ProjectFile MakeFile(string relPath, DataState state = DataState.Modified)
    {
        var f = new ProjectFile();
        f.DataRelPath = relPath;
        f.DataName = System.IO.Path.GetFileName(relPath);
        f.DataState = state;
        return f;
    }

    // ------------------------------------------------------------------ Construct_DoesNotThrow

    [Fact]
    public void Construct_DoesNotThrow()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("1.0");

        var ex = Record.Exception(() => new CheckoutGateScreen(mgr, revision));

        Assert.Null(ex);
    }

    // ------------------------------------------------------------------ Handle_Escape_InRunningPhase_StaysOnScreen

    [Fact]
    public void Handle_EscapeInRunningPhase_StaysOnScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("1.0");
        var screen = new CheckoutGateScreen(mgr, revision);
        // Phase starts as Running (OnEnter not called — simulates mid-flight integrity check)

        var action = screen.Handle(Key(ConsoleKey.Escape));

        // Running phase: no pop (OnEnter blocks; escape cannot reach Handle in practice)
        Assert.IsType<ScreenAction.Stay>(action);
    }

    // ------------------------------------------------------------------ Handle_Escape_InCleanPhase_PopsScreen

    [Fact]
    public void Handle_EscapeInCleanPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("2.0");
        var screen = new CheckoutGateScreen(mgr, revision);
        screen.SetPhaseForTesting(CheckoutGateScreen.Phase.Clean);

        var action = screen.Handle(Key(ConsoleKey.Escape));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ Handle_N_InCleanPhase_PopsScreen

    [Fact]
    public void Handle_NInCleanPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("3.0");
        var screen = new CheckoutGateScreen(mgr, revision);
        screen.SetPhaseForTesting(CheckoutGateScreen.Phase.Clean);

        var action = screen.Handle(Key(ConsoleKey.N, 'n'));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ Handle_Escape_InDirtyPhase_PopsScreen

    [Fact]
    public void Handle_EscapeInDirtyPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("4.0");
        var screen = new CheckoutGateScreen(mgr, revision);
        var mods = new List<ProjectFile> { MakeFile("app.dll") };
        screen.SetPhaseForTesting(CheckoutGateScreen.Phase.Dirty, mods);

        var action = screen.Handle(Key(ConsoleKey.Escape));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ Render_InAllPhases_DoesNotThrow

    [Fact]
    public void Render_InAllPhases_DoesNotThrow()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("5.0");
        var mods = new List<ProjectFile>
        {
            MakeFile("app.dll", DataState.Modified),
            MakeFile("config.xml", DataState.Deleted),
        };

        var phases = new[]
        {
            CheckoutGateScreen.Phase.Running,
            CheckoutGateScreen.Phase.Clean,
            CheckoutGateScreen.Phase.Dirty,
            CheckoutGateScreen.Phase.CheckingOut,
            CheckoutGateScreen.Phase.Error,
        };

        foreach (var phase in phases)
        {
            var screen = new CheckoutGateScreen(mgr, revision);
            screen.SetPhaseForTesting(phase, phase == CheckoutGateScreen.Phase.Dirty ? mods : null);

            var ex = Record.Exception(() => screen.Render());
            Assert.Null(ex);
        }
    }

    // ------------------------------------------------------------------ Handle_AnyKey_InErrorPhase_PopsScreen

    [Fact]
    public void Handle_AnyKey_InErrorPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("6.0");
        var screen = new CheckoutGateScreen(mgr, revision);
        screen.SetPhaseForTesting(CheckoutGateScreen.Phase.Error);

        // Any key should pop in Error phase
        var action = screen.Handle(Key(ConsoleKey.Enter));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ IntegrityTimeout_YieldsErrorPhase_NotClean

    /// <summary>
    /// A pre-checkout integrity check that never reports back must land the screen in
    /// Error — never in Clean, which would let the checkout proceed unverified.  The
    /// manager is made mute by severing FileManager's event backing fields, so neither
    /// completion nor an Idle state transition ever reaches the screen.
    /// </summary>
    [Fact]
    public void IntegrityTimeout_YieldsErrorPhase_NotClean()
    {
        var mgr = BuildManager();
        var fileManager = typeof(MetaDataManager)
            .GetField("_fileManager", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(mgr)!;
        foreach (var eventName in new[] { "IntegrityCheckEventHandler", "ManagerStateEventHandler", "IntegrityProgressEventHandler" })
        {
            var backingField = fileManager.GetType().GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(backingField); // seam broke: FileManager event renamed
            backingField!.SetValue(fileManager, null);
        }

        var screen = new CheckoutGateScreen(mgr, MakeRevision("7.0"));
        var originalTimeout = CheckoutGateScreen.IntegrityWaitTimeout;
        try
        {
            CheckoutGateScreen.IntegrityWaitTimeout = TimeSpan.FromMilliseconds(50);
            screen.OnEnter();
        }
        finally
        {
            CheckoutGateScreen.IntegrityWaitTimeout = originalTimeout;
        }

        var phase = (CheckoutGateScreen.Phase)typeof(CheckoutGateScreen)
            .GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(screen)!;
        Assert.Equal(CheckoutGateScreen.Phase.Error, phase);

        // Behavioral proof it is Error, not Clean: only Error pops on an arbitrary key.
        Assert.IsType<ScreenAction.Pop>(screen.Handle(Key(ConsoleKey.Enter)));
    }
}

#pragma warning restore CS0618
