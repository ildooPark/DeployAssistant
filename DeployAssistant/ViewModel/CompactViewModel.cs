using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.ViewModel.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    /// <summary>One changed file in the compact panel: a state badge, the file name, and its folder.</summary>
    public sealed class CompactChangeRow
    {
        public CompactChangeRow(string kind, string badge, string name, string folder)
        {
            Kind = kind; Badge = badge; Name = name; Folder = folder;
        }
        /// <summary>Added / Deleted / Modified / Restored — drives the badge colour.</summary>
        public string Kind { get; }
        public string Badge { get; }
        public string Name { get; }
        public string Folder { get; }
    }

    /// <summary>
    /// State behind the compact layout: the last integrity result in summary form and the
    /// revisions sub-page. Actions reuse the full layout's commands (FileTrackVM, MetaDataVM,
    /// BackupVM); this VM only listens to MetaDataManager events, never calls managers itself.
    /// </summary>
    public class CompactViewModel : ViewModelBase
    {
        /// <summary>Rows shown before "Show all" takes over in the full layout.</summary>
        public const int MaxRows = 8;

        private readonly MetaDataManager _metaDataManager;
        private readonly IUiDispatcher _uiDispatcher;

        public CompactViewModel(MetaDataManager metaDataManager, IUiDispatcher uiDispatcher)
        {
            _metaDataManager = metaDataManager;
            _uiDispatcher = uiDispatcher;
            _metaDataManager.IntegrityCheckCompleteEventHandler += IntegrityCheckCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.IntegrityCheckCompleteEventHandler -= IntegrityCheckCompleteCallBack);
            _metaDataManager.ProjLoadedEventHandler += ProjLoadedCallBack;
            TrackUnsubscribe(() => _metaDataManager.ProjLoadedEventHandler -= ProjLoadedCallBack);
        }

        /// <summary>Raised by "Show all": the window switches to the full layout and opens the integrity dashboard.</summary>
        public event Action? ShowAllRequested;

        private bool _hasResult;
        public bool HasResult { get => _hasResult; private set { if (SetField(ref _hasResult, value)) RaiseDerived(); } }

        private int _modifiedCount, _addedCount, _deletedCount, _restoredCount;
        public int ModifiedCount { get => _modifiedCount; private set => SetField(ref _modifiedCount, value); }
        public int AddedCount { get => _addedCount; private set => SetField(ref _addedCount, value); }
        public int DeletedCount { get => _deletedCount; private set => SetField(ref _deletedCount, value); }
        public int RestoredCount { get => _restoredCount; private set => SetField(ref _restoredCount, value); }

        private int _changedCount;
        public int ChangedCount { get => _changedCount; private set { if (SetField(ref _changedCount, value)) RaiseDerived(); } }

        public bool IsClean => HasResult && ChangedCount == 0;
        public bool HasChanges => HasResult && ChangedCount > 0;

        /// <summary>Changed files beyond <see cref="MaxRows"/>, reachable through "Show all".</summary>
        public int MoreCount => Math.Max(0, ChangedCount - MaxRows);

        private IReadOnlyList<CompactChangeRow> _topChanges = Array.Empty<CompactChangeRow>();
        public IReadOnlyList<CompactChangeRow> TopChanges { get => _topChanges; private set => SetField(ref _topChanges, value); }

        private int _samplePercent = 100;
        private DateTime _finishedAt;

        /// <summary>
        /// "Fast check, 25% sample · 10:42" or "Full hash · 10:42". Composed on read so a
        /// language switch (<see cref="RefreshLocalizedText"/>) re-renders it.
        /// </summary>
        public string ResultDetail => !HasResult ? "" : _samplePercent >= 100
            ? string.Format(Loc.T("S.Compact.FullHashAt", "Full hash · {0:HH:mm}"), _finishedAt)
            : string.Format(Loc.T("S.Compact.FastCheckAt", "Fast check, {0}% sample · {1:HH:mm}"), _samplePercent, _finishedAt);

        /// <summary>Re-reads code-composed strings after the UI language changes.</summary>
        public void RefreshLocalizedText() => OnPropertyChanged(nameof(ResultDetail));

        private bool _isRevisionsOpen;
        public bool IsRevisionsOpen { get => _isRevisionsOpen; set => SetField(ref _isRevisionsOpen, value); }

        // Kept for "Show all", which opens the same dashboard the full layout opens itself.
        public ProjectData? LastProject { get; private set; }
        public string LastChangeLog { get; private set; } = "";
        public ObservableCollection<ProjectFile> LastFiles { get; private set; } = new ObservableCollection<ProjectFile>();

        private ICommand? _showRevisions;
        public ICommand ShowRevisions => _showRevisions ??= new RelayCommand(_ => IsRevisionsOpen = true);

        private ICommand? _hideRevisions;
        public ICommand HideRevisions => _hideRevisions ??= new RelayCommand(_ => IsRevisionsOpen = false);

        private ICommand? _showAll;
        public ICommand ShowAll => _showAll ??= new RelayCommand(_ => ShowAllRequested?.Invoke(), _ => HasChanges);

        private void IntegrityCheckCompleteCallBack(string changeLog, ObservableCollection<ProjectFile> files)
        {
            int percent = _metaDataManager.LastIntegritySamplePercent;
            ProjectData? project = _metaDataManager.MainProjectData;
            DateTime finishedAt = DateTime.Now;
            _uiDispatcher.Invoke(() => ApplyResult(changeLog, files, percent, project, finishedAt));
        }

        /// <summary>Summarises an integrity result. Public so tests can feed results without a manager run.</summary>
        public void ApplyResult(string changeLog, IEnumerable<ProjectFile> files, int samplePercent, ProjectData? project, DateTime finishedAt)
        {
            // The event carries the whole pre-staged queue; drag & drop entries waiting there
            // are not integrity findings.
            var changes = files.Where(f => (f.DataState & DataState.IntegrityChecked) != 0
                                           && f.DataType == ProjectDataType.File).ToList();

            LastProject = project;
            LastChangeLog = changeLog ?? "";
            LastFiles = new ObservableCollection<ProjectFile>(files);

            ModifiedCount = changes.Count(f => KindOf(f.DataState) == "Modified");
            AddedCount = changes.Count(f => KindOf(f.DataState) == "Added");
            DeletedCount = changes.Count(f => KindOf(f.DataState) == "Deleted");
            RestoredCount = changes.Count(f => KindOf(f.DataState) == "Restored");
            TopChanges = changes.Take(MaxRows).Select(ToRow).ToList();
            ChangedCount = changes.Count;
            _samplePercent = samplePercent;
            _finishedAt = finishedAt;
            HasResult = true;
            RefreshLocalizedText();
        }

        private void ProjLoadedCallBack(object projObj)
        {
            // A load, an update or a checkout moves main: the last result no longer describes it.
            _uiDispatcher.Invoke(Reset);
        }

        public void Reset()
        {
            HasResult = false;
            ChangedCount = 0;
            ModifiedCount = AddedCount = DeletedCount = RestoredCount = 0;
            TopChanges = Array.Empty<CompactChangeRow>();
            IsRevisionsOpen = false;
            RefreshLocalizedText();
        }

        private void RaiseDerived()
        {
            OnPropertyChanged(nameof(IsClean));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(MoreCount));
        }

        private static string KindOf(DataState state)
        {
            if ((state & DataState.Deleted) != 0) return "Deleted";
            if ((state & DataState.Added) != 0) return "Added";
            if ((state & DataState.Restored) != 0) return "Restored";
            return "Modified";
        }

        private static CompactChangeRow ToRow(ProjectFile file)
        {
            string kind = KindOf(file.DataState);
            string badge = kind switch { "Deleted" => "DEL", "Added" => "ADD", "Restored" => "RST", _ => "MOD" };
            string rel = file.DataRelPath ?? "";
            string folder = Path.GetDirectoryName(rel) ?? "";
            string name = string.IsNullOrEmpty(file.DataName) ? Path.GetFileName(rel) : file.DataName;
            return new CompactChangeRow(kind, badge, name, folder);
        }
    }
}
