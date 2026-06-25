using System;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

/// <summary>
/// Prompts the user to enter a friendly name for the project being saved.
/// After confirmation, saves to the registry and transitions to MainScreen.
/// </summary>
internal sealed class ProjectNameScreen : Screen
{
    private readonly string _path;
    private readonly Action<string, string> _onComplete;
    private readonly LineInput _input = new LineInput();
    private readonly string _defaultName;

    /// <param name="path">The project path being registered.</param>
    /// <param name="defaultName">A default name suggestion (e.g. folder name).</param>
    /// <param name="onComplete">Callback(path, name) invoked on confirm.</param>
    public ProjectNameScreen(string path, string defaultName, Action<string, string> onComplete)
    {
        _path = path;
        _defaultName = defaultName;
        _onComplete = onComplete;
        _input.SetText(defaultName);
    }

    public override void Render()
    {
        AnsiConsole.MarkupLine("Set a name for this project:");
        AnsiConsole.MarkupLine(TextStyle.Dim($"  Path: {Markup.Escape(_path)}"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]>[/] {Markup.Escape(_input.Text)}[underline cyan] [/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("enter confirm · esc use default name"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            string name = string.IsNullOrWhiteSpace(_input.Text) ? _defaultName : _input.Text.Trim();
            _onComplete(_path, name);
            return ScreenAction.PopAction;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            _onComplete(_path, _defaultName);
            return ScreenAction.PopAction;
        }

        _input.Handle(key);
        return ScreenAction.StayAction;
    }
}
