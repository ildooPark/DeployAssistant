using System;
using System.Collections.Generic;
using DeployAssistant.CLI.Engine;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class TopMenuScreen : Screen
{
    private static readonly (string Label, string Description)[] Items =
    {
        ("Switch project", "open a folder DeployAssistant already manages"),
        ("Initialize new project", "scan a folder and record its first version"),
        ("Quit", "leave DeployAssistant"),
    };
    private readonly bool _loaded;
    private int _selected;

    public TopMenuScreen(bool loaded) { _loaded = loaded; }

    public override string Title => "Projects";

    public override IReadOnlyList<KeyHint> Hints => new[]
    {
        new KeyHint(Glyphs.UpDown, "move"),
        new KeyHint("enter", "select"),
        new KeyHint("esc", _loaded ? "back" : "quit"),
    };

    public override void Render()
    {
        Ui.Blank();
        Ui.Section("Projects");
        Ui.Blank();
        Ui.Menu(Items, _selected);
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_selected > 0) _selected--;
                return ScreenAction.StayAction;

            case ConsoleKey.DownArrow:
                if (_selected < Items.Length - 1) _selected++;
                return ScreenAction.StayAction;

            case ConsoleKey.Escape:
                return _loaded ? ScreenAction.PopAction : ScreenAction.ExitAction;

            case ConsoleKey.Enter:
                return _selected switch
                {
                    0 => new ScreenAction.Push(new PathPickerScreen(PathPickerScreen.Mode.Switch)),
                    1 => new ScreenAction.Push(new PathPickerScreen(PathPickerScreen.Mode.Init)),
                    2 => ScreenAction.ExitAction,
                    _ => ScreenAction.StayAction,
                };
        }
        return ScreenAction.StayAction;
    }
}
