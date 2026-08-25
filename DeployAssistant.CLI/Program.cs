using System;
using System.Reflection;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;

namespace DeployAssistant.CLI
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            DeployAssistant.Utils.PathCompat.EnableNetFrameworkLongPaths();

            // Argument handling: --help / --version short-circuit before launching the TUI.
            // Unknown arguments exit 1 (caught by the CI smoke test); the no-args path
            // falls through to App.Run which either renders the TUI in an interactive
            // terminal, or prints a banner and exits 0 when stdout is redirected.
            if (args.Length > 0)
            {
                string first = args[0];
                if (first == "--help" || first == "-h" || first == "help")
                {
                    PrintHelp();
                    return 0;
                }
                if (first == "--version" || first == "-v")
                {
                    PrintVersion();
                    return 0;
                }
                Console.Error.WriteLine($"deployassistant: unknown command '{first}'.");
                Console.Error.WriteLine("Run 'deployassistant --help' for usage.");
                return 1;
            }

            Screen root = BuildRootScreen();
            return new App().Run(root);
        }

        private static Screen BuildRootScreen()
        {
            string? lastPath = ConfigStore.LoadLastOpened();
            if (string.IsNullOrWhiteSpace(lastPath)) return new TopMenuScreen(loaded: false);

            var mgr = ManagerFactory.LoadOrThrow(lastPath!, out _);
            return mgr is null
                ? (Screen)new TopMenuScreen(loaded: false)
                : new MainScreen(mgr);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("DeployAssistant CLI");
            Console.WriteLine("Interactive TUI for binary deployment version control.");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  deployassistant            Launch the interactive TUI");
            Console.WriteLine("  deployassistant --help     Show this help text");
            Console.WriteLine("  deployassistant --version  Show version");
            Console.WriteLine();
            Console.WriteLine("The TUI requires a real terminal (cmd.exe, Windows Terminal, etc).");
            Console.WriteLine("Output redirection / piping is not supported by the TUI.");
        }

        private static void PrintVersion()
        {
            Console.WriteLine(CliVersion.Banner);
        }
    }

    /// <summary>
    /// Single source of truth for the CLI version string. The assembly version comes from
    /// &lt;AssemblyVersion&gt; in the csproj, which tracks the CLI's own semver line — the GUI
    /// versions independently (see docs/release-process.md).
    /// </summary>
    internal static class CliVersion
    {
        /// <summary>Bare semver ("1.1.0"), or "0.0.0" when the assembly carries no version.</summary>
        public static string Display { get; } = Resolve();

        /// <summary>Version prefixed with the product name, as printed by --version.</summary>
        public static string Banner => $"DeployAssistant CLI {Display}";

        private static string Resolve()
        {
            // GetName().Version is always 4-part ("1.1.0.0"); trim the revision so the
            // string matches the semver used for release folders and zip names.
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v is null) return "0.0.0";
            return $"{v.Major}.{v.Minor}.{(v.Build < 0 ? 0 : v.Build)}";
        }
    }
}
