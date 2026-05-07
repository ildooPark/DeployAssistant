using System;
using DeployAssistant.CLI.Engine;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using Spectre.Console;

namespace DeployAssistant.CLI.Screens;

internal sealed class MainScreen : Screen
{
    private readonly MetaDataManager _mgr;
    private int _selected;

    private static readonly string[] MenuItems = { "Integrity check", "List revisions" };

    public MainScreen(MetaDataManager mgr) { _mgr = mgr; }

    public override void Render()
    {
        AnsiConsole.Write(BuildCard());
        AnsiConsole.WriteLine();
        for (int i = 0; i < MenuItems.Length; i++)
        {
            string marker = i == _selected ? TextStyle.SelectionMarker : " ";
            string label = i == _selected ? TextStyle.Accent(MenuItems[i]) : MenuItems[i];
            AnsiConsole.MarkupLine($" {marker} {label}");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(TextStyle.Dim("─────────────────────────────────────"));
        AnsiConsole.MarkupLine(TextStyle.Dim("↑↓ move · enter select · m menu · q quit"));
    }

    public override ScreenAction Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_selected > 0) _selected--;
                return ScreenAction.StayAction;

            case ConsoleKey.DownArrow:
                if (_selected < MenuItems.Length - 1) _selected++;
                return ScreenAction.StayAction;

            case ConsoleKey.Enter:
                return _selected switch
                {
                    0 => new ScreenAction.Push(new IntegrityRunScreen(_mgr)),
                    1 => new ScreenAction.Push(new RevisionListScreen(_mgr)),
                    _ => ScreenAction.StayAction,
                };
        }

        return key.KeyChar switch
        {
            'm' or 'M' => new ScreenAction.Push(new TopMenuScreen(loaded: true)),
            'q' or 'Q' => ScreenAction.ExitAction,
            _ => ScreenAction.StayAction,
        };
    }

    private Panel BuildCard()
    {
        var pd = _mgr.MainProjectData;
        int revCount = _mgr.ProjectMetaData?.ProjectDataList.Count ?? 0;
        string version = pd?.UpdatedVersion ?? "Undefined";
        string body =
            $"{TextStyle.Bold("Path   ")}  {Markup.Escape(pd?.ProjectPath ?? "")}\n" +
            $"{TextStyle.Bold("Version")}  {TextStyle.Accent(Markup.Escape(version))}   {TextStyle.Dim("← main")}\n" +
            $"{TextStyle.Bold("Updated")}  {pd?.UpdatedTime:yyyy-MM-dd HH:mm}  by {Markup.Escape(pd?.UpdaterName ?? "")}\n" +
            $"{TextStyle.Bold("Files  ")}  {pd?.ProjectFiles.Count ?? 0}   {TextStyle.Dim($"({revCount} revision(s))")}";

        return new Panel(body)
            .Header(TextStyle.Accent(Markup.Escape(_mgr.ProjectMetaData?.ProjectName ?? "Unknown")))
            .BorderColor(TextStyle.AccentColor);
    }
}
