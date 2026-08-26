using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using DeployAssistant.ViewModel.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    /// <summary>
    /// Backs <c>IntegrityLogWindow</c> in its two modes: integrity-check results
    /// (dashboard with verdict, tallies, and per-row revert) and full-version file
    /// view (Full Log). Both share the date-grouped file grid.
    /// </summary>
    public class VersionCheckViewModel : ViewModelBase
    {
        public sealed class IntegrityRow
        {
            public IntegrityRow(ProjectFile file, IntegrityFileOutcome outcome, string outcomeLabel,
                                string badge, string prevVersion, string groupDate, string time)
            {
                File = file;
                Outcome = outcome;
                OutcomeLabel = outcomeLabel;
                Badge = badge;
                PrevVersion = prevVersion;
                GroupDate = groupDate;
                Time = time;
            }

            public ProjectFile File { get; }
            public IntegrityFileOutcome Outcome { get; }
            public string OutcomeLabel { get; }
            /// <summary>유입 (copied in: CreationTime &gt; LastWriteTime) / 수정 (edited in place) / 삭제.</summary>
            public string Badge { get; }
            public string PrevVersion { get; }
            public string GroupDate { get; }
            public string Time { get; }
            public string Name => File.DataName;
            public string RelPath => File.DataRelPath;
            public string CurrVersion => File.VersionDisplay;
        }

        private readonly MetaDataManager _metaDataManager;
        private readonly ProjectData _projectData;

        public bool IsIntegrityResult { get; }
        public bool IsClean => IsIntegrityResult && Rows.Count == 0;

        public ObservableCollection<IntegrityRow> Rows { get; } = new ObservableCollection<IntegrityRow>();

        public int CountModified { get; private set; }
        public int CountAdded { get; private set; }
        public int CountDeleted { get; private set; }
        public int CountHashFailed { get; }
        public int CountMetadataFallback { get; }

        private string _verdictText = "";
        public string VerdictText
        {
            get => _verdictText;
            private set { _verdictText = value; OnPropertyChanged(nameof(VerdictText)); }
        }

        public string SubtitleText { get; }

        private string? _changeLog;
        public string ChangeLog
        {
            get => _changeLog ??= "";
            set { _changeLog = value; OnPropertyChanged(nameof(ChangeLog)); }
        }

        private string? _updateLog;
        public string UpdateLog
        {
            get => _updateLog ??= "";
            set { _updateLog = value; OnPropertyChanged(nameof(UpdateLog)); }
        }

        private ObservableCollection<ProjectFile>? _fileList;
        public ObservableCollection<ProjectFile> FileList
        {
            get => _fileList ??= new ObservableCollection<ProjectFile>();
            set { _fileList = value; OnPropertyChanged(nameof(FileList)); }
        }

        private ICommand? _exportToXLSX;
        public ICommand ExportToXLSX => _exportToXLSX ??= new RelayCommand(ExportFile, CanExport);
        private void ExportFile(object obj) => _metaDataManager.RequestExportProjectFilesXLSX(FileList, _projectData);
        private bool CanExport(object obj) => FileList.Count > 0;

        private ICommand? _revertRow;
        public ICommand RevertRow => _revertRow ??= new RelayCommand(RevertOne, o => IsIntegrityResult && o is IntegrityRow);
        private void RevertOne(object? obj)
        {
            if (obj is not IntegrityRow row) return;
            if (_metaDataManager.RequestRevertChange(row.File)) RemoveRow(row);
        }

        private ICommand? _revertAllRows;
        public ICommand RevertAllRows => _revertAllRows ??= new RelayCommand(RevertAll, _ => IsIntegrityResult && Rows.Count > 0);
        private void RevertAll(object? obj)
        {
            foreach (IntegrityRow row in Rows.ToList())
                if (_metaDataManager.RequestRevertChange(row.File)) RemoveRow(row);
        }

        private void RemoveRow(IntegrityRow row)
        {
            Rows.Remove(row);
            switch (row.Outcome)
            {
                case IntegrityFileOutcome.Added: CountAdded--; break;
                case IntegrityFileOutcome.Deleted: CountDeleted--; break;
                default: CountModified--; break;
            }
            OnPropertyChanged(nameof(CountModified));
            OnPropertyChanged(nameof(CountAdded));
            OnPropertyChanged(nameof(CountDeleted));
            OnPropertyChanged(nameof(IsClean));
            RefreshVerdict();
        }

        private void RefreshVerdict()
        {
            VerdictText = Rows.Count == 0
                ? Loc.T("S.Ig.VerdictClean", "Every file matches the snapshot.")
                : string.Format(Loc.T("S.Ig.VerdictDirty", "{0} item(s) differ from the current version"), Rows.Count);
        }

        /// <summary>Integrity-result mode.</summary>
        public VersionCheckViewModel(MetaDataManager metaDataManager, ProjectData projectData, string versionLog,
                                     ObservableCollection<ProjectFile> fileList,
                                     IReadOnlyDictionary<string, IntegrityFileOutcome>? outcomes = null)
        {
            _metaDataManager = metaDataManager;
            _projectData = projectData;
            _changeLog = versionLog;
            _fileList = fileList;
            IsIntegrityResult = true;

            var mainFiles = metaDataManager.MainProjectData?.ProjectFiles;
            foreach (ProjectFile file in fileList)
                Rows.Add(BuildIntegrityRow(file, outcomes, mainFiles));

            CountModified = Rows.Count(r => r.Outcome is IntegrityFileOutcome.Modified or IntegrityFileOutcome.Checked or IntegrityFileOutcome.MetadataFallback);
            CountAdded = Rows.Count(r => r.Outcome == IntegrityFileOutcome.Added);
            CountDeleted = Rows.Count(r => r.Outcome == IntegrityFileOutcome.Deleted);
            if (outcomes != null)
            {
                CountHashFailed = outcomes.Values.Count(o => o == IntegrityFileOutcome.HashFailed);
                CountMetadataFallback = outcomes.Values.Count(o => o == IntegrityFileOutcome.MetadataFallback);
            }

            int scanned = mainFiles?.Values.Count(f => f.DataType == ProjectDataType.File) ?? fileList.Count;
            SubtitleText = string.Format(Loc.T("S.Ig.SubtitleScan", "{0} · {1} file(s) scanned"), projectData.UpdatedVersion, scanned);
            RefreshVerdict();
        }

        /// <summary>Full-version (Full Log) mode.</summary>
        public VersionCheckViewModel(MetaDataManager metaDataManager, ProjectData projectData)
        {
            _metaDataManager = metaDataManager;
            _projectData = projectData;
            IsIntegrityResult = false;
            FileList = _projectData.ProjectFilesObs;
            ChangeLog = _projectData.ChangeLog ?? "";
            UpdateLog = _projectData.UpdateLog ?? "";

            foreach (ProjectFile file in FileList.Where(f => f.DataType == ProjectDataType.File))
            {
                Rows.Add(new IntegrityRow(file, IntegrityFileOutcome.Checked, "",
                    badge: "", prevVersion: "",
                    groupDate: file.UpdatedTime.ToString("yyyy-MM-dd"),
                    time: file.UpdatedTime.ToString("HH:mm")));
            }
            SubtitleText = string.Format(Loc.T("S.Ig.SubtitleScan", "{0} · {1} file(s) scanned"),
                projectData.UpdatedVersion, Rows.Count)
                + (string.IsNullOrEmpty(projectData.UpdaterName) ? "" : " · " + projectData.UpdaterName);
            VerdictText = string.Format(Loc.T("S.Ig.FullLogBanner", "Version '{0}'"), projectData.UpdatedVersion);
        }

        private IntegrityRow BuildIntegrityRow(ProjectFile file,
            IReadOnlyDictionary<string, IntegrityFileOutcome>? outcomes,
            Dictionary<string, ProjectFile>? mainFiles)
        {
            IntegrityFileOutcome outcome;
            if (outcomes == null || !outcomes.TryGetValue(file.DataRelPath, out outcome))
            {
                outcome = (file.DataState & DataState.Added) != 0 ? IntegrityFileOutcome.Added
                    : (file.DataState & DataState.Deleted) != 0 ? IntegrityFileOutcome.Deleted
                    : IntegrityFileOutcome.Modified;
            }

            string prev = "";
            if (mainFiles != null && mainFiles.TryGetValue(file.DataRelPath, out ProjectFile? recorded))
                prev = recorded.VersionDisplay;

            string badge;
            DateTime groupTime;
            if (outcome == IntegrityFileOutcome.Deleted)
            {
                badge = Loc.T("S.Ovl.OutDeleted", "deleted");
                groupTime = file.UpdatedTime;
            }
            else
            {
                badge = "";
                groupTime = file.UpdatedTime;
                try
                {
                    var info = new FileInfo(PathCompat.ToNetFrameworkLongPath(
                        Path.Combine(_projectData.ProjectPath, file.DataRelPath)));
                    if (info.Exists)
                    {
                        // Copying stamps a fresh CreationTime but preserves the source's
                        // LastWriteTime, so Create > Write marks a file brought in from
                        // elsewhere; Write >= Create marks an in-place edit.
                        bool copiedIn = info.CreationTime > info.LastWriteTime.AddSeconds(2);
                        badge = copiedIn ? Loc.T("S.Ig.BadgeImported", "imported")
                                         : Loc.T("S.Ig.BadgeEdited", "edited");
                        groupTime = outcome == IntegrityFileOutcome.Added ? info.CreationTime : info.LastWriteTime;
                    }
                }
                catch (Exception) { }
            }

            string label = outcome switch
            {
                IntegrityFileOutcome.Added => Loc.T("S.Ovl.OutAdded", "added"),
                IntegrityFileOutcome.Deleted => Loc.T("S.Ovl.OutDeleted", "deleted"),
                IntegrityFileOutcome.MetadataFallback => Loc.T("S.Ovl.OutMetadata", "metadata check"),
                IntegrityFileOutcome.HashFailed => Loc.T("S.Ovl.OutHashFailed", "hash failed"),
                _ => Loc.T("S.Ovl.OutModified", "modified"),
            };

            return new IntegrityRow(file, outcome, label, badge, prev,
                groupTime.ToString("yyyy-MM-dd"), groupTime.ToString("HH:mm"));
        }
    }
}
