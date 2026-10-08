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


    public RevisionListScreen(MetaDataManager mgr)
    {
        _mgr = mgr;
        _rows = mgr.ProjectMetaData?.ProjectDataList?.ToList() ?? new List<ProjectData>();
        _list = new SelectableList(_rows.Count, 12);
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

    public override string Title => "Revisions";

    public override IReadOnlyList<KeyHint> Hints => _rows.Count == 0
        ? new[] { new KeyHint("esc", "back") }
        : new[]
        {
            new KeyHint(Glyphs.UpDown, "move"),
            new KeyHint("enter", "inspect"),
            new KeyHint("PgUp/PgDn", "page"),
            new KeyHint("esc", "back"),
        };

    public override void Render()
    {
        if (_rows.Count == 0)
        {
            Ui.Blank();
            Ui.Note("  No revisions recorded.");
            return;
        }

        // Section line + column header + blank line around the list.
        _list.SetViewportHeight(Ui.ListRows(reservedRows: 3));
        int top = _list.ViewportTop;
        int last = Math.Min(_rows.Count, top + _list.ViewportHeight);

        Col[] cols = Ui.Compact
            ? new[] { new Col("", 1), new Col("#", 4, alignRight: true), new Col("Version"), new Col("Updated", 16), new Col("Changes", 7, alignRight: true) }
            : new[] { new Col("", 1), new Col("#", 4, alignRight: true), new Col("Version", 24), new Col("Updated", 16), new Col("By"), new Col("Changes", 7, alignRight: true) };

        Ui.Section("History", Ui.Range(top, last - top, _rows.Count));
        Ui.TableHeader(cols);
        for (int i = top; i < last; i++)
        {
            var pd = _rows[i];
            bool isMain = pd.Equals(_mgr.MainProjectData);
            var cells = new List<(string, string?)>
            {
                (isMain ? Glyphs.Main : "", "aqua bold"),
                ($"{i + 1}", "grey"),
                (pd.UpdatedVersion ?? "", isMain ? "aqua bold" : null),
                ($"{pd.UpdatedTime:yyyy-MM-dd HH:mm}", null),
            };
            if (!Ui.Compact) cells.Add((pd.UpdaterName ?? "", "grey"));
            cells.Add(($"{pd.NumberOfChanges}", "grey"));
            Ui.TableRow(cols, cells, selected: i == _list.SelectedIndex);
        }
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
