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


    public IntegrityResultScreen(MetaDataManager mgr, IEnumerable<ProjectFile> files)
    {
        _mgr = mgr;
        _files = files.ToList();
        _list = new SelectableList(_files.Count, 12);
    }

    public override string Title => "Integrity";

    public override IReadOnlyList<KeyHint> Hints => _files.Count == 0
        ? new[] { new KeyHint("any key", "back") }
        : new[]
        {
            new KeyHint(Glyphs.UpDown, "move"),
            new KeyHint("r", "revert"),
            new KeyHint("u", "commit"),
            new KeyHint("PgUp/PgDn", "page"),
            new KeyHint("esc", "back"),
        };

    public override void Render()
    {
        if (_files.Count == 0)
        {
            Ui.Blank();
            Ui.Box("Integrity check", new[]
            {
                $"{TextStyle.SuccessGlyph} [green]All files match the main version.[/]",
                TextStyle.Dim("No deviations detected."),
            }, Color.Green, "green bold");
            return;
        }

        // Summary, blank, section line, blank-or-error below the list.
        _list.SetViewportHeight(Ui.ListRows(reservedRows: 4));
        int top = _list.ViewportTop;
        int last = Math.Min(_files.Count, top + _list.ViewportHeight);

        Ui.Line(BuildSummary());
        Ui.Blank();
        Ui.Section("Changed files", Ui.Range(top, last - top, _files.Count));
        var cols = new[] { new Col("State", 5), new Col("Path", isPath: true) };
        for (int i = top; i < last; i++)
        {
            var (label, color) = TextStyle.StateBadge(_files[i].DataState);
            Ui.TableRow(cols, new (string, string?)[] { (label, $"{color} bold"), (_files[i].DataRelPath, color) },
                        selected: i == _list.SelectedIndex);
        }

        if (_lastError != null)
        {
            Ui.Blank();
            Ui.Error(_lastError);
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
                _files.RemoveAt(_list.SelectedIndex);
                _list.SetItemCount(_files.Count);
                _lastError = null;
            }
            else
            {
                _lastError = $"Revert failed: {selected.DataRelPath} (backup may be missing)";
            }
            return ScreenAction.StayAction;
        }

        // Handle 'u' (update as new version) before delegating to _list.Handle,
        // so it takes precedence over SelectableList's 'u' half-page-up binding.
        if (key.KeyChar == 'u')
        {
            if (_files.Count == 0)
            {
                _lastError = "Nothing to commit.";
                return ScreenAction.StayAction;
            }
            return new ScreenAction.Push(new UpdateVersionPromptScreen(_mgr, _files.Count));
        }

        _list.Handle(key);
        return ScreenAction.StayAction;
    }

    public override ScreenAction? AutoAdvance()
    {
        if (_mgr.ConsumeLastUpdated() != null)
            return ScreenAction.PopAction;
        return null;
    }

    private string BuildSummary()
    {
        int Count(DataState mask) => _files.Count(f => (f.DataState & mask) != 0);
        int mod = Count(DataState.Modified);
        int del = Count(DataState.Deleted);
        int add = Count(DataState.Added);
        int rst = Count(DataState.Restored);
        string Chip(int n, string label, string color) =>
            n > 0 ? $"[{color} bold]{n}[/] [{color}]{label}[/]" : TextStyle.Dim($"0 {label}");
        return "  " + string.Join("   ", Chip(mod, "modified", "yellow"), Chip(del, "deleted", "red"),
                                         Chip(add, "added", "green"), Chip(rst, "restored", "fuchsia"));
    }
}
