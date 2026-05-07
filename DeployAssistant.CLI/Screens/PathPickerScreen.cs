using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Engine.Widgets;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class PathPickerScreen : Screen
{
    public enum Mode { Switch, Init }

    private readonly LineInput _input = new LineInput();
    private readonly Mode _mode;
    private string? _error;

    public PathPickerScreen(Mode mode) : this(mode, DefaultCandidates) { }

    // Test seam — accept a custom candidate provider.
    internal PathPickerScreen(Mode mode, Func<string, string[]> candidates)
    {
        _mode = mode;
        _input.CandidateProvider = candidates;
    }

    public override void Render()
    {
        AnsiConsole.MarkupLine(_mode == Mode.Switch
            ? "Open project at:"
            : "Initialize new project at:");
        AnsiConsole.MarkupLine($"[cyan]>[/] {Markup.Escape(_input.Text)}[underline cyan] [/]");

        if (_input.CandidateMode)
        {
            for (int i = 0; i < _input.Candidates.Count; i++)
            {
                string marker = i == _input.CandidateIndex ? TextStyle.SelectionMarker : " ";
                AnsiConsole.MarkupLine($"  {marker} {Markup.Escape(_input.Candidates[i])}");
            }
        }

        if (_error is not null)
            AnsiConsole.MarkupLine($"{TextStyle.ErrorGlyph} {Markup.Escape(_error)}");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("Tab complete · ↑↓ pick · enter open · esc cancel"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape && !_input.CandidateMode)
            return ScreenAction.PopAction;

        if (key.Key == ConsoleKey.Enter && !_input.CandidateMode)
            return TryAccept();

        _input.Handle(key);
        _error = null;
        return ScreenAction.StayAction;
    }

    private ScreenAction TryAccept()
    {
        string path = _input.Text.TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(path))
        {
            _error = "Path is empty.";
            return ScreenAction.StayAction;
        }
        if (!Directory.Exists(path))
        {
            _error = $"Directory not found: {path}";
            return ScreenAction.StayAction;
        }

        if (_mode == Mode.Switch)
        {
            var mgr = ManagerFactory.LoadOrThrow(path, out string? loadError);
            if (mgr is null)
            {
                _error = loadError ?? "Failed to load project.";
                return ScreenAction.StayAction;
            }
            return new ScreenAction.Replace(new MainScreen(mgr));
        }

        return new ScreenAction.Replace(new InitRunScreen(path));
    }

    private static string[] DefaultCandidates(string fullText)
    {
        try
        {
            string text = fullText ?? "";

            // Bare drive root: user typed "C:" with no separator → enumerate "C:\".
            if (text.Length == 2 && char.IsLetter(text[0]) && text[1] == ':')
            {
                string root = text + Path.DirectorySeparatorChar;
                if (!Directory.Exists(root)) return Array.Empty<string>();
                return EnumerateDirectoryNames(root, partial: "");
            }

            int sep = LastSep(text);
            string parent = sep >= 0 ? text.Substring(0, sep + 1) : "";
            string partial = sep >= 0 ? text.Substring(sep + 1) : text;

            if (string.IsNullOrEmpty(parent)) return Array.Empty<string>();
            if (!Directory.Exists(parent)) return Array.Empty<string>();

            return EnumerateDirectoryNames(parent, partial);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] EnumerateDirectoryNames(string parent, string partial)
    {
        return Directory.EnumerateDirectories(parent)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n) && n[0] != '.')
            .Where(n => n!.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static int LastSep(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
            if (s[i] == '\\' || s[i] == '/') return i;
        return -1;
    }

    // ----- test hooks (internal only) -----
    internal bool IsInCandidateMode => _input.CandidateMode;
    internal IReadOnlyList<string> CurrentCandidates => _input.Candidates;
}
