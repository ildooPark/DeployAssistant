using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeployAssistant.CLI.Engine.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DeployAssistant.CLI.Engine;

/// <summary>A key binding shown in the footer, e.g. ("enter", "open").</summary>
internal readonly struct KeyHint
{
    public KeyHint(string keys, string label) { Keys = keys; Label = label; }
    public string Keys { get; }
    public string Label { get; }
}

/// <summary>One label/value line inside a <see cref="Ui.Card"/>. Values are plain text and get fitted, never wrapped.</summary>
internal readonly struct CardRow
{
    public CardRow(string label, string value, string? style = null, string? note = null, bool isPath = false)
    {
        Label = label; Value = value; Style = style; Note = note; IsPath = isPath;
    }
    public string Label { get; }
    public string Value { get; }
    public string? Style { get; }
    public string? Note { get; }
    public bool IsPath { get; }
}

/// <summary>A table column for <see cref="Ui.TableRow"/>; <c>Width == 0</c> means "take the remaining space".</summary>
internal readonly struct Col
{
    public Col(string header, int width = 0, bool alignRight = false, bool isPath = false)
    {
        Header = header; Width = width; AlignRight = alignRight; IsPath = isPath;
    }
    public string Header { get; }
    public int Width { get; }
    public bool AlignRight { get; }
    public bool IsPath { get; }
}

/// <summary>
/// Layout kit for screen bodies. <see cref="Width"/> and <see cref="BodyHeight"/> describe the
/// canvas the frame renderer handed to the current screen; every helper sizes itself from them
/// and fits text by terminal cells (Hangul counts two), so nothing wraps and a row means one
/// line at any window size.
/// </summary>
internal static class Ui
{
    /// <summary>Widest canvas we draw; extra columns on big monitors stay empty instead of stretching rows.</summary>
    public const int MaxWidth = 120;
    public const int MinWidth = 60;
    public const int MinHeight = 16;

    public static int Width { get; internal set; } = 80;
    public static int BodyHeight { get; internal set; } = 18;

    /// <summary>Widths below this drop optional columns (updater name, descriptions).</summary>
    public static bool Compact => Width < 84;

    // ------------------------------------------------------------------ text fitting

    public static int Len(string s) => string.IsNullOrEmpty(s) ? 0 : new Segment(s).CellCount();

    /// <summary>Truncates to <paramref name="max"/> cells with a trailing ellipsis.</summary>
    public static string Fit(string? s, int max)
    {
        s ??= "";
        if (max <= 0) return "";
        if (Len(s) <= max) return s;
        string ell = Glyphs.Ellipsis;
        int budget = max - Len(ell);
        if (budget <= 0) return ell.Substring(0, Math.Min(ell.Length, max));
        return TakeCells(s, budget) + ell;
    }

    /// <summary>
    /// Path-aware fit: keeps the head (drive/root) and as much of the tail (the file name and
    /// its nearest folders) as fits, eliding the middle — the end of a path is the part users scan.
    /// </summary>
    public static string FitPath(string? path, int max)
    {
        path ??= "";
        if (Len(path) <= max) return path;
        string ell = Glyphs.Ellipsis;
        int sep = path.IndexOfAny(new[] { '\\', '/' });
        string head = sep >= 0 && sep < 4 ? path.Substring(0, sep + 1) : "";
        int tailBudget = max - Len(head) - Len(ell);
        if (tailBudget < 8) return Fit(path, max);
        string tail = TakeCellsFromEnd(path, tailBudget);
        // Prefer to resume at a folder boundary ("…\Line0\x.dll" over "…피\Line0\x.dll")
        // as long as that keeps at least half of the budget.
        int boundary = tail.IndexOfAny(new[] { '\\', '/' });
        if (boundary > 0 && Len(tail.Substring(boundary)) * 2 >= tailBudget)
            tail = tail.Substring(boundary);
        return head + ell + tail;
    }

    /// <summary>Pads plain text with spaces to exactly <paramref name="width"/> cells (fitting first).</summary>
    public static string Pad(string? s, int width, bool alignRight = false)
    {
        string fitted = Fit(s, width);
        string fill = new string(' ', Math.Max(0, width - Len(fitted)));
        return alignRight ? fill + fitted : fitted + fill;
    }

    private static string TakeCells(string s, int cells)
    {
        var sb = new StringBuilder();
        int used = 0;
        foreach (char c in s)
        {
            int w = new Segment(c.ToString()).CellCount();
            if (used + w > cells) break;
            sb.Append(c);
            used += w;
        }
        return sb.ToString();
    }

