using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeployAssistant.CLI.Engine.Widgets;

internal sealed class LineInput
{
    private readonly StringBuilder _buffer = new();
    private string[] _candidates = Array.Empty<string>();
    private int _candidateIndex;
    private bool _candidateMode;

    /// <summary>
    /// Returns matching directory names for the given full input text.
    /// The widget splits at the last directory separator: prefix is the parent
    /// to enumerate, suffix is the partial name to match.
    /// </summary>
    public Func<string, string[]>? CandidateProvider { get; set; }

    public string Text => _buffer.ToString();
    public int CursorIndex { get; private set; }
    public bool CandidateMode => _candidateMode;
    public IReadOnlyList<string> Candidates => _candidates;
    public int CandidateIndex => _candidateIndex;

    public void SetText(string value)
    {
        _buffer.Clear();
        _buffer.Append(value);
        CursorIndex = _buffer.Length;
        ExitCandidateMode();
    }

    public void Handle(ConsoleKeyInfo key)
    {
        if (_candidateMode)
        {
            HandleCandidateMode(key);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Tab:
                TryComplete();
                return;

            case ConsoleKey.Backspace:
                if (CursorIndex > 0)
                {
                    _buffer.Remove(CursorIndex - 1, 1);
                    CursorIndex--;
                }
                return;

            case ConsoleKey.LeftArrow:
                if (CursorIndex > 0) CursorIndex--;
                return;

            case ConsoleKey.RightArrow:
                if (CursorIndex < _buffer.Length) CursorIndex++;
                return;

            case ConsoleKey.Home:
                CursorIndex = 0;
                return;

            case ConsoleKey.End:
                CursorIndex = _buffer.Length;
                return;
        }

        if (!char.IsControl(key.KeyChar))
        {
            _buffer.Insert(CursorIndex, key.KeyChar);
            CursorIndex++;
        }
    }

    private void TryComplete()
    {
        if (CandidateProvider is null) return;
        var matches = CandidateProvider(Text) ?? Array.Empty<string>();
        if (matches.Length == 0) return;

        if (matches.Length == 1)
        {
            ApplyCandidate(matches[0]);
            return;
        }

        _candidates = matches;
        _candidateIndex = 0;
        _candidateMode = true;
    }

    private void HandleCandidateMode(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_candidateIndex > 0) _candidateIndex--;
                return;
            case ConsoleKey.DownArrow:
                if (_candidateIndex < _candidates.Length - 1) _candidateIndex++;
                return;
            case ConsoleKey.Enter:
                ApplyCandidate(_candidates[_candidateIndex]);
                ExitCandidateMode();
                return;
            case ConsoleKey.Escape:
                ExitCandidateMode();
                return;
        }

        // Any other key (typing, backspace) dismisses the list and re-applies.
        ExitCandidateMode();
        Handle(key);
    }

    private void ApplyCandidate(string name)
    {
        // Replace the partial suffix (after the last separator) with name + separator.
        string text = Text;
        int sep = LastSeparatorIndex(text);
        string parent = sep >= 0 ? text.Substring(0, sep + 1) : "";
        string newText = parent + name + Path.DirectorySeparatorChar;
        SetText(newText);
    }

    private static int LastSeparatorIndex(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
            if (s[i] == '\\' || s[i] == '/') return i;
        return -1;
    }

    private void ExitCandidateMode()
    {
        _candidates = Array.Empty<string>();
        _candidateIndex = 0;
        _candidateMode = false;
    }
}
