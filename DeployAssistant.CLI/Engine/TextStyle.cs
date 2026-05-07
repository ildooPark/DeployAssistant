using DeployAssistant.DataComponent;
using Spectre.Console;

namespace DeployAssistant.CLI.Engine;

internal static class TextStyle
{
    public static readonly Color AccentColor = Color.Cyan1;

    public static string Added(string s)    => $"[green]{s}[/]";
    public static string Removed(string s)  => $"[red]{s}[/]";
    public static string Modified(string s) => $"[yellow]{s}[/]";
    public static string Restored(string s) => $"[magenta]{s}[/]";
    public static string Accent(string s)   => $"[cyan bold]{s}[/]";
    public static string Dim(string s)      => $"[dim]{s}[/]";
    public static string Bold(string s)     => $"[bold]{s}[/]";

    /// <summary>Selection chevron (›) used as the per-row pointer in menus and lists.</summary>
    public static string SelectionMarker => Accent("›");

    /// <summary>Active-row arrow (→) used in the revision list to point to the current "main".</summary>
    public static string MainMarker => Accent("→");

    /// <summary>Success glyph (✓) used at the start of "all OK" lines.</summary>
    public static string SuccessGlyph => Added("✓");

    /// <summary>Error glyph (✗) used at the start of failure lines.</summary>
    public static string ErrorGlyph => Removed("✗");

    public static string FormatFileState(DataState state, string relPath)
    {
        string escaped = Markup.Escape(relPath);
        if ((state & DataState.Added) != 0)
            return $"  {Added("+")} {Added(escaped)}  {Dim("(added)")}";
        if ((state & DataState.Deleted) != 0)
            return $"  {Removed("-")} {Removed(escaped)}  {Dim("(deleted)")}";
        if ((state & DataState.Modified) != 0)
            return $"  {Modified("~")} {Modified(escaped)}  {Dim("(modified)")}";
        if ((state & DataState.Restored) != 0)
            return $"  {Restored("*")} {Restored(escaped)}  {Dim("(restored)")}";
        return $"  {Dim($"{escaped}  ({state})")}";
    }
}
