using System;
using System.Collections.Generic;
using System.Linq;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class RevisionListScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private readonly List<ProjectData> _rows;
    private readonly SelectableList _list;
    private const int ViewportHeight = 12;

    public RevisionListScreen(MetaDataManager mgr)
    {
        _mgr = mgr;
        _rows = mgr.ProjectMetaData?.ProjectDataList?.ToList() ?? new List<ProjectData>();
        _list = new SelectableList(_rows.Count, ViewportHeight);
    }

    public override void Render()
    {
        if (_rows.Count == 0)
        {
            AnsiConsole.MarkupLine(TextStyle.Dim("No revisions recorded."));
            AnsiConsole.MarkupLine(TextStyle.Dim("Press esc to return."));
            return;
        }

        int top = _list.ViewportTop;
        int last = Math.Min(_rows.Count, top + ViewportHeight);
        AnsiConsole.MarkupLine(TextStyle.Accent($"  {Markup.Escape(_mgr.ProjectMetaData?.ProjectName ?? "")}"));
        AnsiConsole.WriteLine();
        for (int i = top; i < last; i++)
        {
            var pd = _rows[i];
            bool isMain = pd.Equals(_mgr.MainProjectData);
            string mainMarker = isMain ? TextStyle.MainMarker : " ";
            string version = isMain
                ? TextStyle.Accent(Markup.Escape(pd.UpdatedVersion ?? ""))
                : Markup.Escape(pd.UpdatedVersion ?? "");
            string selMarker = i == _list.SelectedIndex ? TextStyle.SelectionMarker : " ";
            string line = $" {selMarker} {mainMarker} #{i + 1}  {version}  " +
                          $"{pd.UpdatedTime:yyyy-MM-dd HH:mm}  by {Markup.Escape(pd.UpdaterName ?? "")}  " +
                          TextStyle.Dim($"({pd.NumberOfChanges} changes)");
            AnsiConsole.MarkupLine(line);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · d/u half-page · esc back"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
        _list.Handle(key);
        return ScreenAction.StayAction;
    }
}