    private static string TakeCellsFromEnd(string s, int cells)
    {
        int used = 0;
        int i = s.Length;
        while (i > 0)
        {
            int w = new Segment(s[i - 1].ToString()).CellCount();
            if (used + w > cells) break;
            used += w;
            i--;
        }
        return s.Substring(i);
    }

    // ------------------------------------------------------------------ writers

    public static void Line(string markup) => AnsiConsole.MarkupLine(markup);

    public static void Blank() => AnsiConsole.WriteLine();

    /// <summary>"Title ──────────── right" spanning the canvas.</summary>
    public static void Section(string title, string? right = null)
    {
        string r = right ?? "";
        int fill = Width - Len(title) - Len(r) - (r.Length > 0 ? 3 : 2);
        string rule = new string(Glyphs.Horizontal[0], Math.Max(1, fill));
        string rightPart = r.Length > 0 ? $" {TextStyle.Dim(Markup.Escape(r))}" : "";
        Line($"{TextStyle.Accent(Markup.Escape(title))} [grey]{rule}[/]{rightPart}");
    }

    /// <summary>A bordered label/value card filling the canvas width.</summary>
    public static void Card(string title, IReadOnlyList<CardRow> rows, Color? border = null, string titleStyle = "aqua bold")
    {
        int inner = Width - 4; // two border cells + one cell of padding each side
        int labelWidth = rows.Count == 0 ? 0 : rows.Max(r => Len(r.Label));
        var lines = new List<string>();
        foreach (CardRow row in rows)
        {
            if (row.Label.Length == 0 && row.Value.Length == 0) { lines.Add(""); continue; }
            int valueBudget = inner - labelWidth - 2;
            string? note = row.Note;
            if (note != null && valueBudget - Len(note) - 2 < 12) note = null; // too narrow: drop the note
            if (note != null) valueBudget -= Len(note) + 2;

            string value = row.IsPath ? FitPath(row.Value, valueBudget) : Fit(row.Value, valueBudget);
            string styled = row.Style != null ? $"[{row.Style}]{Markup.Escape(value)}[/]" : Markup.Escape(value);
            string notePart = note != null ? "  " + TextStyle.Dim(Markup.Escape(note)) : "";
            lines.Add($"[grey]{Markup.Escape(Pad(row.Label, labelWidth))}[/]  {styled}{notePart}");
        }

        var panel = new Panel(new Markup(string.Join("\n", lines)))
            .Header($" [{titleStyle}]{Markup.Escape(Fit(title, Width - 6))}[/] ")
            .Border(Glyphs.Box)
            .BorderColor(border ?? TextStyle.FrameColor)
            .Expand();
        AnsiConsole.Write(panel);
    }

    /// <summary>A bordered free-text box (messages, blockers) filling the canvas width.</summary>
    public static void Box(string title, IEnumerable<string> markupLines, Color border, string titleStyle)
    {
        var panel = new Panel(new Markup(string.Join("\n", markupLines)))
            .Header($" [{titleStyle}]{Markup.Escape(Fit(title, Width - 6))}[/] ")
            .Border(Glyphs.Box)
            .BorderColor(border)
            .Expand();
        AnsiConsole.Write(panel);
    }

    /// <summary>Vertical menu: highlighted bar on the selection, optional grey description column.</summary>
    public static void Menu(IReadOnlyList<(string Label, string Description)> items, int selected)
    {
        int labelWidth = items.Max(i => Len(i.Label)) + 2;
        for (int i = 0; i < items.Count; i++)
        {
            string desc = Compact ? "" : items[i].Description;
            if (i == selected)
            {
                string plain = $" {Glyphs.Pointer} {Pad(items[i].Label, labelWidth)}{desc}";
                Line(Highlight(plain));
            }
            else
            {
                string descPart = desc.Length > 0
                    ? TextStyle.Dim(Markup.Escape(Fit(desc, Width - labelWidth - 3)))
                    : "";
                Line($"   {Markup.Escape(Pad(items[i].Label, labelWidth))}{descPart}");
            }
        }
    }

    /// <summary>
    /// Fixed-layout table row: a 3-cell selection gutter, then the columns separated by two
    /// spaces. A column of width 0 is flexible and absorbs whatever the canvas has left, so the
    /// table always spans exactly the canvas width.
    /// </summary>
    public static void TableRow(IReadOnlyList<Col> cols, IReadOnlyList<(string Text, string? Style)> cells, bool selected)
    {
        int[] widths = ResolveWidths(cols);
        if (selected)
        {
            var plain = new StringBuilder($" {Glyphs.Pointer} ");
            for (int i = 0; i < cols.Count; i++)
            {
                if (i > 0) plain.Append("  ");
                plain.Append(CellText(cols[i], widths[i], cells[i].Text));
            }
            Line(Highlight(plain.ToString()));
            return;
        }

        var sb = new StringBuilder("   ");
        for (int i = 0; i < cols.Count; i++)
        {
            if (i > 0) sb.Append("  ");
            string text = Markup.Escape(CellText(cols[i], widths[i], cells[i].Text));
            sb.Append(cells[i].Style != null ? $"[{cells[i].Style}]{text}[/]" : text);
        }
        Line(sb.ToString());
    }

