using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DeployAssistant.CLI.Engine;

/// <summary>
/// Composes one full-window frame — header with breadcrumb, the screen body, an optional
/// modal, and the key-hint footer — as exactly <c>height</c> lines no wider than the canvas.
/// The body is rendered off-screen at the canvas width, so screens never see the real window
/// size and look the same on every display; only list lengths grow with the window height.
/// </summary>
internal static class FrameRenderer
{
    /// <summary>Console capabilities the off-screen renders must match.</summary>
    internal readonly struct Target
    {
        public Target(bool ansi, ColorSystem colors) { Ansi = ansi; Colors = colors; }
        public bool Ansi { get; }
        public ColorSystem Colors { get; }
    }

    public static int CanvasWidth(int windowWidth) => Math.Min(windowWidth - 1, Ui.MaxWidth);

    public static bool FitsMinimum(int windowWidth, int windowHeight) =>
        windowWidth >= Ui.MinWidth && windowHeight >= Ui.MinHeight;

    /// <param name="stack">Screens bottom-to-top; the last one is rendered, all titles form the breadcrumb.</param>
    /// <param name="bodyless">Draw the chrome only — used while a screen's OnEnter runs a live widget.</param>
    public static string[] Compose(IReadOnlyList<Screen> stack, int windowWidth, int windowHeight, Target target,
                                   IRenderable? modal = null, bool bodyless = false)
    {
        if (!FitsMinimum(windowWidth, windowHeight))
            return TooSmall(windowWidth, windowHeight, target);

        Screen screen = stack[stack.Count - 1];
        int width = CanvasWidth(windowWidth);
        Ui.Width = width;

        List<string> footer = bodyless ? new List<string>() : HintLines(screen.Hints, width);
        int bodyHeight = windowHeight - 3 - Math.Max(1, footer.Count); // header + 2 rules + footer
        Ui.BodyHeight = bodyHeight;

        var lines = new List<string>(windowHeight);
        lines.AddRange(Capture(width, target, () => AnsiConsole.MarkupLine(Header(stack, width))));
        lines.Add(Rule(width, target));

        List<string> body = bodyless ? new List<string>() : Capture(width, target, screen.Render);
        if (modal != null)
        {
            List<string> box = Capture(width, target, () => AnsiConsole.Write(modal));
            int keep = Math.Max(0, bodyHeight - box.Count - 1);
            body = body.Take(keep).ToList();
            while (body.Count < keep) body.Add("");
            body.Add("");
            body.AddRange(box);
        }
        if (body.Count > bodyHeight)
        {
            body = body.Take(bodyHeight - 1).ToList();
            body.AddRange(Capture(width, target, () => AnsiConsole.MarkupLine(TextStyle.Dim($"{Glyphs.More} more below — enlarge the window"))));
        }
        while (body.Count < bodyHeight) body.Add("");
        lines.AddRange(body.Take(bodyHeight));

        lines.Add(Rule(width, target));
        if (footer.Count == 0) footer.Add(TextStyle.Dim(bodyless ? "working..." : ""));
        foreach (string hintLine in footer)
            lines.AddRange(Capture(width, target, () => AnsiConsole.MarkupLine(hintLine)));

        return lines.Take(windowHeight).ToArray();
    }

    private static string Header(IReadOnlyList<Screen> stack, int width)
    {
        string app = "DeployAssistant";
        string version = $"CLI {CliVersion.Display}";
        string sep = $" {Glyphs.Crumb} ";

        var crumbs = stack.Select(s => s.Title).Where(t => !string.IsNullOrEmpty(t)).ToList();
        int budget = width - Ui.Len(app) - 1 - Ui.Len(version);
        // Drop the oldest crumbs first; the current screen's title is the one that matters.
        while (crumbs.Count > 1 && Ui.Len(sep + string.Join(sep, crumbs)) > budget)
            crumbs.RemoveAt(0);
        string trail = crumbs.Count > 0 ? Ui.Fit(sep + string.Join(sep, crumbs), budget) : "";

        return $"[aqua bold]{app}[/] [grey]{Markup.Escape(version)}[/]{Markup.Escape(trail)}";
    }

    private static string Rule(int width, Target target)
    {
        string rule = new string(Glyphs.Horizontal[0], width);
        return Capture(width, target, () => AnsiConsole.MarkupLine($"[grey]{rule}[/]"))[0];
    }

    /// <summary>Key chips packed into at most two lines; hints that still do not fit are dropped from the tail.</summary>
    internal static List<string> HintLines(IReadOnlyList<KeyHint> hints, int width)
    {
        var lines = new List<string>();
        string current = "";
        int used = 0;
        foreach (KeyHint hint in hints)
        {
            int cost = Ui.Len(hint.Keys) + 2 + 1 + Ui.Len(hint.Label) + 2;
            if (used + cost > width && used > 0)
            {
                lines.Add(current);
                if (lines.Count == 2) return lines;
                current = "";
                used = 0;
            }
            current += $"[black on grey] {Markup.Escape(hint.Keys)} [/] {Markup.Escape(hint.Label)}  ";
            used += cost;
        }
        if (used > 0) lines.Add(current);
        return lines;
    }

    private static string[] TooSmall(int windowWidth, int windowHeight, Target target)
    {
        int width = Math.Max(1, windowWidth - 1);
        int height = Math.Max(1, windowHeight);
        string message = $"Window too small: {windowWidth}x{windowHeight} (need {Ui.MinWidth}x{Ui.MinHeight}). Please enlarge it.";
        var lines = Capture(width, target, () => AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]"));
        while (lines.Count < height) lines.Add("");
        return lines.Take(height).ToArray();
    }

    /// <summary>
    /// Runs <paramref name="render"/> against a throwaway console of the canvas width and returns
    /// its output split into lines. Screens write through the static AnsiConsole, so it is swapped
    /// for the duration of the call.
    /// </summary>
    private static List<string> Capture(int width, Target target, Action render)
    {
        var writer = new StringWriter();
        var offscreen = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = target.Ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = (ColorSystemSupport)(int)target.Colors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        offscreen.Profile.Width = width;
        offscreen.Profile.Capabilities.Unicode = Term.Unicode;

        IAnsiConsole original = AnsiConsole.Console;
        AnsiConsole.Console = offscreen;
        try { render(); }
        finally { AnsiConsole.Console = original; }

        string text = writer.ToString().Replace("\r\n", "\n");
        if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
        if (text.Length == 0) return new List<string>();
        return text.Split('\n').ToList();
    }
}
