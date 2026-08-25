#pragma warning disable CS0618  // ProjectData/ProjectMetaData are V1 types; used intentionally in CLI screen tests

using System;
using System.Linq;
using System.Reflection;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class RevisionListScreenTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);

    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    private static ProjectData MakeRevision(string version)
    {
        var pd = new ProjectData(@"C:\TestProject");
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

    /// <summary>
    /// Attaches a hand-built store to the manager. MetaDataManager.ProjectMetaData has a
    /// private setter that fires load events and rewrites CurrentProjectPath, so the tests
    /// poke the backing field instead — the screen only ever reads ProjectDataList.
    /// </summary>
    private static ProjectMetaData AttachMetaData(MetaDataManager mgr, params string[] versions)
    {
        var meta = new ProjectMetaData("TestProject", @"C:\TestProject");
        foreach (string v in versions) meta.ProjectDataList.AddLast(MakeRevision(v));

        FieldInfo field = typeof(MetaDataManager)
            .GetField("_projectMetaData", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(mgr, meta);
        return meta;
    }

    // ------------------------------------------------------------------ Construct_SnapshotsTheStoredVersions

    [Fact]
    public void Construct_SnapshotsTheStoredVersions()
    {
        var mgr = BuildManager();
        AttachMetaData(mgr, "1.0", "1.1", "1.2");

        var screen = new RevisionListScreen(mgr);

        Assert.Null(Record.Exception(() => screen.Render()));
    }

    // ------------------------------------------------------------------ OnEnter_AfterDelete_DropsTheStaleRow

    [Fact]
    public void OnEnter_AfterTheStoreLosesAVersion_RebuildsTheRows()
    {
        // The screen snapshots ProjectDataList at construction. A delete performed further
        // down the stack (detail → delete gate) removes a node from the live list, and
        // OnEnter runs again on the way back — that is where the snapshot is retaken.
        var mgr = BuildManager();
        var meta = AttachMetaData(mgr, "1.0", "1.1", "1.2");
        var screen = new RevisionListScreen(mgr);

        // Select the last row, then delete it out from under the screen.
        screen.Handle(Key(ConsoleKey.End));
        meta.ProjectDataList.RemoveLast();

        screen.OnEnter();

        // Rendering a stale row would index past the rebuilt list, or show "1.2" — neither
        // may happen. Entering the (now last) row must hand out a revision that still exists.
        Assert.Null(Record.Exception(() => screen.Render()));
        var action = screen.Handle(Key(ConsoleKey.Enter));
        Assert.IsType<ScreenAction.Push>(action);
        Assert.Equal(2, meta.ProjectDataList.Count);
    }

    // ------------------------------------------------------------------ OnEnter_AfterRename_ShowsTheNewTag

    [Fact]
    public void OnEnter_AfterTheStoreIsEmptied_RendersTheEmptyState()
    {
        var mgr = BuildManager();
        var meta = AttachMetaData(mgr, "1.0");
        var screen = new RevisionListScreen(mgr);

        meta.ProjectDataList.Clear();
        screen.OnEnter();

        Assert.Null(Record.Exception(() => screen.Render()));
        // Nothing left to inspect: Enter must not push a detail screen for a missing row.
        Assert.IsType<ScreenAction.Stay>(screen.Handle(Key(ConsoleKey.Enter)));
    }

    // ------------------------------------------------------------------ AutoAdvance

    [Fact]
    public void AutoAdvance_WithNothingCheckedOutOrDeleted_ReturnsNull()
    {
        var mgr = BuildManager();
        AttachMetaData(mgr, "1.0", "1.1");
        var screen = new RevisionListScreen(mgr);

        Assert.Null(screen.AutoAdvance());
    }

    [Fact]
    public void AutoAdvance_AfterAFailedDelete_StillReturnsNull()
    {
        // A refused delete never sets LastDeletedVersion, so the list must stay put.
        var mgr = BuildManager();
        AttachMetaData(mgr, "1.0", "1.1");
        var screen = new RevisionListScreen(mgr);

        mgr.RequestDeleteVersion("does-not-exist", confirmed: true);

        Assert.Null(screen.AutoAdvance());
        Assert.Null(mgr.LastDeletedVersion);
    }

    // ------------------------------------------------------------------ Handle_Escape_PopsScreen

    [Fact]
    public void Handle_Escape_PopsScreen()
    {
        var mgr = BuildManager();
        AttachMetaData(mgr, "1.0");
        var screen = new RevisionListScreen(mgr);

        Assert.IsType<ScreenAction.Pop>(screen.Handle(Key(ConsoleKey.Escape)));
    }

    // ------------------------------------------------------------------ Enter_PushesDetailForTheSelectedRow

    [Fact]
    public void Handle_Enter_PushesDetailScreenForTheSelectedRow()
    {
        var mgr = BuildManager();
        var meta = AttachMetaData(mgr, "1.0", "1.1");
        var screen = new RevisionListScreen(mgr);

        screen.Handle(Key(ConsoleKey.DownArrow));
        var action = screen.Handle(Key(ConsoleKey.Enter));

        var push = Assert.IsType<ScreenAction.Push>(action);
        Assert.IsType<RevisionDetailScreen>(push.Next);
        Assert.Equal(2, meta.ProjectDataList.Count);
    }
}

#pragma warning restore CS0618
