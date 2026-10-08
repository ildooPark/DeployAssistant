using DeployAssistant.DataComponent;
using Spectre.Console;

namespace DeployAssistant.CLI.Engine;

/// <summary>
/// Markup helpers and the glyph set. Colors stay inside the 16-color palette and "dim" maps
/// to grey rather than the SGR faint attribute, which legacy conhost ignores — so a screen
/// reads the same in cmd.exe, Windows Terminal and VS Code.
/// </summary>
internal static class TextStyle
{
    public static readonly Color AccentColor = Color.Aqua;
    public static readonly Color FrameColor = Color.Grey;

    public static string Added(string s)    => $"[green]{s}[/]";
    public static string Removed(string s)  => $"[red]{s}[/]";
    public static string Modified(string s) => $"[yellow]{s}[/]";
    public static string Restored(string s) => $"[fuchsia]{s}[/]";
    public static string Accent(string s)   => $"[aqua bold]{s}[/]";
    public static string Dim(string s)      => $"[grey]{s}[/]";
    public static string Bold(string s)     => $"[bold]{s}[/]";

    /// <summary>Selection pointer used as the per-row marker in menus and lists.</summary>
    public static string SelectionMarker => Accent(Glyphs.Pointer);

    /// <summary>Marker for the revision that is currently "main".</summary>
    public static string MainMarker => Accent(Glyphs.Main);

    /// <summary>Success glyph used at the start of "all OK" lines.</summary>
    public static string SuccessGlyph => Added(Glyphs.Check);

    /// <summary>Error glyph used at the start of failure lines.</summary>
    public static string ErrorGlyph => Removed(Glyphs.Cross);

    /// <summary>Short fixed-width badge for a file state, e.g. "MOD", plus its color.</summary>
    public static (string Label, string Color) StateBadge(DataState state)
    {
        if ((state & DataState.Added) != 0)    return ("ADD", "green");
        if ((state & DataState.Deleted) != 0)  return ("DEL", "red");
        if ((state & DataState.Modified) != 0) return ("MOD", "yellow");
        if ((state & DataState.Restored) != 0) return ("RST", "fuchsia");
        return ("---", "grey");
    }

    /// <summary>One changed-file row (badge + escaped path) for plain inline use.</summary>
    public static string FormatFileState(DataState state, string relPath)
    {
        var (label, color) = StateBadge(state);
        return $"[{color}]{label}[/]  [{color}]{Markup.Escape(relPath)}[/]";
    }
}

/// <summary>
/// The two glyph sets. Every symbol in the ASCII set is a single narrow cell on any console
/// font, which is what keeps columns aligned on legacy CJK consoles.
/// </summary>
internal static class Glyphs
{
    private static bool U => Term.Unicode;

    public static string Pointer   => U ? "›" : ">";
    public static string Main      => U ? "●" : "*";
    public static string Check     => U ? "✓" : "+";
    public static string Cross     => U ? "✗" : "x";
    public static string Warn      => "!";
    public static string Arrow     => U ? "→" : "->";
    public static string Ellipsis  => U ? "…" : "...";
    public static string Dot       => U ? "·" : "|";
    public static string Crumb     => U ? "›" : ">";
    public static string Horizontal => U ? "─" : "-";
    public static string UpDown    => U ? "↑↓" : "Up/Dn";
    public static string More      => U ? "▾" : "v";
    public static string Less      => U ? "▴" : "^";

    public static BoxBorder Box => U ? BoxBorder.Rounded : BoxBorder.Ascii;
}
