#pragma warning disable CS0618
using DeployAssistant.DataComponent;
using DeployAssistant.Model;

namespace DeployAssistant.ViewModel
{
    /// <summary>
    /// Selectable wrapper around a <see cref="ChangedFile"/> used by
    /// <see cref="MetaFileDiffViewModel"/> to drive the diff-grid checkboxes.
    /// </summary>
    public class DiffItem : ViewModelBase
    {
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public ChangedFile ChangedFile { get; }

        // ── Convenience pass-throughs for XAML bindings ──────────────────
        public DataState DataState        => ChangedFile.DataState;
        public string    FileName         => ChangedFile.DstFile?.DataName        ?? ChangedFile.SrcFile?.DataName        ?? string.Empty;
        public string    RelPath          => ChangedFile.DstFile?.DataRelPath      ?? ChangedFile.SrcFile?.DataRelPath      ?? string.Empty;
        public string?   SrcBuildVersion  => ChangedFile.SrcFile?.VersionDisplay;
        public string?   DstBuildVersion  => ChangedFile.DstFile?.VersionDisplay;
        public string?   SrcHash          => ChangedFile.SrcFile?.DataHash;
        public string?   DstHash          => ChangedFile.DstFile?.DataHash;
        public string?   DeployedV        => ChangedFile.DstFile?.DeployedProjectVersion ?? ChangedFile.SrcFile?.DeployedProjectVersion;
        /// <summary>The stored file a Restore should bring back: the pre-change SrcFile when one exists (Modified/Deleted), else the version's own DstFile (Added).</summary>
        public ProjectFile? RestoreTarget => ChangedFile.SrcFile ?? ChangedFile.DstFile;

        public DiffItem(ChangedFile changedFile, bool defaultSelected = true)
        {
            ChangedFile = changedFile;
            _isSelected = defaultSelected;
        }
    }
}
#pragma warning restore CS0618