    public static void TableHeader(IReadOnlyList<Col> cols)
    {
        int[] widths = ResolveWidths(cols);
        var sb = new StringBuilder("   ");
        for (int i = 0; i < cols.Count; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(Pad(cols[i].Header, widths[i], cols[i].AlignRight));
        }
        Line(TextStyle.Dim(Markup.Escape(sb.ToString())));
    }

    private static string CellText(Col col, int width, string text) =>
        col.IsPath ? Pad(FitPath(text, width), width, col.AlignRight) : Pad(text, width, col.AlignRight);

    private static int[] ResolveWidths(IReadOnlyList<Col> cols)
    {
        int fixedTotal = cols.Where(c => c.Width > 0).Sum(c => c.Width);
        int gaps = 3 + 2 * (cols.Count - 1);
        int flexCount = cols.Count(c => c.Width == 0);
        int flex = flexCount == 0 ? 0 : Math.Max(4, (Width - fixedTotal - gaps) / flexCount);
        return cols.Select(c => c.Width > 0 ? c.Width : flex).ToArray();
    }

    /// <summary>A full-width selection bar around plain text.</summary>
    public static string Highlight(string plain) => $"[black on aqua]{Markup.Escape(Pad(plain, Width))}[/]";

    /// <summary>Rows a list may use after <paramref name="reservedRows"/> of other body content.</summary>
    public static int ListRows(int reservedRows) => Math.Max(3, BodyHeight - reservedRows);

    /// <summary>"1-12 of 40" for a section header, or null when everything fits.</summary>
    public static string? Range(int top, int shown, int total) =>
        total > shown ? $"{top + 1}-{top + shown} of {total}" : (total > 0 ? $"{total}" : null);

    public static void Error(string message)   => Line($"{TextStyle.ErrorGlyph} [red]{Markup.Escape(Fit(message, Width - 2))}[/]");
    public static void Success(string message) => Line($"{TextStyle.SuccessGlyph} [green]{Markup.Escape(Fit(message, Width - 2))}[/]");
    public static void Note(string message)    => Line(TextStyle.Dim(Markup.Escape(Fit(message, Width))));

    /// <summary>"› Label   [input box]" — one form row with the box sized to the canvas.</summary>
    public static void FormField(string label, LineInput input, bool focused, int labelWidth)
    {
        int boxWidth = Math.Min(80, Width - labelWidth - 6);
        string marker = focused ? TextStyle.SelectionMarker : " ";
        string labelMarkup = focused
            ? TextStyle.Bold(Markup.Escape(Pad(label, labelWidth)))
            : TextStyle.Dim(Markup.Escape(Pad(label, labelWidth)));
        Line($" {marker} {labelMarkup}  {Field(input, boxWidth, focused)}");
    }

    /// <summary>
    /// A single-line input box of <paramref name="width"/> cells. Scrolls horizontally so the
    /// caret stays visible; the caret is drawn as an inverted cell because the real cursor is hidden.
    /// </summary>
    public static string Field(LineInput input, int width, bool focused)
    {
        string text = input.Text;
        if (!focused)
        {
            if (text.Length == 0) return TextStyle.Dim(Pad("(empty)", width));
            return $"[underline]{Markup.Escape(Pad(text, width))}[/]";
        }

        int cursor = Math.Min(input.CursorIndex, text.Length);
        int start = 0;
        while (start < cursor && Len(text.Substring(start, cursor - start)) + 1 > width) start++;

        string before = text.Substring(start, cursor - start);
        string at = cursor < text.Length ? text[cursor].ToString() : " ";
        int remaining = Math.Max(0, width - Len(before) - Len(at));
        string after = cursor + 1 <= text.Length ? TakeCells(text.Substring(Math.Min(text.Length, cursor + 1)), remaining) : "";
        string fill = new string(' ', Math.Max(0, remaining - Len(after)));
        return $"[underline]{Markup.Escape(before)}[/][black on aqua]{Markup.Escape(at)}[/][underline]{Markup.Escape(after + fill)}[/]";
    }
}
