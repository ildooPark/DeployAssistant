using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

/// <summary>
/// Pre-checkout integrity gate. Runs an integrity check, then:
/// - Clean path: inline y/n confirm before calling RequestRevertProject.
/// - Dirty path: shows the list of local modifications; user can discard all
///   changes and checkout, or cancel.
/// </summary>
internal sealed class CheckoutGateScreen : Screen
{
    internal enum Phase { Running, Clean, Dirty, CheckingOut, Error }

    private readonly MetaDataManager _mgr;
    private readonly ProjectData _target;
    private Phase _phase = Phase.Running;
    private List<ProjectFile> _modifications = new List<ProjectFile>();
    private SelectableList? _modList;
    private string? _errorMessage;
    private const int DiffViewportHeight = 8;

    public CheckoutGateScreen(MetaDataManager mgr, ProjectData target)
    {
        _mgr = mgr;
        _target = target;
    }

    // internal test seam
    internal void SetPhaseForTesting(Phase phase, List<ProjectFile>? mods = null)
    {
        _phase = phase;
        if (mods != null)
        {
            _modifications = mods;
            _modList = new SelectableList(mods.Count, DiffViewportHeight);
        }
    }

    public override void OnEnter()
    {
        if (_phase != Phase.Running) return; // re-entry guard

        var captured = new List<ProjectFile>();
        using var done = new ManualResetEventSlim(false);

        void OnComplete(string _, ObservableCollection<ProjectFile> files)
        {
            lock (captured)
            {
                captured.Clear();
                captured.AddRange(files);
            }
            done.Set();
        }

        void OnState(MetaDataState s)
        {
            if (s == MetaDataState.Idle) done.Set();
        }

        int totalKnown = 0;
        var progressLock = new object();
        ProgressTask? activeTask = null;

        void OnProgress(int completed, int total)
        {
            lock (progressLock)
            {
                if (activeTask is null) return;
                if (totalKnown == 0)
                {
                    totalKnown = total;
                    activeTask.MaxValue = total;
                }
                if (completed > activeTask.Value)
                    activeTask.Value = completed;
            }
        }

        _mgr.IntegrityCheckCompleteEventHandler += OnComplete;
        _mgr.ManagerStateEventHandler += OnState;
        _mgr.IntegrityProgressEventHandler += OnProgress;
        try
        {
            AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                })
                .Start(ctx =>
                {
                    lock (progressLock)
                    {
                        activeTask = ctx.AddTask("[cyan]Pre-checkout integrity check[/]",
                            new ProgressTaskSettings { AutoStart = true, MaxValue = 1 });
                    }
                    _mgr.RequestProjectIntegrityCheck();
                    done.Wait(TimeSpan.FromMinutes(10));
                    lock (progressLock)
                    {
                        if (activeTask is not null && totalKnown > 0)
                            activeTask.Value = totalKnown; // ensure 100% on completion
                    }
                });

            lock (captured) { _modifications = new List<ProjectFile>(captured); }
            _phase = _modifications.Count == 0 ? Phase.Clean : Phase.Dirty;
            if (_phase == Phase.Dirty)
                _modList = new SelectableList(_modifications.Count, DiffViewportHeight);
        }
        finally
        {
            _mgr.IntegrityCheckCompleteEventHandler -= OnComplete;
            _mgr.ManagerStateEventHandler -= OnState;
            _mgr.IntegrityProgressEventHandler -= OnProgress;
        }
    }

    public override void Render()
    {
        switch (_phase)
        {
            case Phase.Running:
                // Render handled inside OnEnter via AnsiConsole.Progress
                return;

            case Phase.Clean:
                AnsiConsole.MarkupLine($"  [bold]Checkout revision[/]");
                AnsiConsole.MarkupLine($"  Restore project to [cyan]{Markup.Escape(_target.UpdatedVersion ?? "")}[/]?");
                AnsiConsole.MarkupLine($"  Updated: {_target.UpdatedTime:yyyy-MM-dd HH:mm} by {Markup.Escape(_target.UpdaterName ?? "")}");
                AnsiConsole.MarkupLine($"  [dim]{TextStyle.SuccessGlyph} No local modifications detected.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(TextStyle.Dim("  y checkout · n/esc cancel"));
                return;

            case Phase.Dirty:
                AnsiConsole.MarkupLine($"  [bold yellow]Local modifications detected[/]");
                AnsiConsole.MarkupLine($"  Restoring to [cyan]{Markup.Escape(_target.UpdatedVersion ?? "")}[/] requires discarding {_modifications.Count} change(s):");
                AnsiConsole.WriteLine();

                int top = _modList!.ViewportTop;
                int last = Math.Min(_modifications.Count, top + DiffViewportHeight);
                for (int i = top; i < last; i++)
                {
                    var pf = _modifications[i];
                    string row = TextStyle.FormatFileState(pf.DataState, pf.DataRelPath);
                    string marker = i == _modList.SelectedIndex ? TextStyle.SelectionMarker : " ";
                    AnsiConsole.MarkupLine($"   {marker}{row}");
                }
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(TextStyle.Dim("  ↑↓ move · d/u half-page · d discard & checkout · esc cancel"));
                return;

            case Phase.CheckingOut:
                AnsiConsole.MarkupLine("  [cyan]Working...[/]");
                return;

            case Phase.Error:
                AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(_errorMessage ?? "Unknown error")}[/]");
                AnsiConsole.MarkupLine(TextStyle.Dim("  Press any key to return."));
                return;
        }
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        switch (_phase)
        {
            case Phase.Running:
                return ScreenAction.StayAction; // shouldn't reach — OnEnter blocks

            case Phase.Clean:
                if (key.Key == ConsoleKey.Y) return Checkout();
                if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
                return ScreenAction.StayAction;

            case Phase.Dirty:
                if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
                if (key.KeyChar == 'd') return DiscardAndCheckout();
                if (_modList != null) _modList.Handle(key);
                return ScreenAction.StayAction;

            case Phase.CheckingOut:
                return ScreenAction.StayAction;

            case Phase.Error:
                return ScreenAction.PopAction;
        }
        return ScreenAction.StayAction;
    }

    private ScreenAction Checkout()
    {
        _phase = Phase.CheckingOut;
        bool ok = _mgr.RequestRevertProject(_target);
        if (ok)
            return ScreenAction.PopAction; // RevisionListScreen.AutoAdvance pops again to MainScreen
        _phase = Phase.Error;
        _errorMessage = "Checkout failed (see trace logs).";
        return ScreenAction.StayAction;
    }

    private ScreenAction DiscardAndCheckout()
    {
        _phase = Phase.CheckingOut;
        // Revert each local modification first.
        var failedFiles = new List<string>();
        foreach (var mod in _modifications)
        {
            if (!_mgr.RequestRevertChange(mod))
                failedFiles.Add(mod.DataRelPath);
        }
        if (failedFiles.Count > 0)
        {
            _phase = Phase.Error;
            _errorMessage = $"Failed to revert {failedFiles.Count} local change(s) (backups may be missing): " +
                            $"{string.Join(", ", failedFiles.Take(3))}{(failedFiles.Count > 3 ? "..." : "")}";
            return ScreenAction.StayAction;
        }
        // Now do the actual checkout.
        bool ok = _mgr.RequestRevertProject(_target);
        if (ok) return ScreenAction.PopAction;
        _phase = Phase.Error;
        _errorMessage = "Checkout failed after reverting local changes.";
        return ScreenAction.StayAction;
    }
}
