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
    private readonly List<ProjectFile> _files;
    private readonly SelectableList _list;
    private const int ViewportHeight = 12;

    public IntegrityResultScreen(IEnumerable<ProjectFile> files)
    {
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
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · d/u half-page · esc back"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
        if (_files.Count == 0) return ScreenAction.PopAction;
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
