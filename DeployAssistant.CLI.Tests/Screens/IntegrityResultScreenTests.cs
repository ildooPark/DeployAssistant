#pragma warning disable CS0618  // ProjectFile is a V1 type; used intentionally in CLI screen tests

using System;
using System.Collections.Generic;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens;

public class IntegrityResultScreenTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);

    private static MetaDataManager BuildManager()
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        return mgr;
    }

    private static ProjectFile MakeFile(string relPath, DataState state = DataState.Modified)
    {
        var f = new ProjectFile();
        f.DataRelPath = relPath;
        f.DataName = System.IO.Path.GetFileName(relPath);
        f.DataState = state;
        return f;
    }

    [Fact]
    public void Handle_Escape_PopsScreen()
    {
        var mgr = BuildManager();
        var screen = new IntegrityResultScreen(mgr, new List<ProjectFile>());

        var action = screen.Handle(Key(ConsoleKey.Escape));

        Assert.IsType<ScreenAction.Pop>(action);
    }

    [Fact]
    public void Handle_R_OnEmptyList_DoesNothing()
    {
        var mgr = BuildManager();
        var screen = new IntegrityResultScreen(mgr, new List<ProjectFile>());

        // An empty list should return Pop on any key (existing behavior for zero-file state).
        // Pressing R on an empty list should not throw; it just falls through.
        // The zero-file branch returns PopAction before the R branch is reached.
        var action = screen.Handle(Key(ConsoleKey.R));

        // Empty-list case should return Pop (existing zero-files behavior).
        Assert.IsType<ScreenAction.Pop>(action);
    }

    [Fact]
    public void Construct_NonEmptyList_RendersWithoutThrowing()
    {
        var mgr = BuildManager();
        var files = new List<ProjectFile>
        {
            MakeFile("app.dll", DataState.Modified),
            MakeFile("config.xml", DataState.Deleted),
        };

        var screen = new IntegrityResultScreen(mgr, files);

        // Render writes to the console; should not throw.
        var ex = Record.Exception(() => screen.Render());
        Assert.Null(ex);
    }

    [Fact]
    public void Handle_NonRKey_ClearsLastErrorImplicitly()
    {
        // Verify that after pressing a non-R key the screen returns Stay and remains stable.
        var mgr = BuildManager();
        var files = new List<ProjectFile> { MakeFile("foo.dll") };
        var screen = new IntegrityResultScreen(mgr, files);

        // Down arrow — should not throw and should return Stay.
        var action = screen.Handle(Key(ConsoleKey.DownArrow));
        Assert.IsType<ScreenAction.Stay>(action);
    }
}

#pragma warning restore CS0618
