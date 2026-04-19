using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;

namespace DeployAssistant.CLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string[] rest = args.Skip(1).ToArray();

            return command switch
            {
                "init"             => RunInit(rest),
                "load"             => RunLoad(rest),
                "scan"             => RunScan(rest),
                "stage"            => RunStage(rest),
                "deploy"           => RunDeploy(rest),
                "revert"           => RunRevert(rest),
                "export"           => RunExport(rest),
                "list"             => RunList(rest),
                "integrity-check"  => RunIntegrityCheck(rest),
                "--help" or "help" => Help(),
                _ => UnknownCommand(command)
            };
        }

        // ------------------------------------------------------------------ //
        //  Commands                                                           //
        // ------------------------------------------------------------------ //

        private static int RunInit(string[] args)
        {
            if (args.Length < 1) return UsageError("init <path>");
            string path = args[0];

            var mgr = CreateManager();
            using var done = new ManualResetEventSlim(false);

            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Idle) done.Set();
            };
            mgr.ConfirmationCallback = (_, _) => true;

            Console.WriteLine($"Initializing project at: {path}");
            mgr.RequestProjectInitialization(path);

            done.Wait(TimeSpan.FromMinutes(5));
            Console.WriteLine("Initialization complete.");
            return 0;
        }

        private static int RunLoad(string[] args)
        {
            if (args.Length < 1) return UsageError("load <path>");
            string path = args[0];

            var mgr = CreateManager();
            mgr.ConfirmationCallback = (_, _) => false;

            bool ok = mgr.RequestProjectRetrieval(path);
            if (!ok)
            {
                Console.Error.WriteLine($"Failed to load project from: {path}");
                return 1;
            }

            Console.WriteLine($"Loaded: {mgr.ProjectMetaData?.ProjectName} " +
                              $"— {mgr.ProjectMetaData?.ProjectDataList.Count} revision(s)");
            PrintProjectMain(mgr.MainProjectData);
            return 0;
        }

        private static int RunScan(string[] args)
        {
            if (args.Length < 2) return UsageError("scan <dst-path> <src-path>");
            string dstPath = args[0];
            string srcPath = args[1];

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            var changedFiles = new List<DeployAssistant.Model.ProjectFile>();

            mgr.FileChangesEventHandler += files =>
            {
                changedFiles.Clear();
                changedFiles.AddRange(files);
                done.Set();
            };

            Console.WriteLine($"Scanning source: {srcPath}");
            mgr.RequestSrcDataRetrieval(srcPath);

            done.Wait(TimeSpan.FromMinutes(2));
            Console.WriteLine($"Found {changedFiles.Count} pre-staged file(s):");
            foreach (var f in changedFiles)
                Console.WriteLine($"  [{f.DataState}] {f.DataRelPath}");
            return 0;
        }

        private static int RunStage(string[] args)
        {
            if (args.Length < 1) return UsageError("stage <dst-path>");
            string dstPath = args[0];

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Idle) done.Set();
            };

            Console.WriteLine("Staging file changes...");
            mgr.RequestStageChanges();

            done.Wait(TimeSpan.FromMinutes(2));
            Console.WriteLine("Staging complete.");
            return 0;
        }

        private static int RunDeploy(string[] args)
        {
            // deploy <dst-path> [--updater <name>] [--log <message>]
            if (args.Length < 1) return UsageError("deploy <dst-path> [--updater <name>] [--log <message>]");
            string dstPath = args[0];
            string updater = ParseFlag(args, "--updater") ?? Environment.UserName;
            string log     = ParseFlag(args, "--log")     ?? "CLI deploy";

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Idle) done.Set();
            };
            mgr.ConfirmationCallback = (_, _) => true;

            Console.WriteLine($"Deploying to: {dstPath}");
            mgr.RequestProjectUpdate(updater, log, dstPath);

            done.Wait(TimeSpan.FromMinutes(10));
            Console.WriteLine("Deploy complete.");
            return 0;
        }

        private static int RunRevert(string[] args)
        {
            // revert <dst-path> <version>
            if (args.Length < 2) return UsageError("revert <dst-path> <version>");
            string dstPath  = args[0];
            string version  = args[1];

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            ProjectData? target = mgr.ProjectMetaData?.ProjectDataList
                .FirstOrDefault(p => p.UpdatedVersion == version);
            if (target == null)
            {
                Console.Error.WriteLine($"Version not found: {version}");
                return 1;
            }

            using var done = new ManualResetEventSlim(false);
            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Idle) done.Set();
            };
            mgr.ConfirmationCallback = (_, _) => true;

            Console.WriteLine($"Reverting to: {version}");
            mgr.RequestRevertProject(target);

            done.Wait(TimeSpan.FromMinutes(10));
            Console.WriteLine("Revert complete.");
            return 0;
        }

        private static int RunExport(string[] args)
        {
            // export <dst-path> <version>
            if (args.Length < 2) return UsageError("export <dst-path> <version>");
            string dstPath = args[0];
            string version = args[1];

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            ProjectData? target = mgr.ProjectMetaData?.ProjectDataList
                .FirstOrDefault(p => p.UpdatedVersion == version);
            if (target == null)
            {
                Console.Error.WriteLine($"Version not found: {version}");
                return 1;
            }

            using var done = new ManualResetEventSlim(false);
            mgr.ProjExportEventHandler += exportPath =>
            {
                Console.WriteLine($"Exported to: {exportPath}");
                done.Set();
            };
            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Idle) done.Set();
            };
            mgr.ConfirmationCallback = (_, _) => true;

            Console.WriteLine($"Exporting version: {version}");
            mgr.RequestExportProjectBackup(target);

            done.Wait(TimeSpan.FromMinutes(10));
            return 0;
        }

        private static int RunList(string[] args)
        {
            if (args.Length < 1) return UsageError("list <dst-path>");
            string dstPath = args[0];

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            var list = mgr.ProjectMetaData?.ProjectDataList;
            if (list == null || list.Count == 0)
            {
                Console.WriteLine("No revisions found.");
                return 0;
            }

            Console.WriteLine($"Revisions for '{mgr.ProjectMetaData?.ProjectName}':");
            int idx = 1;
            foreach (ProjectData pd in list)
            {
                string marker = pd.Equals(mgr.MainProjectData) ? " *" : "  ";
                Console.WriteLine($"{marker} {idx,3}. [{pd.UpdatedVersion}]  {pd.UpdatedTime:yyyy-MM-dd HH:mm}  by {pd.UpdaterName}  ({pd.NumberOfChanges} changes)");
                idx++;
            }
            return 0;
        }

        private static int RunIntegrityCheck(string[] args)
        {
            if (args.Length < 1) return UsageError("integrity-check <dst-path>");
            string dstPath = args[0];

            var mgr = LoadOrFail(dstPath);
            if (mgr == null) return 1;

            using var done = new ManualResetEventSlim(false);
            mgr.IntegrityCheckCompleteEventHandler += (log, files) =>
            {
                Console.WriteLine(log);
                Console.WriteLine($"Changed files detected: {files.Count}");
                foreach (var f in files)
                    Console.WriteLine($"  [{f.DataState}] {f.DataRelPath}");
                done.Set();
            };
            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Idle) done.Set();
            };

            Console.WriteLine("Running integrity check...");
            mgr.RequestProjectIntegrityCheck();

            done.Wait(TimeSpan.FromMinutes(10));
            return 0;
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        private static MetaDataManager CreateManager()
        {
            var mgr = new MetaDataManager();
            mgr.Awake();
            return mgr;
        }

        private static MetaDataManager? LoadOrFail(string dstPath)
        {
            var mgr = CreateManager();
            mgr.ConfirmationCallback = (_, _) => false;

            if (!mgr.RequestProjectRetrieval(dstPath))
            {
                Console.Error.WriteLine($"Could not load project from: {dstPath}");
                Console.Error.WriteLine("Run 'deployassistant init <path>' first.");
                return null;
            }
            return mgr;
        }

        private static void PrintProjectMain(ProjectData? pd)
        {
            if (pd == null) return;
            Console.WriteLine($"  Version : {pd.UpdatedVersion}");
            Console.WriteLine($"  Updated : {pd.UpdatedTime:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"  By      : {pd.UpdaterName}");
            Console.WriteLine($"  Files   : {pd.ProjectFiles.Count}");
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
            Console.Error.WriteLine($"Usage: deployassistant {usage}");
            return 1;
        }

        private static int UnknownCommand(string cmd)
        {
            Console.Error.WriteLine($"Unknown command: {cmd}");
            ShowHelp();
            return 1;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("""
                DeployAssistant CLI

                Usage: deployassistant <command> [options]

                Commands:
                  init <path>
                      Initialise a new project at the given directory.

                  load <path>
                      Load and display the project at the given directory.

                  scan <dst-path> <src-path>
                      Scan a source directory against the loaded destination project
                      and list detected file changes.

                  stage <dst-path>
                      Hash and promote pre-staged changes to the staged state.

                  deploy <dst-path> [--updater <name>] [--log <message>]
                      Apply staged changes to the project and record a new revision.

                  revert <dst-path> <version>
                      Revert the project to the specified revision version string.

                  export <dst-path> <version>
                      Export the specified revision as a zipped snapshot.

                  list <dst-path>
                      List all recorded revisions (* = current).

                  integrity-check <dst-path>
                      Compare the recorded project state against the files on disk.

                  help / --help
                      Show this help text.
                """);
        }
    }
}
