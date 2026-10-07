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
/// - Clean path: inline y/n confirm before calling RequestCheckoutVersion.
/// - Dirty path: shows the list of local modifications. In <see cref="CheckoutMode.Fast"/>
///   the user must discard those changes first; in <see cref="CheckoutMode.CleanRestore"/>
///   the checkout itself repairs the drift, so a plain y/n confirm is the whole gate.
/// </summary>
internal sealed class CheckoutGateScreen : Screen
{
    internal enum Phase { Running, Clean, Dirty, CheckingOut, Error }

    private readonly MetaDataManager _mgr;
    private readonly ProjectData _target;
    private readonly CheckoutMode _mode;
    private Phase _phase = Phase.Running;
    private List<ProjectFile> _modifications = new List<ProjectFile>();
    private SelectableList? _modList;
    private string? _errorMessage;
    private const int DiffViewportHeight = 8;

    // internal test seam; production default stays 10 minutes
    internal static TimeSpan IntegrityWaitTimeout = TimeSpan.FromMinutes(10);

    public CheckoutGateScreen(MetaDataManager mgr, ProjectData target, CheckoutMode mode = CheckoutMode.Fast)
    {
        _mgr = mgr;
        _target = target;
        _mode = mode;
    }

    internal CheckoutMode Mode => _mode;

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
        bool integrityCompleted = false;

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
                        activeTask = ctx.AddTask("[aqua]Pre-checkout integrity check[/]",
                            new ProgressTaskSettings { AutoStart = true, MaxValue = 1 });
                    }
                    _mgr.RequestProjectIntegrityCheck(forceFullHash: true);
                    integrityCompleted = done.Wait(IntegrityWaitTimeout);
                    lock (progressLock)
                    {
                        if (activeTask is not null && totalKnown > 0)
                            activeTask.Value = totalKnown; // ensure 100% on completion
                    }
                });

            if (!integrityCompleted)
            {
                // A check that never reported back must not read as "no modifications" —
                // that would let a checkout proceed unverified over dirty local state.
                _errorMessage = "Integrity check did not complete (timed out). Checkout cancelled; re-open the project and retry.";
                _phase = Phase.Error;
                return;
            }

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

    public override string Title => "Checkout";

    public override IReadOnlyList<KeyHint> Hints => _phase switch
    {
        Phase.Clean => new[] { new KeyHint("y", "checkout"), new KeyHint("n/esc", "cancel") },
        Phase.Dirty when _mode == CheckoutMode.CleanRestore => new[]
        {
            new KeyHint(Glyphs.UpDown, "move"), new KeyHint("y", "safe checkout"), new KeyHint("esc", "cancel"),
        },
        Phase.Dirty => new[]
        {
            new KeyHint(Glyphs.UpDown, "move"), new KeyHint("d", "discard & checkout"),
            new KeyHint("u", "half-page up"), new KeyHint("esc", "cancel"),
        },
        Phase.Error => new[] { new KeyHint("any key", "back") },
        _ => Array.Empty<KeyHint>(),
    };

    public override void Render()
    {
        switch (_phase)
        {
            case Phase.Running:
                // Render handled inside OnEnter via AnsiConsole.Progress
                return;

            case Phase.Clean:
                Ui.Card(ModeTitle, new[]
                {
                    new CardRow("Target", _target.UpdatedVersion ?? "", style: "aqua bold"),
                    new CardRow("Updated", $"{_target.UpdatedTime:yyyy-MM-dd HH:mm}  by {_target.UpdaterName}"),
                    new CardRow("Working", "no local modifications", style: "green"),
                }, TextStyle.AccentColor);
                Ui.Blank();
                if (_mode == CheckoutMode.CleanRestore)
                    Ui.Note("  Safe checkout re-hashes the working directory against the target snapshot.");
                Ui.Line($"  Restore the project to {TextStyle.Accent(Markup.Escape(_target.UpdatedVersion ?? ""))}?");
                return;

            case Phase.Dirty:
            {
                string verb = _mode == CheckoutMode.CleanRestore
                    ? $"Safe checkout will repair {_modifications.Count} drifted file(s)."
                    : $"Checkout requires discarding {_modifications.Count} local change(s).";
                Ui.Box("Local modifications detected", new[]
                {
                    $"Target  {TextStyle.Accent(Markup.Escape(Ui.Fit(_target.UpdatedVersion ?? "", Ui.Width - 14)))}",
                    Markup.Escape(verb),
                }, Color.Yellow, "yellow bold");
                Ui.Blank();

                // box(4) + blank + section
                _modList!.SetViewportHeight(Ui.ListRows(reservedRows: 6));
                int top = _modList.ViewportTop;
                int last = Math.Min(_modifications.Count, top + _modList.ViewportHeight);
                Ui.Section("Local changes", Ui.Range(top, last - top, _modifications.Count));
                var cols = new[] { new Col("State", 5), new Col("Path", isPath: true) };
                for (int i = top; i < last; i++)
                {
                    var pf = _modifications[i];
                    var (label, color) = TextStyle.StateBadge(pf.DataState);
                    Ui.TableRow(cols, new (string, string?)[] { (label, $"{color} bold"), (pf.DataRelPath, color) },
                                selected: i == _modList.SelectedIndex);
                }
                return;
            }

            case Phase.CheckingOut:
                Ui.Blank();
                Ui.Line($"  [aqua]Checking out {Markup.Escape(_target.UpdatedVersion ?? "")}...[/]");
                return;

            case Phase.Error:
                Ui.Box("Checkout failed", new[] { $"[red]{Markup.Escape(_errorMessage ?? "Unknown error")}[/]" },
                       Color.Red, "red bold");
                return;
        }
    }

    private string ModeTitle =>
        _mode == CheckoutMode.CleanRestore ? "Safe checkout revision" : "Checkout revision";

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
                // Clean restore repairs drift as part of the checkout itself, so there is
                // nothing to discard up front — confirming is the whole gate.
                if (_mode == CheckoutMode.CleanRestore && key.Key == ConsoleKey.Y) return Checkout();
                if (_mode == CheckoutMode.Fast && key.KeyChar == 'd') return DiscardAndCheckout();
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
        bool ok = RunCheckout(out CheckoutResult? result);
        if (ok)
            return ScreenAction.PopAction; // RevisionDetailScreen.AutoAdvance pops on towards MainScreen
        _phase = Phase.Error;
        _errorMessage = DescribeFailure(result, "Checkout failed (see trace logs).");
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
        bool ok = RunCheckout(out CheckoutResult? result);
        if (ok) return ScreenAction.PopAction;
        _phase = Phase.Error;
        _errorMessage = DescribeFailure(result, "Checkout failed after reverting local changes.");
        return ScreenAction.StayAction;
    }

    /// <summary>
    /// Fires the request with CheckoutCompleteEventHandler attached. The event carries the
    /// real refusal reason on every failure exit, so the user never gets a bare "see logs".
    /// </summary>
    private bool RunCheckout(out CheckoutResult? result)
    {
        CheckoutResult? captured = null;
        void OnComplete(CheckoutResult r) => captured = r;

        _mgr.CheckoutCompleteEventHandler += OnComplete;
        try
        {
            return _mgr.RequestCheckoutVersion(_target, _mode);
        }
        finally
        {
            _mgr.CheckoutCompleteEventHandler -= OnComplete;
            result = captured;
        }
    }

    private static string DescribeFailure(CheckoutResult? result, string fallback)
    {
        if (result == null || result.Messages.Count == 0) return fallback;
        return string.Join(" ", result.Messages);
    }
}
