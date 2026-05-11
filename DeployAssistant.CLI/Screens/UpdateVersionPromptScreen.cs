using System;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class UpdateVersionPromptScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private readonly int _changeCount;
    private readonly LineInput _updaterInput;
    private readonly LineInput _logInput;
    private int _focusedField; // 0 = updater, 1 = log
    private string? _lastError;

    public UpdateVersionPromptScreen(MetaDataManager mgr, int changeCount)
    {
        _mgr = mgr;
        _changeCount = changeCount;
        _updaterInput = new LineInput();
        _updaterInput.SetText(Environment.UserName ?? "");
        _logInput = new LineInput();
        _focusedField = 0;
    }

    public override void Render()
    {
        AnsiConsole.MarkupLine($"  [bold]Update as new version[/]");
        AnsiConsole.MarkupLine($"  [dim]{_changeCount} detected change(s) will be committed as a new revision.[/]");
        AnsiConsole.WriteLine();

        RenderField("Updater name", _updaterInput, _focusedField == 0);
        RenderField("Update log",   _logInput,     _focusedField == 1);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("  Tab next field · Enter submit · esc cancel"));

        if (_lastError != null)
            AnsiConsole.MarkupLine($"  [red]{Markup.Escape(_lastError)}[/]");
    }

    private static void RenderField(string label, LineInput input, bool focused)
    {
        string marker = focused ? TextStyle.SelectionMarker : " ";
        string content = string.IsNullOrEmpty(input.Text) ? TextStyle.Dim("(empty)") : Markup.Escape(input.Text);
        string suffix = focused ? "_" : "";
        AnsiConsole.MarkupLine($" {marker}{label}: {content}{suffix}");
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        if (_lastError != null && key.Key != ConsoleKey.Enter) _lastError = null;

        if (key.Key == ConsoleKey.Escape) return ScreenAction.PopAction;

        if (key.Key == ConsoleKey.Tab)
        {
            _focusedField = (_focusedField + 1) % 2;
            return ScreenAction.StayAction;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (_focusedField == 0)
            {
                _focusedField = 1;
                return ScreenAction.StayAction;
            }
            // Both fields entered — submit.
            return Submit();
        }

        // Forward typing to the focused input.
        var input = _focusedField == 0 ? _updaterInput : _logInput;
        input.Handle(key);
        return ScreenAction.StayAction;
    }

    private ScreenAction Submit()
    {
        string updater = _updaterInput.Text.Trim();
        string log = _logInput.Text.Trim();
        string? projectPath = _mgr.ProjectMetaData?.ProjectPath;

        if (string.IsNullOrWhiteSpace(updater))
        {
            _lastError = "Updater name cannot be empty.";
            _focusedField = 0;
            return ScreenAction.StayAction;
        }

        bool confirmed = TuiPrompt.Confirm(
            "Create new revision",
            $"Commit {_changeCount} changes as a new revision by '{updater}'?");
        if (!confirmed) return ScreenAction.StayAction;

        bool success = _mgr.RequestProjectUpdate(updater, log, projectPath);
        if (success)
        {
            // IntegrityResultScreen.AutoAdvance will detect LastUpdated and pop again
            // (landing back on MainScreen).
            return ScreenAction.PopAction;
        }
        else
        {
            _lastError = "Update failed (see trace logs).";
            return ScreenAction.StayAction;
        }
    }
}
