using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using Spectre.Console;

namespace DeployAssistant.CLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var dialog = new ConsoleDialogService(autoYes: args.Contains("--yes"));

            if (args.Length == 0)
            {
                ShowHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string[] rest = args.Skip(1).ToArray();

            return command switch
            {
                "init"             => RunInit(rest, dialog),
                "load"             => RunLoad(rest, dialog),
                "scan"             => RunScan(rest, dialog),
                "stage"            => RunStage(rest, dialog),
                "deploy"           => RunDeploy(rest, dialog),
                "revert"           => RunRevert(rest, dialog),
                "export"           => RunExport(rest, dialog),
                "list"             => RunList(rest, dialog),
                "integrity-check"  => RunIntegrityCheck(rest, dialog),
                "--help" or "help" => Help(),
                _ => UnknownCommand(command)
            };
        }

        // ------------------------------------------------------------------ //
        //  Commands                                                           //
        // ------------------------------------------------------------------ //

        private static int RunInit(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 1) return UsageError("init <path>");
            string path = args[0];

            var mgr = CreateManager(dialog);
            using var done = new ManualResetEventSlim(false);
            mgr.ManagerStateEventHandler += state => { if (state == MetaDataState.Idle) done.Set(); };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start($"[cyan]Initializing project at:[/] {Markup.Escape(path)}", _ =>
                {
                    mgr.RequestProjectInitialization(path);
                    done.Wait(TimeSpan.FromMinutes(5));
                });

            AnsiConsole.MarkupLine("[green]✓[/] Initialization complete.");
            return 0;
        }

        private static int RunLoad(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 1) return UsageError("load <path>");
            string path = args[0];

            var mgr = CreateManager(dialog);

            bool ok = mgr.RequestProjectRetrieval(path);
            if (!ok)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to load project from: [dim]{Markup.Escape(path)}[/]");
                return 1;
            }

            PrintProjectCard(mgr);
            return 0;
        }

        private static int RunScan(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 2) return UsageError("scan <dst-path> <src-path>");
            string dstPath = args[0];
            string srcPath = args[1];

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            List<ProjectFile> changedFiles = [];

            mgr.FileChangesEventHandler += files =>
            {
                changedFiles.Clear();
                changedFiles.AddRange(files);
                done.Set();
            };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start($"[cyan]Scanning source:[/] {Markup.Escape(srcPath)}", _ =>
                {
                    mgr.RequestSrcDataRetrieval(srcPath);
                    done.Wait(TimeSpan.FromMinutes(2));
                });

            if (changedFiles.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✓[/] No changes detected.");
                return 0;
            }

            AnsiConsole.MarkupLine($"[cyan]ℹ[/]  Found [bold]{changedFiles.Count}[/] pre-staged file(s):");
            foreach (var f in changedFiles)
                AnsiConsole.MarkupLine(FormatFileState(f.DataState, f.DataRelPath));
            return 0;
        }

        private static int RunStage(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 1) return UsageError("stage <dst-path>");
            string dstPath = args[0];

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            List<ProjectFile> stagedFiles = [];
            mgr.FileChangesEventHandler += files =>
            {
                stagedFiles.Clear();
                stagedFiles.AddRange(files);
            };
            mgr.ManagerStateEventHandler += state => { if (state == MetaDataState.Idle) done.Set(); };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start("[cyan]Staging file changes…[/]", _ =>
                {
                    mgr.RequestStageChanges();
                    done.Wait(TimeSpan.FromMinutes(2));
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Staging complete. [bold]{stagedFiles.Count}[/] file(s) staged.");
            foreach (var f in stagedFiles)
                AnsiConsole.MarkupLine(FormatFileState(f.DataState, f.DataRelPath));
            return 0;
        }

        private static int RunDeploy(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 1) return UsageError("deploy <dst-path> [--updater <name>] [--log <message>]");
            string dstPath = args[0];
            string updater = ParseFlag(args, "--updater") ?? Environment.UserName;
            string log     = ParseFlag(args, "--log")     ?? "CLI deploy";

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            string? newVersion = null;
            mgr.ProjLoadedEventHandler += obj => { if (obj is ProjectData pd) newVersion = pd.UpdatedVersion; };
            mgr.ManagerStateEventHandler += state => { if (state == MetaDataState.Idle) done.Set(); };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start($"[cyan]Deploying to:[/] {Markup.Escape(dstPath)}", _ =>
                {
                    mgr.RequestProjectUpdate(updater, log, dstPath);
                    done.Wait(TimeSpan.FromMinutes(10));
                });

            string versionStr = newVersion != null
                ? $" [cyan bold]→ {Markup.Escape(newVersion)}[/]"
                : string.Empty;
            AnsiConsole.MarkupLine($"[green]✓[/] Deploy complete.{versionStr}");
            return 0;
        }

        private static int RunRevert(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 2) return UsageError("revert <dst-path> <version>");
            string dstPath  = args[0];
            string version  = args[1];

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            ProjectData? target = mgr.ProjectMetaData?.ProjectDataList
                .FirstOrDefault(p => p.UpdatedVersion == version);
            if (target == null)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Version not found: [bold]{Markup.Escape(version)}[/]");
                return 1;
            }

            using var done = new ManualResetEventSlim(false);
            mgr.ManagerStateEventHandler += state => { if (state == MetaDataState.Idle) done.Set(); };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start($"[cyan]Reverting to:[/] {Markup.Escape(version)}", _ =>
                {
                    mgr.RequestRevertProject(target);
                    done.Wait(TimeSpan.FromMinutes(10));
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Reverted [cyan bold]→ {Markup.Escape(version)}[/]");
            return 0;
        }

        private static int RunExport(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 2) return UsageError("export <dst-path> <version>");
            string dstPath = args[0];
            string version = args[1];

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            ProjectData? target = mgr.ProjectMetaData?.ProjectDataList
                .FirstOrDefault(p => p.UpdatedVersion == version);
            if (target == null)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Version not found: [bold]{Markup.Escape(version)}[/]");
                return 1;
            }

            using var done = new ManualResetEventSlim(false);
            string? exportPath = null;
            mgr.ProjExportEventHandler += path =>
            {
                exportPath = path;
                done.Set();
            };
            mgr.ManagerStateEventHandler += state => { if (state == MetaDataState.Idle) done.Set(); };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start($"[cyan]Exporting version:[/] {Markup.Escape(version)}", _ =>
                {
                    mgr.RequestExportProjectBackup(target);
                    done.Wait(TimeSpan.FromMinutes(10));
                });

            if (exportPath != null)
                AnsiConsole.MarkupLine($"[green]✓[/] Exported to: [dim]{Markup.Escape(exportPath)}[/]");
            else
                AnsiConsole.MarkupLine("[green]✓[/] Export complete.");
            return 0;
        }

        private static int RunList(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 1) return UsageError("list <dst-path>");
            string dstPath = args[0];

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            var list = mgr.ProjectMetaData?.ProjectDataList;
            if (list == null || list.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]⚠[/]  No revisions found.");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[cyan bold]{Markup.Escape(mgr.ProjectMetaData?.ProjectName ?? "")}[/]")
                .AddColumn(new TableColumn(" ").Centered())
                .AddColumn(new TableColumn("[dim]Rev[/]").RightAligned())
                .AddColumn("[dim]Version[/]")
                .AddColumn("[dim]Date[/]")
                .AddColumn("[dim]By[/]")
                .AddColumn(new TableColumn("[dim]Changes[/]").RightAligned());

            int idx = 1;
            foreach (ProjectData pd in list)
            {
                bool isCurrent = pd.Equals(mgr.MainProjectData);
                string marker      = isCurrent ? "[cyan bold]→[/]" : " ";
                string versionText = isCurrent
                    ? $"[cyan bold]{Markup.Escape(pd.UpdatedVersion ?? "")}[/]"
                    : Markup.Escape(pd.UpdatedVersion ?? "");
                table.AddRow(
                    marker,
                    $"[dim]#{idx}[/]",
                    versionText,
                    pd.UpdatedTime.ToString("yyyy-MM-dd HH:mm"),
                    Markup.Escape(pd.UpdaterName ?? ""),
                    $"{pd.NumberOfChanges}");
                idx++;
            }

            AnsiConsole.Write(table);
            return 0;
        }

        private static int RunIntegrityCheck(string[] args, ConsoleDialogService dialog)
        {
            if (args.Length < 1) return UsageError("integrity-check <dst-path>");
            string dstPath = args[0];

            var mgr = LoadOrFail(dstPath, dialog);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            List<ProjectFile>? changedFiles = null;
            mgr.IntegrityCheckCompleteEventHandler += (_, files) =>
            {
                changedFiles = [..files];
                done.Set();
            };
            mgr.ManagerStateEventHandler += state => { if (state == MetaDataState.Idle) done.Set(); };

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start("[cyan]Running integrity check…[/]", _ =>
                {
                    mgr.RequestProjectIntegrityCheck();
                    done.Wait(TimeSpan.FromMinutes(10));
                });

            if (changedFiles == null || changedFiles.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✓[/] All files match — no deviations detected.");
                return 0;
            }

            AnsiConsole.MarkupLine($"[yellow]⚠[/]  [bold]{changedFiles.Count}[/] deviation(s) found:");
            foreach (var f in changedFiles)
                AnsiConsole.MarkupLine(FormatFileState(f.DataState, f.DataRelPath));
            return 0;
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        private static MetaDataManager CreateManager(IDialogService? dialogService = null)
        {
            var mgr = new MetaDataManager(dialogService ?? new NullDialogService());
            mgr.Awake();
            return mgr;
        }

        private static MetaDataManager? LoadOrFail(string dstPath, IDialogService? dialogService = null)
        {
            var mgr = CreateManager(dialogService);

            if (!mgr.RequestProjectRetrieval(dstPath))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Could not load project from: [dim]{Markup.Escape(dstPath)}[/]");
                AnsiConsole.MarkupLine("[dim]  Run 'deployassistant init <path>' first.[/]");
                return null;
            }
            return mgr;
        }

        private static void PrintProjectCard(MetaDataManager mgr)
        {
            var pd = mgr.MainProjectData;
            int revCount = mgr.ProjectMetaData?.ProjectDataList.Count ?? 0;
            var panel = new Panel(
                $"[bold]Path   [/]  {Markup.Escape(pd?.ProjectPath ?? "")}\n" +
                $"[bold]Version[/]  [cyan bold]{Markup.Escape(pd?.UpdatedVersion ?? "Undefined")}[/]\n" +
                $"[bold]Updated[/]  {pd?.UpdatedTime:yyyy-MM-dd HH:mm}  by {Markup.Escape(pd?.UpdaterName ?? "")}\n" +
                $"[bold]Files  [/]  {pd?.ProjectFiles.Count ?? 0}   [dim]({revCount} revision(s))[/]")
                .Header($"[cyan bold]{Markup.Escape(mgr.ProjectMetaData?.ProjectName ?? "Unknown")}[/]")
                .BorderColor(Color.Cyan1);
            AnsiConsole.Write(panel);
        }

        /// <summary>Returns a markup line describing a single file's change state.</summary>
        private static string FormatFileState(DataState state, string relPath)
        {
            string escaped = Markup.Escape(relPath);
            if ((state & DataState.Added) != 0)
                return $"  [green]+[/] [green]{escaped}[/]  [dim](added)[/]";
            if ((state & DataState.Deleted) != 0)
                return $"  [red]-[/] [red]{escaped}[/]  [dim](deleted)[/]";
            if ((state & DataState.Modified) != 0)
                return $"  [yellow]~[/] [yellow]{escaped}[/]  [dim](modified)[/]";
            if ((state & DataState.Restored) != 0)
                return $"  [magenta]*[/] [magenta]{escaped}[/]  [dim](restored)[/]";
            return $"  [dim]{escaped}  ({state})[/]";
        }

        private static string? ParseFlag(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        private static int Help()
        {
            ShowHelp();
            return 0;
        }

        private static int UsageError(string usage)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] [bold]Usage:[/] deployassistant {Markup.Escape(usage)}");
            return 1;
        }

        private static int UnknownCommand(string cmd)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown command: [bold]{Markup.Escape(cmd)}[/]");
            AnsiConsole.WriteLine();
            ShowHelp();
            return 1;
        }

        private static void ShowHelp()
        {
            AnsiConsole.MarkupLine("[cyan bold]DeployAssistant CLI[/]");
            AnsiConsole.MarkupLine("[dim]Usage: deployassistant <command> [[options]][/]");
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("Command").Width(46))
                .AddColumn("Description");

            table.AddRow(
                "[bold]init[/] [dim]<path>[/]",
                "Initialise a new project at the given directory.");
            table.AddRow(
                "[bold]load[/] [dim]<path>[/]",
                "Load and display the project at the given directory.");
            table.AddRow(
                "[bold]scan[/] [dim]<dst-path> <src-path>[/]",
                "Scan a source directory and list detected changes.");
            table.AddRow(
                "[bold]stage[/] [dim]<dst-path>[/]",
                "Hash and promote pre-staged changes to staged state.");
            table.AddRow(
                "[bold]deploy[/] [dim]<dst-path> [[--updater N]] [[--log M]][/]",
                "Apply staged changes and record a new revision.");
            table.AddRow(
                "[bold]revert[/] [dim]<dst-path> <version>[/]",
                "Revert the project to the specified revision.");
            table.AddRow(
                "[bold]export[/] [dim]<dst-path> <version>[/]",
                "Export the specified revision as a zipped snapshot.");
            table.AddRow(
                "[bold]list[/] [dim]<dst-path>[/]",
                $"List all recorded revisions ([cyan]→[/] = current).");
            table.AddRow(
                "[bold]integrity-check[/] [dim]<dst-path>[/]",
                "Compare the recorded state against files on disk.");
            table.AddRow(
                "[bold]help[/] [dim]/ --help[/]",
                "Show this help text.");

            AnsiConsole.Write(table);
        }
    }
}
