#pragma warning disable CS0618  // ProjectData/ProjectFile/ProjectMetaData are V1 types; used intentionally in CLI screen tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Spectre.Console;
using Xunit;

namespace DeployAssistant.CLI.Tests.Engine;

/// <summary>
/// The frame must look the same on every PC: exactly one window of lines, nothing wider than
/// the canvas (so the terminal never wraps a row), and no ambiguous-width glyphs in ASCII mode.
/// Every screen is composed at a spread of window sizes, in both glyph modes.
/// </summary>
[Collection(nameof(StaticConsoleCollection))]
public class FrameRendererTests : IDisposable
{
    private static readonly Regex AnsiEscape = new(@"\x1b\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
    private static readonly FrameRenderer.Target Target = new(ansi: true, ColorSystem.Standard);
    private const string AmbiguousGlyphs = "─│╭╮╰╯┌┐└┘›✓✗●…·↑↓▾▴→";

    public static readonly IEnumerable<object[]> Sizes = new[]
    {
        new object[] { 60, 16 },   // minimum supported
        new object[] { 80, 24 },   // classic default
        new object[] { 120, 30 },  // conhost default
        new object[] { 160, 45 },
        new object[] { 240, 70 },  // 4K monitor, small font
    };

    public void Dispose() => Term.Unicode = true;

    // ------------------------------------------------------------------ fixtures

    private static MetaDataManager BuildManager(int revisions = 60)
    {
        var mgr = new MetaDataManager(new NullDialogService());
        mgr.Awake();
        var meta = new ProjectMetaData("CleVisionSystems", ProjectPath);
        for (int i = 0; i < revisions; i++) meta.ProjectDataList.AddLast(MakeRevision($"1.0.{i}", i));
        Set(mgr, "_projectMetaData", meta);
        Set(mgr, "_mainProjectData", meta.ProjectDataList.Last!.Value);
        return mgr;
    }

    private const string ProjectPath = @"C:\Workspaces\현장\G2 라인\CleVisionSystems\Release\bin";

    private static ProjectData MakeRevision(string version, int i)
    {
        var pd = new ProjectData(ProjectPath)
        {
            ProjectName = "CleVisionSystems",
            UpdaterName = i % 2 == 0 ? "박일두" : "deploy-bot-with-a-long-name",
            UpdatedVersion = version,
            UpdateLog = "hotfix",
            ChangeLog = string.Join("\n", Enumerable.Range(0, 40).Select(n => $"Modified Vision\\Recipes\\Line{n}\\recipe_{n}.xml")),
            UpdatedTime = new DateTime(2026, 1, 1).AddDays(i),
            NumberOfChanges = i * 3,
        };
        return pd;
    }

    private static List<ProjectFile> MakeFiles(int count) =>
        Enumerable.Range(0, count).Select(i => new ProjectFile
        {
            DataRelPath = $@"Vision\검사 레시피\Line{i}\very\deeply\nested\folder\structure\for\testing\file_{i}.dll",
            DataName = $"file_{i}.dll",
            DataState = (i % 4) switch { 0 => DataState.Modified, 1 => DataState.Added, 2 => DataState.Deleted, _ => DataState.Restored },
        }).ToList();

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.A, false, false, false);

