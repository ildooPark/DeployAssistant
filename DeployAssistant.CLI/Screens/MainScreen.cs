using System;
using System.Collections.Generic;
using DeployAssistant.CLI.Engine;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class MainScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private int _selected;

    private static readonly (string Label, string Description)[] MenuItems =
    {
        ("Integrity check", "hash the working folder against the main version"),
        ("List revisions", "browse history, checkout, rename or delete versions"),
    };

    public MainScreen(MetaDataManager mgr) { _mgr = mgr; }

    public override string Title => _mgr.ProjectMetaData?.ProjectName ?? "Project";

    public override IReadOnlyList<KeyHint> Hints => new[]
    {
        new KeyHint(Glyphs.UpDown, "move"),
        new KeyHint("enter", "open"),
        new KeyHint("m", "projects"),
        new KeyHint("q", "quit"),
    };

    public override void Render()
    {
        var pd = _mgr.MainProjectData;
        int revCount = _mgr.ProjectMetaData?.ProjectDataList.Count ?? 0;
        string updated = pd != null ? $"{pd.UpdatedTime:yyyy-MM-dd HH:mm}  by {pd.UpdaterName}" : "";

        Ui.Card(_mgr.ProjectMetaData?.ProjectName ?? "Unknown", new[]
        {
            new CardRow("Path", pd?.ProjectPath ?? "", isPath: true),
            new CardRow("Version", pd?.UpdatedVersion ?? "Undefined", style: "aqua bold", note: "main"),
            new CardRow("Updated", updated),
            new CardRow("Files", $"{pd?.ProjectFiles.Count ?? 0}", note: $"{revCount} revision(s)"),
        }, TextStyle.AccentColor);
        Ui.Blank();
        Ui.Section("Actions");
        Ui.Blank();
        Ui.Menu(MenuItems, _selected);
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_selected > 0) _selected--;
                return ScreenAction.StayAction;

            case ConsoleKey.DownArrow:
                if (_selected < MenuItems.Length - 1) _selected++;
                return ScreenAction.StayAction;

            case ConsoleKey.Enter:
                return _selected switch
                {
                    0 => new ScreenAction.Push(new IntegrityRunScreen(_mgr)),
                    1 => new ScreenAction.Push(new RevisionListScreen(_mgr)),
                    _ => ScreenAction.StayAction,
                };
        }

        return key.KeyChar switch
        {
            'm' or 'M' => new ScreenAction.Push(new TopMenuScreen(loaded: true)),
            'q' or 'Q' => ScreenAction.ExitAction,
            _ => ScreenAction.StayAction,
        };
    }
}
