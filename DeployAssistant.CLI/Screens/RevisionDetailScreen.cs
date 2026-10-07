using System;
using System.Collections.Generic;
using System.Linq;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

#pragma warning disable CS0618  // ChangedFile is V1 type; used intentionally here

internal sealed class RevisionDetailScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private readonly ProjectData _revision;
    private List<ChangedFile>? _diff;            // null until OnEnter runs
    private SelectableList? _diffList;
    private string? _lastError;
    private readonly bool _isCurrent;
    // Set when this screen pushed a gate; the matching manager flag is only meaningful
    // afterwards, so a stale flag from an earlier screen can never auto-pop this one.
    private bool _checkoutRequested;
    private bool _deleteRequested;
    private const int DiffViewportHeight = 8;

    public RevisionDetailScreen(MetaDataManager mgr, ProjectData revision)
    {
        _mgr = mgr;
        _revision = revision;
        _isCurrent = ReferenceEquals(revision, mgr.MainProjectData)
                     || (mgr.MainProjectData != null && revision.Equals(mgr.MainProjectData));
    }

    public override void OnEnter()
    {
        if (_isCurrent)
        {
            _diff = new List<ChangedFile>();  // empty — nothing to checkout
            _diffList = new SelectableList(0, DiffViewportHeight);
            return;
        }

        // Subscribe before firing the request so we don't miss the synchronous fire.
        // -= first: the engine re-enters OnEnter every time a pushed child screen pops,
        // and duplicate handlers on the process-lifetime manager leak popped screens.
        _mgr.ProjComparisonCompleteEventHandler -= OnDiffComplete;
        _mgr.ProjComparisonCompleteEventHandler += OnDiffComplete;
        _mgr.RequestProjVersionDiff(_revision);
    }

    public override void OnExit()
    {
        _mgr.ProjComparisonCompleteEventHandler -= OnDiffComplete;
    }

    private void OnDiffComplete(ProjectData src, ProjectData dst, List<ChangedFile> diff)
    {
        // Match on the exact object reference first; fall back to value equality.
        if (!ReferenceEquals(src, _revision) && !src.Equals(_revision)) return;
        _diff = diff;
        _diffList = new SelectableList(diff.Count, DiffViewportHeight);
    }

    public override string Title => $"#{RevisionNumber} {_revision.UpdatedVersion}";

    private int RevisionNumber
    {
        get
        {
            var allRevisions = _mgr.ProjectMetaData?.ProjectDataList?.ToList();
            return allRevisions != null ? allRevisions.IndexOf(_revision) + 1 : 0;
        }
    }

    public override IReadOnlyList<KeyHint> Hints
    {
        get
        {
            var hints = new List<KeyHint>();
            if (_diff is { Count: > 0 }) hints.Add(new KeyHint(Glyphs.UpDown, "move"));
            if (!_isCurrent) hints.Add(new KeyHint("c", "checkout"));
            hints.Add(new KeyHint("r", "rename"));
            hints.Add(new KeyHint("x", "delete"));
            hints.Add(new KeyHint("esc", "back"));
            return hints;
        }
    }

    public override void Render()
    {
        Ui.Card($"Revision #{RevisionNumber}", new[]
        {
            new CardRow("Version", _revision.UpdatedVersion ?? "", style: "aqua bold", note: _isCurrent ? "current main" : null),
            new CardRow("Updated", $"{_revision.UpdatedTime:yyyy-MM-dd HH:mm}  by {_revision.UpdaterName}"),
            new CardRow("Changes", $"{_revision.NumberOfChanges} file(s)"),
        }, _isCurrent ? TextStyle.AccentColor : TextStyle.FrameColor);

        // The change log lists one line per touched file, so it is capped to a share of the
        // window height instead of pushing the diff off screen.
        string changelog = !string.IsNullOrWhiteSpace(_revision.ChangeLog)
            ? _revision.ChangeLog
            : (!string.IsNullOrWhiteSpace(_revision.UpdateLog) ? _revision.UpdateLog : "(no change log)");
        string[] logLines = changelog.Replace("\r\n", "\n").Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        int logBudget = Math.Max(2, Math.Min(6, Ui.BodyHeight / 4));
        int logShown = logLines.Length > logBudget ? logBudget - 1 : logLines.Length;

        Ui.Blank();
        Ui.Section("Change log", logLines.Length > logShown ? $"{logShown} of {logLines.Length} lines" : null);
        foreach (string line in logLines.Take(logShown))
            Ui.Line("   " + Markup.Escape(Ui.Fit(line.Trim(), Ui.Width - 3)));
        if (logLines.Length > logShown)
            Ui.Note($"   +{logLines.Length - logShown} more");
        Ui.Blank();

        if (_isCurrent)
        {
            Ui.Section("Checkout");
            Ui.Note("   This is the current main version - nothing to check out.");
        }
        else if (_diff is null)
        {
            Ui.Section("Checkout");
            Ui.Note("   Computing diff...");
        }
        else if (_diff.Count == 0)
        {
            Ui.Section("Checkout");
            Ui.Note("   No file differences against the current main version.");
        }
        else
        {
            // card(5) + blank + log section + log lines + blank + diff section + error line
            int reserved = 5 + 1 + 1 + logShown + (logLines.Length > logShown ? 1 : 0) + 1 + 1 + 2;
            _diffList!.SetViewportHeight(Ui.ListRows(reserved));
            int top = _diffList.ViewportTop;
            int last = Math.Min(_diff.Count, top + _diffList.ViewportHeight);

            Ui.Section("Files that change on checkout", Ui.Range(top, last - top, _diff.Count));
            var cols = new[] { new Col("State", 5), new Col("Path", isPath: true) };
            for (int i = top; i < last; i++)
            {
                var cf = _diff[i];
                var pf = cf.DstFile ?? cf.SrcFile;
                if (pf == null) continue;
                var (label, color) = TextStyle.StateBadge(pf.DataState);
                Ui.TableRow(cols, new (string, string?)[] { (label, $"{color} bold"), (pf.DataRelPath, color) },
                            selected: i == _diffList.SelectedIndex);
            }
        }

        if (_lastError != null)
        {
            Ui.Blank();
            Ui.Error(_lastError);
        }
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        // Any key other than the checkout keys clears the transient error.
        if (key.KeyChar != 'c' && key.KeyChar != 'C') _lastError = null;

        if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;

        // The gate's integrity check already re-hashes the working directory, making the
        // old 'C' (CleanRestore) binding redundant; the mode survives in Core only.
        if (key.KeyChar == 'c' || key.KeyChar == 'C') return PushCheckout(CheckoutMode.Fast);

        // Case-insensitive, unlike c/C — these two have only one meaning each.
        if (key.KeyChar == 'r' || key.KeyChar == 'R')
            return new ScreenAction.Push(new VersionRenameScreen(_mgr, _revision));

        if (key.KeyChar == 'x' || key.KeyChar == 'X')
        {
            _deleteRequested = true;
            return new ScreenAction.Push(new VersionDeleteGateScreen(_mgr, _revision));
        }

        if (_diffList != null) _diffList.Handle(key);
        return ScreenAction.StayAction;
    }

    private ScreenAction PushCheckout(CheckoutMode mode)
    {
        if (_isCurrent && mode == CheckoutMode.Fast)
        {
            _lastError = "Already on this revision.";
            return ScreenAction.StayAction;
        }
        // Push the checkout gate, which runs an integrity check first and handles
        // the dirty/clean prompt logic. On success it pops and this screen's
        // AutoAdvance pops again, landing on the (rebuilt) revision list.
        _checkoutRequested = true;
        return new ScreenAction.Push(new CheckoutGateScreen(_mgr, _revision, mode));
    }

    public override ScreenAction? AutoAdvance()
    {
        // Peek — never consume — the manager's read-once flags: RevisionListScreen is the
        // consumer, and it needs the flag to survive this hop to rebuild its rows.
        if (_deleteRequested && _mgr.LastDeletedVersion == _revision.UpdatedVersion)
            return ScreenAction.PopAction;   // this revision no longer exists
        if (_checkoutRequested && _mgr.LastCheckedOut != null)
            return ScreenAction.PopAction;   // main moved; the diff shown here is stale
        return null;
    }
}

#pragma warning restore CS0618
