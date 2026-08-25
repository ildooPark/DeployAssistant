#pragma warning disable CS0618  // ProjectData is a V1 type; the checkout API is V1-shaped.

namespace DeployAssistant.Model
{
    /// <summary>
    /// Outcome of a single <c>MetaDataManager.RequestCheckoutVersion</c> call.
    /// Always delivered through <c>CheckoutCompleteEventHandler</c>, including on
    /// failure — <see cref="Messages"/> then carries the reason so the GUI/CLI never
    /// has to guess why nothing happened.
    /// </summary>
    public sealed class CheckoutResult
    {
        public bool Success { get; }
        /// <summary>Version that was checked out. <c>null</c> when the request was rejected before a target could be resolved.</summary>
        public ProjectData? Target { get; }
        public CheckoutMode Mode { get; }
        /// <summary>Number of file changes handed to the file handler (0 on failure).</summary>
        public int FilesApplied { get; }
        public IReadOnlyList<string> Messages { get; }

        public CheckoutResult(bool success, ProjectData? target, CheckoutMode mode,
            int filesApplied, IReadOnlyList<string>? messages = null)
        {
            Success = success;
            Target = target;
            Mode = mode;
            FilesApplied = filesApplied;
            Messages = messages ?? new List<string>();
        }
    }
}
