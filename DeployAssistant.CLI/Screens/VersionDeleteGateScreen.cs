using System;
using System.Collections.Generic;
using DeployAssistant.CLI.Engine;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

#pragma warning disable CS0618  // ProjectData is a V1 type; version management is V1-shaped.

/// <summary>
/// Pre-delete gate for one stored version. Builds a <see cref="VersionDeletePlan"/> through
/// MetaDataManager (which mutates nothing), shows the blast radius — reclaimable bytes,
/// exclusive blobs, blobs that have to be relocated into a surviving folder — and only then
/// accepts an explicit 'y'. A plan carrying <see cref="VersionDeletePlan.Blockers"/> renders
/// them and refuses outright; the delete key is never offered in that phase.
/// </summary>
internal sealed class VersionDeleteGateScreen : Screen
{
    internal enum Phase { Preview, Blocked, Deleting, Error }

    private readonly MetaDataManager _mgr;
    private readonly ProjectData _target;
    private Phase _phase = Phase.Preview;
    private VersionDeletePlan? _plan;
    private List<string> _messages = new List<string>();
    private string? _errorMessage;
    private bool _previewed;

    public VersionDeleteGateScreen(MetaDataManager mgr, ProjectData target)
    {
        _mgr = mgr;
        _target = target;
    }

    // internal test seams
    internal Phase CurrentPhase => _phase;
    internal string? ErrorMessage => _errorMessage;
    internal IReadOnlyList<string> BlockReasons => _messages;

    internal void SetPhaseForTesting(Phase phase, VersionDeletePlan? plan = null)
    {
        _previewed = true;
        _phase = phase;
        _plan = plan;
        if (plan != null) _messages = new List<string>(plan.Blockers);
    }

    public override void OnEnter()
    {
        if (_previewed) return; // re-entry guard — the preview is built exactly once
        _previewed = true;

        _plan = _mgr.RequestVersionDeletePreview(_target);
        if (_plan == null)
        {
            _phase = Phase.Error;
            _errorMessage = "No project is loaded, or the version could not be resolved.";
            return;
        }
        if (!_plan.CanDelete)
        {
            _phase = Phase.Blocked;
            _messages = new List<string>(_plan.Blockers);
            return;
        }
        _phase = Phase.Preview;
    }

    public override string Title => "Delete";

    public override IReadOnlyList<KeyHint> Hints => _phase switch
    {
        Phase.Preview => new[] { new KeyHint("y", "delete permanently"), new KeyHint("n/esc", "cancel") },
        Phase.Blocked or Phase.Error => new[] { new KeyHint("any key", "back") },
        _ => Array.Empty<KeyHint>(),
    };

    public override void Render()
    {
        switch (_phase)
        {
            case Phase.Preview:
                RenderPlanCard(_plan!);
                Ui.Blank();
                Ui.Line($"  [red]{Glyphs.Warn}[/] This removes the version from the history permanently.");
                return;

            case Phase.Blocked:
                RenderBlockedCard();
                return;

            case Phase.Deleting:
                Ui.Blank();
                Ui.Line("  [aqua]Deleting...[/]");
                return;

            case Phase.Error:
                Ui.Box("Delete failed", new[] { $"[red]{Markup.Escape(_errorMessage ?? "Unknown error")}[/]" },
                       Color.Red, "red bold");
                return;
        }
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        switch (_phase)
        {
            case Phase.Preview:
                if (key.Key == ConsoleKey.Y) return Delete();
                if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
                return ScreenAction.StayAction;

            case Phase.Deleting:
                return ScreenAction.StayAction;

            case Phase.Blocked:
            case Phase.Error:
                return ScreenAction.PopAction;
        }
        return ScreenAction.StayAction;
    }

    private ScreenAction Delete()
    {
        _phase = Phase.Deleting;

        VersionDeleteResult? captured = null;
        void OnComplete(VersionDeleteResult r) => captured = r;

        bool ok;
        _mgr.VersionDeleteCompleteEventHandler += OnComplete;
        try
        {
            // The 'y' above IS the confirmation; the Core-level dialog prompt would be a
            // second, non-TUI one (ConsoleDialogService writes to stderr and reads a line).
            ok = _mgr.RequestDeleteVersion(_target, confirmed: true);
        }
        finally
        {
            _mgr.VersionDeleteCompleteEventHandler -= OnComplete;
        }

        if (ok)
        {
            // RevisionDetailScreen.AutoAdvance sees LastDeletedVersion and pops on to the
            // list, which consumes the flag and rebuilds its rows.
            return ScreenAction.PopAction;
        }

        _phase = captured != null && captured.Outcome == VersionDeleteOutcome.Blocked
            ? Phase.Blocked
            : Phase.Error;
        if (_phase == Phase.Blocked)
            _messages = new List<string>(captured!.Messages);
        else
            _errorMessage = Describe(captured, "Delete failed (see trace logs).");
        return ScreenAction.StayAction;
    }

    private static void RenderPlanCard(VersionDeletePlan plan)
    {
        Ui.Card("Delete version", new[]
        {
            new CardRow("Version", plan.VersionName, style: "aqua bold"),
            new CardRow("Backup folder", plan.BackupFolderPath, isPath: true),
            new CardRow("Reclaims", FormatBytes(plan.ReclaimableBytes),
                        note: $"{plan.ExclusiveHashes.Count} exclusive backup file(s) deleted"),
            new CardRow("Keeps", $"{plan.SharedHashes.Count} shared backup file(s)",
                        note: $"{plan.RelocationHashes.Count} relocated first"),
            new CardRow("Folder", plan.BackupFolderRemovable ? "removed after relocation" : "kept (still referenced)",
                        style: plan.BackupFolderRemovable ? null : "grey"),
        }, Color.Red, "red bold");
    }

    private void RenderBlockedCard()
    {
        var lines = new List<string>
        {
            $"[grey]Version[/]  {TextStyle.Accent(Markup.Escape(Ui.Fit(_plan?.VersionName ?? _target.UpdatedVersion ?? "", Ui.Width - 14)))}",
            "",
            "Cannot delete this version:",
        };
        foreach (string blocker in _messages)
            lines.Add($"  {TextStyle.ErrorGlyph} {Markup.Escape(blocker)}");
        if (_messages.Count == 0)
            lines.Add($"  {TextStyle.ErrorGlyph} (no reason reported)");

        Ui.Box("Delete refused", lines, Color.Yellow, "yellow bold");
    }

    private static string Describe(VersionDeleteResult? result, string fallback)
    {
        if (result == null || result.Messages.Count == 0) return fallback;
        return string.Join(" ", result.Messages);
    }

    /// <summary>Human-readable byte count. Kept here (not TextStyle) — nothing else needs it yet.</summary>
    internal static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";

        string[] units = { "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024d;
            unit++;
        }
        while (value >= 1024d && unit < units.Length - 1);
        return $"{value:0.#} {units[unit]}";
    }
}

#pragma warning restore CS0618
