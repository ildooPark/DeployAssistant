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
    private List<ProjectData> _rows;
    private readonly SelectableList _list;
    private const int ViewportHeight = 12;

    public RevisionListScreen(MetaDataManager mgr)
    {
        _mgr = mgr;
        _rows = mgr.ProjectMetaData?.ProjectDataList?.ToList() ?? new List<ProjectData>();
        _list = new SelectableList(_rows.Count, ViewportHeight);
    }

    /// <summary>
    /// The rows are a snapshot of ProjectDataList, so a delete performed further down the
    /// stack (detail → delete gate) would leave a row here pointing at a version that no
    /// longer exists. OnEnter runs again every time this screen returns to top-of-stack,
    /// which is exactly when the snapshot has to be retaken.
    /// </summary>
    public override void OnEnter() => RebuildRows();

    private void RebuildRows()
    {
        _rows = _mgr.ProjectMetaData?.ProjectDataList?.ToList() ?? _rows;
        _list.SetItemCount(_rows.Count);  // clamps the selection if the list shrank
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
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · d/u half-page · enter inspect (checkout · rename · delete) · esc back"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;
        if (key.Key == ConsoleKey.Enter)
        {
            if (_rows.Count > 0)
                return new ScreenAction.Push(new RevisionDetailScreen(_mgr, _rows[_list.SelectedIndex]));
            return ScreenAction.StayAction;
        }
        _list.Handle(key);
        return ScreenAction.StayAction;
    }

    public override ScreenAction? AutoAdvance()
    {
        if (_mgr.ConsumeLastCheckedOut() != null)
            return ScreenAction.PopAction;
        if (_mgr.ConsumeLastDeletedVersion() != null)
        {
            // Belt-and-braces: OnEnter already retook the snapshot on the way back here.
            // Consuming the flag stops it leaking into a later visit.
            RebuildRows();
            return null;
        }
        return null;
    }
}
