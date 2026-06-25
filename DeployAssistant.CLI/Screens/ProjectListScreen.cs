using System;
using System.Collections.Generic;
using System.Linq;
using DeployAssistant.CLI.Engine;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

/// <summary>
/// Lists previously configured projects for quick switching.
/// The last item is always "Browse for path..." which falls through to PathPickerScreen.
/// </summary>
internal sealed class ProjectListScreen : Screen
{
    private List<ProjectEntry> _entries;
    private int _selected;

    public ProjectListScreen()
    {
        _entries = LoadEntries();
    }

    public override void OnEnter()
    {
        // Reload in case entries were added via PathPickerScreen (Browse).
        _entries = LoadEntries();
        if (_selected >= _entries.Count + 1) _selected = _entries.Count;
    }

    private static List<ProjectEntry> LoadEntries() =>
        ProjectRegistry.Load()
            .OrderByDescending(e => e.LastAccessedUtc)
            .ToList();

    public override void Render()
    {
        AnsiConsole.MarkupLine(TextStyle.Accent("  Switch Project"));
        AnsiConsole.WriteLine();

        if (_entries.Count == 0)
        {
            AnsiConsole.MarkupLine(TextStyle.Dim("  No saved projects."));
            AnsiConsole.WriteLine();
        }
        else
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                string marker = i == _selected ? TextStyle.SelectionMarker : " ";
                string name = Markup.Escape(_entries[i].Name);
                string path = Markup.Escape(_entries[i].Path);
                string label = i == _selected
                    ? $"{TextStyle.Accent(name)}  {TextStyle.Dim(path)}"
                    : $"{name}  {TextStyle.Dim(path)}";
                AnsiConsole.MarkupLine($" {marker} {label}");
            }
            AnsiConsole.WriteLine();
        }

        // "Browse..." option at the end
        int browseIdx = _entries.Count;
        string browseMarker = _selected == browseIdx ? TextStyle.SelectionMarker : " ";
        string browseLabel = _selected == browseIdx
            ? TextStyle.Accent("Browse for path...")
            : "Browse for path...";
        AnsiConsole.MarkupLine($" {browseMarker} {browseLabel}");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · enter open · r rename · del remove · esc back"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        int totalItems = _entries.Count + 1; // entries + "Browse..."

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_selected > 0) _selected--;
                return ScreenAction.StayAction;

            case ConsoleKey.DownArrow:
                if (_selected < totalItems - 1) _selected++;
                return ScreenAction.StayAction;

            case ConsoleKey.Escape:
                return ScreenAction.PopAction;

            case ConsoleKey.Delete:
                if (_selected < _entries.Count)
                {
                    ProjectRegistry.Remove(_entries[_selected].Path);
                    _entries.RemoveAt(_selected);
                    if (_selected >= _entries.Count && _selected > 0) _selected--;
                }
                return ScreenAction.StayAction;

            case ConsoleKey.Enter:
                if (_selected == _entries.Count)
                {
                    // "Browse..." selected
                    return new ScreenAction.Push(new PathPickerScreen(PathPickerScreen.Mode.Switch));
                }
                return TryOpenProject(_entries[_selected]);
        }

        if ((key.KeyChar == 'r' || key.KeyChar == 'R') && _selected < _entries.Count)
        {
            var entry = _entries[_selected];
            return new ScreenAction.Push(new ProjectNameScreen(entry.Path, entry.Name, (p, n) =>
            {
                ProjectRegistry.SaveOrUpdate(p, n);
                entry.Name = n;
            }));
        }

        return ScreenAction.StayAction;
    }

    private ScreenAction TryOpenProject(ProjectEntry entry)
    {
        var mgr = ManagerFactory.LoadOrThrow(entry.Path, out string? error);
        if (mgr is null)
        {
            AnsiConsole.MarkupLine($"{TextStyle.ErrorGlyph} {Markup.Escape(error ?? "Failed to load project.")}");
            return ScreenAction.StayAction;
        }

        // Update last-accessed time
        ProjectRegistry.SaveOrUpdate(entry.Path, entry.Name);
        return new ScreenAction.Replace(new MainScreen(mgr));
    }
}