    /// <summary>Every screen in a representative state, each paired with the stack it sits on.</summary>
    private static IEnumerable<(string Name, List<Screen> Stack)> AllScreens()
    {
        var mgr = BuildManager();
        var main = new MainScreen(mgr);
        var rev = mgr.ProjectMetaData!.ProjectDataList.First!.Value;
        var list = new RevisionListScreen(mgr);

        yield return ("top-menu", new List<Screen> { new TopMenuScreen(loaded: false) });
        yield return ("main", new List<Screen> { main });
        yield return ("revisions", new List<Screen> { main, list });
        yield return ("revision-detail", new List<Screen> { main, list, new RevisionDetailScreen(mgr, rev) });
        yield return ("integrity-dirty", new List<Screen> { main, new IntegrityResultScreen(mgr, MakeFiles(150)) });
        yield return ("integrity-clean", new List<Screen> { main, new IntegrityResultScreen(mgr, new List<ProjectFile>()) });

        var gate = new CheckoutGateScreen(mgr, rev);
        gate.SetPhaseForTesting(CheckoutGateScreen.Phase.Dirty, MakeFiles(80));
        yield return ("checkout-dirty", new List<Screen> { main, list, gate });

        var clean = new CheckoutGateScreen(mgr, rev);
        clean.SetPhaseForTesting(CheckoutGateScreen.Phase.Clean);
        yield return ("checkout-clean", new List<Screen> { main, list, clean });

        var delete = new VersionDeleteGateScreen(mgr, rev);
        delete.SetPhaseForTesting(VersionDeleteGateScreen.Phase.Preview, new VersionDeletePlan(
            rev, @"C:\Workspaces\현장\G2 라인\CleVisionSystems\Release\bin\Backup_CleVisionSystems\Backup_1.0.0",
            exclusiveHashes: new List<string> { "a", "b" }, sharedHashes: new List<string> { "c" },
            relocationHashes: new List<string> { "c" }, reclaimableBytes: 512L * 1024 * 1024,
            backupFolderRemovable: true, blockers: null));
        yield return ("delete-preview", new List<Screen> { main, list, delete });

        yield return ("new-version", new List<Screen> { main, new UpdateVersionPromptScreen(mgr, 12) });
        yield return ("rename", new List<Screen> { main, list, new VersionRenameScreen(mgr, rev) });

        var picker = new PathPickerScreen(PathPickerScreen.Mode.Switch,
            _ => Enumerable.Range(0, 70).Select(i => $"Project_{i:D2}_with_a_fairly_long_folder_name").ToArray());
        foreach (char c in @"C:\Projects\") picker.Handle(Char(c));
        picker.Handle(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        yield return ("path-picker", new List<Screen> { new TopMenuScreen(loaded: false), picker });
    }

    private static int VisibleWidth(string line) => Ui.Len(AnsiEscape.Replace(line, ""));

    // ------------------------------------------------------------------ tests

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryScreen_FillsTheWindowExactly_AndNeverExceedsTheCanvas(int width, int height)
    {
        foreach (bool unicode in new[] { true, false })
        {
            Term.Unicode = unicode;
            foreach (var (name, stack) in AllScreens())
            {
                string[] lines = FrameRenderer.Compose(stack, width, height, Target);
                int canvas = FrameRenderer.CanvasWidth(width);

                Assert.True(lines.Length == height, $"{name} @ {width}x{height}: {lines.Length} lines");
                for (int i = 0; i < lines.Length; i++)
                    Assert.True(VisibleWidth(lines[i]) <= canvas,
                        $"{name} @ {width}x{height} (unicode={unicode}) line {i} is {VisibleWidth(lines[i])} cells: {AnsiEscape.Replace(lines[i], "")}");
                Assert.Contains($"DeployAssistant CLI {CliVersion.Display}", AnsiEscape.Replace(lines[0], ""));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void AsciiMode_EmitsNoAmbiguousWidthGlyphs(int width, int height)
    {
        Term.Unicode = false;
        foreach (var (name, stack) in AllScreens())
        {
            string text = string.Join("\n", FrameRenderer.Compose(stack, width, height, Target));
            char bad = text.FirstOrDefault(c => AmbiguousGlyphs.IndexOf(c) >= 0);
            Assert.True(bad == default, $"{name} @ {width}x{height} contains '{bad}' (U+{(int)bad:X4})");
        }
    }

    [Fact]
    public void ListsGrowWithTheWindowHeight()
    {
        var mgr = BuildManager();
        int Rows(int height)
        {
            string[] lines = FrameRenderer.Compose(new List<Screen> { new RevisionListScreen(mgr) }, 120, height, Target);
            return lines.Count(l => AnsiEscape.Replace(l, "").Contains("2026-"));
        }

        Assert.True(Rows(50) > Rows(24), $"50 rows -> {Rows(50)}, 24 rows -> {Rows(24)}");
    }

    [Fact]
    public void WideWindows_CapTheCanvas()
    {
        Assert.Equal(Ui.MaxWidth, FrameRenderer.CanvasWidth(300));
        Assert.Equal(79, FrameRenderer.CanvasWidth(80));
    }

    [Fact]
    public void BelowMinimum_ShowsResizeMessage()
    {
        string[] lines = FrameRenderer.Compose(new List<Screen> { new TopMenuScreen(false) }, 50, 12, Target);

        Assert.Equal(12, lines.Length);
        Assert.Contains("Window too small", AnsiEscape.Replace(lines[0], ""));
    }

    [Fact]
    public void Modal_IsDrawnInsideTheFrame()
    {
        var stack = new List<Screen> { new MainScreen(BuildManager()) };
        var modal = TuiPrompt.BuildModal("Create new revision", "Commit 12 changes as a new revision by 'tester'?");

        string[] lines = FrameRenderer.Compose(stack, 80, 24, Target, modal);

        Assert.Equal(24, lines.Length);
        Assert.Contains(lines, l => AnsiEscape.Replace(l, "").Contains("Create new revision"));
        Assert.All(lines, l => Assert.True(VisibleWidth(l) <= 79));
    }

    [Fact]
    public void FooterHints_DropTheTailWhenTheyCannotFit()
    {
        var hints = Enumerable.Range(0, 30).Select(i => new KeyHint($"k{i}", $"action number {i}")).ToList();

        var lines = FrameRenderer.HintLines(hints, 60);

        Assert.Equal(2, lines.Count);
    }

    [Theory]
    [InlineData(null, 949, false)]          // Korean conhost
    [InlineData(null, 437, true)]           // US conhost
    [InlineData("WT_SESSION", 949, true)]   // Windows Terminal, any code page
    [InlineData("TERM_PROGRAM", 949, true)] // VS Code terminal
    public void DetectUnicode_FollowsTheHost(string? hostVar, int codePage, bool expected)
    {
        Func<string, string?> env = name => name == hostVar ? "1" : null;

        Assert.Equal(expected, Term.DetectUnicode(env, codePage));
    }

    [Theory]
    [InlineData("ascii", 437, false)]
    [InlineData("unicode", 949, true)]
    public void DetectUnicode_HonoursTheOverride(string value, int codePage, bool expected)
    {
        Func<string, string?> env = name => name == "DA_CLI_GLYPHS" ? value : null;

        Assert.Equal(expected, Term.DetectUnicode(env, codePage));
    }

    [Theory]
    [InlineData(@"C:\a\b\c\d\e\f\g\file.dll", 20)]
    [InlineData(@"C:\현장\라인\검사\레시피\파일.xml", 16)]
    public void FitPath_KeepsTheTailAndNeverExceedsTheBudget(string path, int max)
    {
        string fitted = Ui.FitPath(path, max);

        Assert.True(Ui.Len(fitted) <= max, fitted);
        Assert.EndsWith(Path.GetFileName(path), fitted);
    }
}

/// <summary>Tests here swap the static AnsiConsole and Term state, so they must not run in parallel with each other.</summary>
[CollectionDefinition(nameof(StaticConsoleCollection), DisableParallelization = true)]
public class StaticConsoleCollection { }
