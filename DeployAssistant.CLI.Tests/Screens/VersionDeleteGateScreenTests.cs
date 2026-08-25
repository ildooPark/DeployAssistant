#pragma warning disable CS0618  // ProjectData is a V1 type; used intentionally in CLI screen tests

using System;
using System.Collections.Generic;
using System.Linq;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class VersionDeleteGateScreenTests
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

    private static VersionDeletePlan MakePlan(ProjectData target, params string[] blockers) =>
        new VersionDeletePlan(
            target,
            $@"C:\TestProject\Backup_TestProject\Backup_{target.UpdatedVersion}",
            exclusiveHashes: new List<string> { "aaa", "bbb" },
            sharedHashes: new List<string> { "ccc" },
            relocationHashes: new List<string> { "ccc" },
            reclaimableBytes: 5L * 1024 * 1024,
            backupFolderRemovable: true,
            blockers: blockers.Length == 0 ? null : (IEnumerable<string>)blockers);

    // ------------------------------------------------------------------ Construct_DoesNotThrow

    [Fact]
    public void Construct_DoesNotThrow()
    {
        var mgr = BuildManager();

        var ex = Record.Exception(() => new VersionDeleteGateScreen(mgr, MakeRevision("1.0")));

        Assert.Null(ex);
    }

    // ------------------------------------------------------------------ OnEnter_NoProjectLoaded_EntersErrorPhase

    [Fact]
    public void OnEnter_NoProjectLoaded_EntersErrorPhase()
    {
        // RequestVersionDeletePreview returns null when no metadata is loaded.
        var mgr = BuildManager();
        var screen = new VersionDeleteGateScreen(mgr, MakeRevision("1.0"));

        screen.OnEnter();

        Assert.Equal(VersionDeleteGateScreen.Phase.Error, screen.CurrentPhase);
        Assert.NotNull(screen.ErrorMessage);
    }

    // ------------------------------------------------------------------ OnEnter_IsIdempotent

    [Fact]
    public void OnEnter_CalledTwice_DoesNotRebuildThePreview()
    {
        var mgr = BuildManager();
        var screen = new VersionDeleteGateScreen(mgr, MakeRevision("1.0"));
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, MakePlan(MakeRevision("1.0")));

        screen.OnEnter();  // re-entry guard: must not overwrite the seeded phase

        Assert.Equal(VersionDeleteGateScreen.Phase.Preview, screen.CurrentPhase);
    }

    // ------------------------------------------------------------------ Handle_N_InPreviewPhase_PopsScreen

    [Fact]
    public void Handle_NInPreviewPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("2.0");
        var screen = new VersionDeleteGateScreen(mgr, revision);
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, MakePlan(revision));

        var action = screen.Handle(Key(ConsoleKey.N, 'n'));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ Handle_Escape_InPreviewPhase_PopsScreen

    [Fact]
    public void Handle_EscapeInPreviewPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("2.1");
        var screen = new VersionDeleteGateScreen(mgr, revision);
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, MakePlan(revision));

        var action = screen.Handle(Key(ConsoleKey.Escape));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    // ------------------------------------------------------------------ Handle_UnrelatedKey_InPreviewPhase_Stays

    [Fact]
    public void Handle_UnrelatedKeyInPreviewPhase_StaysOnScreen()
    {
        // Anything that is not an explicit 'y' must never start a delete.
        var mgr = BuildManager();
        var revision = MakeRevision("2.2");
        var screen = new VersionDeleteGateScreen(mgr, revision);
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, MakePlan(revision));

        var action = screen.Handle(Key(ConsoleKey.D, 'd'));

        Assert.IsType<ScreenAction.Stay>(action);
        Assert.Equal(VersionDeleteGateScreen.Phase.Preview, screen.CurrentPhase);
    }

    // ------------------------------------------------------------------ Handle_Y_WhenRequestRefused_ShowsRealReason

    [Fact]
    public void Handle_YInPreviewPhase_WhenRequestRefused_ShowsTheReportedReason()
    {
        // No metadata is loaded, so RequestDeleteVersion reports Blocked/"No project is loaded."
        // through VersionDeleteCompleteEventHandler. The screen must surface that, not pop.
        var mgr = BuildManager();
        var revision = MakeRevision("2.3");
        var screen = new VersionDeleteGateScreen(mgr, revision);
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, MakePlan(revision));

        var action = screen.Handle(Key(ConsoleKey.Y, 'y'));

        Assert.IsType<ScreenAction.Stay>(action);
        Assert.Equal(VersionDeleteGateScreen.Phase.Blocked, screen.CurrentPhase);
        Assert.Contains(screen.BlockReasons, m => m.Contains("No project is loaded"));
    }

    // ------------------------------------------------------------------ BlockedPlan_RefusesAndKeepsTheBlockers

    [Fact]
    public void SetPhase_Blocked_CarriesThePlanBlockers()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("3.0");
        var plan = MakePlan(revision, "Cannot delete the current main version.");
        var screen = new VersionDeleteGateScreen(mgr, revision);

        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Blocked, plan);

        Assert.False(plan.CanDelete);
        Assert.Contains("Cannot delete the current main version.", screen.BlockReasons);
    }

    // ------------------------------------------------------------------ Handle_AnyKey_InBlockedPhase_PopsScreen

    [Fact]
    public void Handle_AnyKey_InBlockedPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("3.1");
        var screen = new VersionDeleteGateScreen(mgr, revision);
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Blocked,
            MakePlan(revision, "Cannot delete the last remaining version."));

        // 'y' included: a blocked plan must never be deletable from this phase.
        Assert.IsType<ScreenAction.Pop>(screen.Handle(Key(ConsoleKey.Y, 'y')));
    }

    // ------------------------------------------------------------------ Handle_AnyKey_InErrorPhase_PopsScreen

    [Fact]
    public void Handle_AnyKey_InErrorPhase_PopsScreen()
    {
        var mgr = BuildManager();
        var screen = new VersionDeleteGateScreen(mgr, MakeRevision("3.2"));
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Error);

        Assert.IsType<ScreenAction.Pop>(screen.Handle(Key(ConsoleKey.Enter)));
    }

    // ------------------------------------------------------------------ Handle_InDeletingPhase_Stays

    [Fact]
    public void Handle_InDeletingPhase_StaysOnScreen()
    {
        var mgr = BuildManager();
        var screen = new VersionDeleteGateScreen(mgr, MakeRevision("3.3"));
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Deleting);

        Assert.IsType<ScreenAction.Stay>(screen.Handle(Key(ConsoleKey.Escape)));
    }

    // ------------------------------------------------------------------ Render_InAllPhases_DoesNotThrow

    [Fact]
    public void Render_InAllPhases_DoesNotThrow()
    {
        var mgr = BuildManager();
        var revision = MakeRevision("4.0");

        var phases = new[]
        {
            VersionDeleteGateScreen.Phase.Preview,
            VersionDeleteGateScreen.Phase.Blocked,
            VersionDeleteGateScreen.Phase.Deleting,
            VersionDeleteGateScreen.Phase.Error,
        };

        foreach (var phase in phases)
        {
            var screen = new VersionDeleteGateScreen(mgr, revision);
            screen.SetPhaseForTesting(phase, MakePlan(revision, "Cannot delete the current main version."));

            var ex = Record.Exception(() => screen.Render());
            Assert.Null(ex);
        }
    }

    // ------------------------------------------------------------------ Render_VersionNameWithBrackets_DoesNotThrow

    [Fact]
    public void Render_VersionNameContainingBrackets_DoesNotCrashSpectreMarkup()
    {
        // Unescaped '[' / ']' in a markup string throws at render time (PR #19).
        var mgr = BuildManager();
        var revision = MakeRevision("1.0-[rc1]");
        var screen = new VersionDeleteGateScreen(mgr, revision);
        screen.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, MakePlan(revision));

        var ex = Record.Exception(() => screen.Render());

        Assert.Null(ex);
    }

    // ------------------------------------------------------------------ FormatBytes

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1073741824L, "1 GB")]
    public void FormatBytes_RendersHumanReadableSizes(long bytes, string expected)
    {
        Assert.Equal(expected, VersionDeleteGateScreen.FormatBytes(bytes));
    }

    [Fact]
    public void FormatBytes_NegativeInput_ClampsToZero()
    {
        Assert.Equal("0 B", VersionDeleteGateScreen.FormatBytes(-1));
    }

    // ------------------------------------------------------------------ RevisionDetailScreen_X_PushesDeleteGate

    [Fact]
    public void RevisionDetailScreen_X_PushesVersionDeleteGateScreen()
    {
        var mgr = BuildManager();
        var detail = new RevisionDetailScreen(mgr, MakeRevision("5.0"));

        var action = detail.Handle(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        var push = Assert.IsType<ScreenAction.Push>(action);
        Assert.IsType<VersionDeleteGateScreen>(push.Next);
    }
}

#pragma warning restore CS0618
