using System;
using System.Collections.Generic;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

#pragma warning disable CS0618  // ProjectData is a V1 type; version management is V1-shaped.

/// <summary>
/// Re-tags one stored version: its <c>UpdatedVersion</c> name, its <c>UpdateLog</c>, or both.
/// Fields start pre-filled with the current values; a field left untouched is sent as
/// <c>null</c>, which MetaDataManager reads as "leave alone" — so a log-only edit never
/// trips the version-name validation.
/// </summary>
internal sealed class VersionRenameScreen : Screen
{
    internal enum Phase { Edit, Done }

    private readonly MetaDataManager _mgr;
    private readonly ProjectData _revision;
    private readonly LineInput _nameInput;
    private readonly LineInput _logInput;
    private readonly string _originalName;
    private readonly string _originalLog;
    private Phase _phase = Phase.Edit;
    private int _focusedField; // 0 = version name, 1 = update log
    private string? _lastError;
    private List<string> _notices = new List<string>();

    public VersionRenameScreen(MetaDataManager mgr, ProjectData revision)
    {
        _mgr = mgr;
        _revision = revision;
        _originalName = revision.UpdatedVersion ?? "";
        _originalLog = revision.UpdateLog ?? "";
        _nameInput = new LineInput();
        _nameInput.SetText(_originalName);
        _logInput = new LineInput();
        _logInput.SetText(_originalLog);
        _focusedField = 0;
    }

    // internal test seams
    internal Phase CurrentPhase => _phase;
    internal string? LastError => _lastError;
    internal string NameText => _nameInput.Text;
    internal string LogText => _logInput.Text;
    internal int FocusedField => _focusedField;

    public override void Render()
    {
        if (_phase == Phase.Done)
        {
            AnsiConsole.MarkupLine($"  [bold]{TextStyle.SuccessGlyph} Version updated[/]");
            AnsiConsole.MarkupLine($"  {Markup.Escape(_originalName)} → {TextStyle.Accent(Markup.Escape(_revision.UpdatedVersion ?? ""))}");
            AnsiConsole.WriteLine();
            foreach (string notice in _notices)
                AnsiConsole.MarkupLine(TextStyle.Dim($"  {Markup.Escape(notice)}"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(TextStyle.Dim("  Press any key to return."));
            return;
        }

        AnsiConsole.MarkupLine("  [bold]Rename revision[/]");
        AnsiConsole.MarkupLine(TextStyle.Dim($"  Currently tagged {Markup.Escape(_originalName)}."));
        AnsiConsole.WriteLine();

        RenderField("Version name", _nameInput, _focusedField == 0);
        RenderField("Update log  ", _logInput, _focusedField == 1);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("  Tab next field · Enter submit · esc cancel"));

        if (_lastError != null)
            AnsiConsole.MarkupLine($"  [red]{TextStyle.ErrorGlyph} {Markup.Escape(_lastError)}[/]");
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
        if (_phase == Phase.Done) return ScreenAction.PopAction;

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
            return Submit();
        }

        // Forward typing to the focused input.
        var input = _focusedField == 0 ? _nameInput : _logInput;
        input.Handle(key);
        return ScreenAction.StayAction;
    }

    private ScreenAction Submit()
    {
        // null = "leave alone". Only send what the user actually edited so an unchanged
        // name never has to pass the (stricter) version-name validation.
        string? newName = _nameInput.Text == _originalName ? null : _nameInput.Text;
        string? newLog = _logInput.Text == _originalLog ? null : _logInput.Text;

        if (newName == null && newLog == null)
        {
            _lastError = "Nothing to change.";
            return ScreenAction.StayAction;
        }

        VersionRenameResult? captured = null;
        void OnComplete(VersionRenameResult r) => captured = r;

        bool ok;
        _mgr.VersionRenameCompleteEventHandler += OnComplete;
        try
        {
            ok = _mgr.RequestRenameVersion(_revision, newName, newLog);
        }
        finally
        {
            _mgr.VersionRenameCompleteEventHandler -= OnComplete;
        }

        if (!ok)
        {
            // Every rejection reason (duplicate tag, invalid path characters, busy manager)
            // arrives on the event — surface it verbatim instead of a generic failure.
            _lastError = Describe(captured, "Rename failed (see trace logs).");
            if (newName != null) _focusedField = 0;
            return ScreenAction.StayAction;
        }

        _notices = captured != null ? new List<string>(captured.Messages) : new List<string>();
        _phase = Phase.Done;
        return ScreenAction.StayAction;
    }

    private static string Describe(VersionRenameResult? result, string fallback)
    {
        if (result == null || result.Messages.Count == 0) return fallback;
        return string.Join(" ", result.Messages);
    }
}

#pragma warning restore CS0618
