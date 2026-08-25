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

    public override void Render()
    {
        switch (_phase)
        {
            case Phase.Preview:
                AnsiConsole.Write(BuildPlanCard(_plan!));
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(TextStyle.Dim("  This removes the version from the history permanently."));
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(TextStyle.Dim("  y delete · n/esc cancel"));
                return;

            case Phase.Blocked:
                AnsiConsole.Write(BuildBlockedCard());
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(TextStyle.Dim("  Press any key to return."));
                return;

            case Phase.Deleting:
                AnsiConsole.MarkupLine("  [cyan]Working...[/]");
                return;

            case Phase.Error:
                AnsiConsole.MarkupLine($"  [red]{TextStyle.ErrorGlyph} {Markup.Escape(_errorMessage ?? "Unknown error")}[/]");
                AnsiConsole.MarkupLine(TextStyle.Dim("  Press any key to return."));
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

    private Panel BuildPlanCard(VersionDeletePlan plan)
    {
        string body =
            $"{TextStyle.Bold("Version   ")}  {TextStyle.Accent(Markup.Escape(plan.VersionName))}\n" +
            $"{TextStyle.Bold("Folder    ")}  {Markup.Escape(plan.BackupFolderPath)}\n" +
            $"{TextStyle.Bold("Reclaims  ")}  {FormatBytes(plan.ReclaimableBytes)}   " +
                $"{TextStyle.Dim($"({plan.ExclusiveHashes.Count} exclusive backup file(s) deleted)")}\n" +
            $"{TextStyle.Bold("Keeps     ")}  {plan.SharedHashes.Count} shared backup file(s)   " +
                $"{TextStyle.Dim($"({plan.RelocationHashes.Count} relocated first)")}\n" +
            $"{TextStyle.Bold("Folder    ")}  {(plan.BackupFolderRemovable ? "removed after relocation" : TextStyle.Dim("kept (still referenced)"))}";

        return new Panel(body)
            .Header(TextStyle.Removed(Markup.Escape("Delete version")))
            .BorderColor(Color.Red);
    }

    private Panel BuildBlockedCard()
    {
        var lines = new List<string>
        {
            $"{TextStyle.Bold("Version   ")}  {Markup.Escape(_plan?.VersionName ?? _target.UpdatedVersion ?? "")}",
            "",
            "Cannot delete this version:",
        };
        foreach (string blocker in _messages)
            lines.Add($"  {TextStyle.ErrorGlyph} {Markup.Escape(blocker)}");
        if (_messages.Count == 0)
            lines.Add($"  {TextStyle.ErrorGlyph} (no reason reported)");

        return new Panel(string.Join("\n", lines))
            .Header(TextStyle.Modified(Markup.Escape("Delete refused")))
            .BorderColor(Color.Yellow);
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
