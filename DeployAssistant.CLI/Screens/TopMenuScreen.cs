using System;
using DeployAssistant.CLI.Engine;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class TopMenuScreen : Screen
{
    private static readonly string[] Items = { "Switch project", "Initialize new project", "Quit" };
    private readonly bool _loaded;
    private int _selected;

    public TopMenuScreen(bool loaded) { _loaded = loaded; }

    public override void Render()
    {
        AnsiConsole.MarkupLine(TextStyle.Accent("  Project ▾"));
        AnsiConsole.WriteLine();
        for (int i = 0; i < Items.Length; i++)
        {
            string marker = i == _selected ? TextStyle.SelectionMarker : " ";
            string label = i == _selected ? TextStyle.Accent(Items[i]) : Items[i];
            AnsiConsole.MarkupLine($" {marker} {label}");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · enter select · esc back"));
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
