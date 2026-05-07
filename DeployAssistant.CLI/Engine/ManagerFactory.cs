using System;
using DeployAssistant.DataComponent;
using DeployAssistant.Services;

namespace DeployAssistant.CLI.Engine
{
    internal static class ManagerFactory
    {
        /// <summary>
        /// Creates a <see cref="MetaDataManager"/> wired to a CLI-appropriate
        /// dialog service. Defaults to auto-yes so manager-side confirmations
        /// during a TUI operation don't block on stdin (the TUI owns the
        /// keyboard).
        /// </summary>
        public static MetaDataManager Create(IDialogService? dialogService = null)
        {
            var dialog = dialogService ?? new ConsoleDialogService(autoYes: true);
            var mgr = new MetaDataManager(dialog);
            mgr.Awake();
            return mgr;
        }

        public static MetaDataManager? LoadOrThrow(string dstPath, out string? error)
        {
            var mgr = Create();
            try
            {
                if (!mgr.RequestProjectRetrieval(dstPath))
                {
                    error = $"Could not load project at {dstPath}.";
                    return null;
                }
                error = null;
                return mgr;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
