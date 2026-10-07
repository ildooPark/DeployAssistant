using System;
using System.IO;
using System.Text;
using Spectre.Console;

namespace DeployAssistant.CLI.Engine;

/// <summary>
/// Terminal capabilities and session state. Decides once, at startup, whether the box and
/// symbol glyphs can be Unicode, so every PC draws the same frame instead of whatever its
/// console font happens to do with them.
/// </summary>
internal static class Term
{
    /// <summary>
    /// Unicode box/symbol glyphs vs. a pure-ASCII set. Defaults to true so code that renders
    /// outside <see cref="Enter"/> (tests) gets the full glyph set.
    /// </summary>
    public static bool Unicode { get; internal set; } = true;

    /// <summary>True when the console understands VT escape sequences (Win10 1511+ conhost, Windows Terminal).</summary>
    public static bool Ansi { get; private set; }

    /// <summary>
    /// Legacy conhost in a CJK code page draws East-Asian "ambiguous width" glyphs (─ │ › ✓ ·)
    /// two cells wide, which shears every border; Windows Terminal, VS Code and ConEmu do not.
    /// DA_CLI_GLYPHS=unicode|ascii overrides the guess.
    /// </summary>
    internal static bool DetectUnicode(Func<string, string?> env, int outputCodePage)
    {
        string? forced = env("DA_CLI_GLYPHS");
        if (string.Equals(forced, "unicode", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(forced, "ascii", StringComparison.OrdinalIgnoreCase)) return false;

        bool modernHost = !string.IsNullOrEmpty(env("WT_SESSION"))
                          || !string.IsNullOrEmpty(env("TERM_PROGRAM"))
                          || string.Equals(env("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase);
        if (modernHost) return true;

        bool cjkCodePage = outputCodePage is 932 or 936 or 949 or 950;
        return !cjkCodePage;
    }

    /// <summary>
    /// Switches the console into TUI mode: glyph set, UTF-8 output when Unicode is in use,
    /// the alternate screen buffer and a hidden cursor. Dispose restores all of it, so the
    /// user's scrollback looks exactly as it did before launch.
    /// </summary>
    public static IDisposable Enter()
    {
        Encoding originalEncoding = Console.OutputEncoding;
        Unicode = DetectUnicode(Environment.GetEnvironmentVariable, originalEncoding.CodePage);
        if (Unicode && originalEncoding.CodePage != 65001)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (IOException) { Unicode = false; }
        }

        // Console.Out is replaced when OutputEncoding changes; rebuild the Spectre console
        // so it writes through the new writer, then pin the capabilities we decided on.
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Out),
        });
        AnsiConsole.Profile.Capabilities.Unicode = Unicode;
        Ansi = AnsiConsole.Profile.Capabilities.Ansi;

        if (Ansi) Write("\x1b[?1049h\x1b[?25l");
        return new Session(originalEncoding);
    }

    public static (int Width, int Height) Size()
    {
        try { return (Console.WindowWidth, Console.WindowHeight); }
        catch (IOException) { return (0, 0); }
    }

    /// <summary>
    /// Paints a whole frame in one write: cursor home, each line followed by erase-to-EOL,
    /// then erase-below. Nothing is cleared first, so there is no blank flash between frames.
    /// The last line gets no newline, otherwise a full-height frame would scroll by one.
    /// </summary>
    public static void WriteFrame(string[] lines)
    {
        var sb = new StringBuilder(lines.Length * 96);
        if (Ansi)
        {
            sb.Append("\x1b[H");
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(lines[i]).Append("\x1b[0m\x1b[K");
                if (i < lines.Length - 1) sb.Append("\r\n");
            }
            sb.Append("\x1b[J");
        }
        else
        {
            Console.Clear();
            sb.Append(string.Join(Environment.NewLine, lines));
        }
        Write(sb.ToString());
    }

    /// <summary>Moves the cursor (0-based) so a live widget (progress, spinner) draws inside the body.</summary>
    public static void MoveTo(int row, int column)
    {
        if (Ansi) Write($"\x1b[{row + 1};{column + 1}H");
    }

    private static void Write(string s)
    {
        Console.Out.Write(s);
        Console.Out.Flush();
    }

    private sealed class Session : IDisposable
    {
        private readonly Encoding _originalEncoding;
        private bool _disposed;

        public Session(Encoding originalEncoding) { _originalEncoding = originalEncoding; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Ansi) Write("\x1b[0m\x1b[?25h\x1b[?1049l");
            if (Console.OutputEncoding.CodePage != _originalEncoding.CodePage)
            {
                try { Console.OutputEncoding = _originalEncoding; }
                catch (IOException) { }
            }
        }
    }
}
