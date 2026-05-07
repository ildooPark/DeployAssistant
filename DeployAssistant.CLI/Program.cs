using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;

namespace DeployAssistant.CLI
{
    internal static class Program
    {
        private static int Main(string[] _)
        {
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
    }
}
