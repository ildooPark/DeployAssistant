namespace DeployAssistant.Model
{
    /// <summary>
    /// Selects how <c>MetaDataManager.RequestCheckoutVersion</c> computes the work list
    /// before applying a version to the working directory.
    /// </summary>
    public enum CheckoutMode
    {
        /// <summary>
        /// Snapshot-to-snapshot diff (metadata only, no disk hashing).
        /// Fast, but trusts that the working directory still matches the current main.
        /// </summary>
        Fast,

        /// <summary>
        /// Full integrity scan of the working directory against the target snapshot.
        /// Slower (hashes every intersecting file) but repairs drift that happened
        /// outside DeployAssistant.
        /// </summary>
        CleanRestore
    }
}
