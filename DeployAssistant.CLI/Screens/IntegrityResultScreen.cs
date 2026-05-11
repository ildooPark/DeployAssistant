using System;
using System.Collections.Generic;
using System.Linq;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class IntegrityResultScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private readonly List<ProjectFile> _files;
    private SelectableList _list;
    private string? _lastError;
    private const int ViewportHeight = 12;

    public IntegrityResultScreen(MetaDataManager mgr, IEnumerable<ProjectFile> files)
    {
        _mgr = mgr;
        _files = files.ToList();
        _list = new SelectableList(_files.Count, ViewportHeight);
    }

    public override void Render()
    {
        if (_files.Count == 0)
        {
            AnsiConsole.MarkupLine($"{TextStyle.SuccessGlyph} All files match — no deviations detected.");
            AnsiConsole.MarkupLine(TextStyle.Dim("Press any key to return."));
            return;
        }

        AnsiConsole.MarkupLine(BuildSummary());
        AnsiConsole.MarkupLine(TextStyle.Dim("─────────────────────────────────────────────"));

        int top = _list.ViewportTop;
        int last = Math.Min(_files.Count, top + ViewportHeight);
        for (int i = top; i < last; i++)
        {
            string row = TextStyle.FormatFileState(_files[i].DataState, _files[i].DataRelPath);
            string marker = i == _list.SelectedIndex ? TextStyle.SelectionMarker : " ";
            AnsiConsole.MarkupLine($" {marker}{row}");
        }
        AnsiConsole.MarkupLine(TextStyle.Dim("─────────────────────────────────────────────"));
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · d/u half-page · r revert · esc back"));

        if (_lastError != null)
        {
            AnsiConsole.MarkupLine($"  [red]{_lastError}[/]");
        }
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        // Any non-r key clears the transient error.
        if (key.Key != ConsoleKey.R) _lastError = null;

        if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
        if (_files.Count == 0) return ScreenAction.PopAction;

        if (key.Key == ConsoleKey.R)
        {
            var selected = _files[_list.SelectedIndex];
            bool confirmed = TuiPrompt.Confirm(
                "Revert change",
                $"Revert {selected.DataRelPath}? Local change will be replaced with backup contents.");
            if (!confirmed)
            {
                _lastError = null;
                return ScreenAction.StayAction;
            }

            bool success = _mgr.RequestRevertChange(selected);
            if (success)
            {
                int prevIndex = _list.SelectedIndex;
                _files.RemoveAt(prevIndex);
                int newCount = _files.Count;
                int newIndex = Math.Min(prevIndex, newCount - 1);
                _list = new SelectableList(newCount, ViewportHeight);
                if (newIndex > 0)
                {
                    // Advance selection to match previous position clamped to valid range.
                    for (int i = 0; i < newIndex; i++) _list.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                }
                _lastError = null;
            }
            else
            {
                _lastError = $"Revert failed: {Markup.Escape(selected.DataRelPath)} (backup may be missing)";
            }
            return ScreenAction.StayAction;
        }

        _list.Handle(key);
        return ScreenAction.StayAction;
    }

    private string BuildSummary()
    {
        int Count(DataState mask) => _files.Count(f => (f.DataState & mask) != 0);
        int mod = Count(DataState.Modified);
        int del = Count(DataState.Deleted);
        int add = Count(DataState.Added);
        int rst = Count(DataState.Restored);
        return $"{mod} modified · {del} deleted · {add} added · {rst} restored";
    }
}
