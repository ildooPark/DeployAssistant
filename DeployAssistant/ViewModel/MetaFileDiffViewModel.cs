using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.ViewModel.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    /// <summary>
    /// ViewModel for the "Metafile Compare" panel.
    /// Supports drag-drop or browse to load a <c>.VersionLog</c> metafile,
    /// automatically diffs it against the currently loaded project, and
    /// exposes commands to import selected changes or export a sync package.
    /// </summary>
    public class MetaFileDiffViewModel : ViewModelBase
    {
        private readonly MetaDataManager _metaDataManager;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;

        // ── Loaded metafile state ─────────────────────────────────────────

        private ProjectData? _loadedMetaFile;
        public ProjectData? LoadedMetaFile
        {
            get => _loadedMetaFile;
            private set => SetField(ref _loadedMetaFile, value);
        }

        private string _dropZoneMessage = "Drop a .VersionLog file here, or use Browse above";
        public string DropZoneMessage
        {
            get => _dropZoneMessage;
            set => SetField(ref _dropZoneMessage, value);
        }

        private bool _isMetaFileLoaded;
        public bool IsMetaFileLoaded
        {
            get => _isMetaFileLoaded;
            private set => SetField(ref _isMetaFileLoaded, value);
        }

        // ── Diff results ──────────────────────────────────────────────────

        private ObservableCollection<DiffItem> _diffItems = new();
        public ObservableCollection<DiffItem> DiffItems
        {
            get => _diffItems;
            private set
            {
                SetField(ref _diffItems, value);
                RefreshSummaryCounts();
            }
        }

        private int _addedCount;
        public int AddedCount
        {
            get => _addedCount;
            private set => SetField(ref _addedCount, value);
        }

        private int _modifiedCount;
        public int ModifiedCount
        {
            get => _modifiedCount;
            private set => SetField(ref _modifiedCount, value);
        }

        private int _deletedCount;
        public int DeletedCount
        {
            get => _deletedCount;
            private set => SetField(ref _deletedCount, value);
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            private set => SetField(ref _totalCount, value);
        }

        // ── Commands ──────────────────────────────────────────────────────

        private ICommand? _browseMetaFile;
        public ICommand BrowseMetaFile =>
            _browseMetaFile ??= new RelayCommand(_ => OnBrowseMetaFile());

        private ICommand? _selectAllChanged;
        public ICommand SelectAllChanged =>
            _selectAllChanged ??= new RelayCommand(_ => OnSelectAllChanged(), _ => DiffItems.Count > 0);

        private ICommand? _deselectAll;
        public ICommand DeselectAll =>
            _deselectAll ??= new RelayCommand(_ => OnDeselectAll(), _ => DiffItems.Count > 0);

        private ICommand? _importSelected;
        public ICommand ImportSelected =>
            _importSelected ??= new RelayCommand(_ => OnImportSelected(), _ => CanImport());

        private ICommand? _exportSyncPackage;
        public ICommand ExportSyncPackage =>
            _exportSyncPackage ??= new RelayCommand(_ => OnExportSyncPackage(), _ => CanExport());

        private const string VersionLogExtension = ".VersionLog";

        public MetaFileDiffViewModel(MetaDataManager metaDataManager,
                                     IDialogService dialogService,
                                     IUiDispatcher uiDispatcher)
        {
            _metaDataManager = metaDataManager;
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
        }

        // ── Public drag-drop entry point ──────────────────────────────────

        /// <summary>
        /// Called by the view's <c>Drop</c> handler with the path of the dropped file.
        /// </summary>
        public void LoadMetaFileFromPath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                IsMetaFileLoaded = false;
                LoadedMetaFile = null;
                DiffItems.Clear();
                DropZoneMessage = $"File not found: {Path.GetFileName(filePath)}";
                return;
            }
            if (!filePath.EndsWith(VersionLogExtension, StringComparison.OrdinalIgnoreCase))
            {
                IsMetaFileLoaded = false;
                LoadedMetaFile = null;
                DiffItems.Clear();
                DropZoneMessage = "Only .VersionLog files are supported";
                return;
            }
            LoadAndComputeDiff(filePath);
        }

        // ── Private implementation ────────────────────────────────────────

        private void OnBrowseMetaFile()
        {
            var dialog = new OpenFileDialog
            {
                Title  = "Select a .VersionLog Metafile",
                Filter = "Version Log files (*.VersionLog)|*.VersionLog|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            LoadAndComputeDiff(dialog.FileName);
        }

        private void LoadAndComputeDiff(string filePath)
        {
            ProjectData? projectData = _metaDataManager.LoadExternalMetaFile(filePath);
            if (projectData == null)
            {
                DropZoneMessage  = $"Could not parse metafile: {Path.GetFileName(filePath)}";
                IsMetaFileLoaded = false;
                return;
            }

            LoadedMetaFile   = projectData;
            IsMetaFileLoaded = true;
            DropZoneMessage  = $"Loaded: {projectData.UpdatedVersion}  ({Path.GetFileName(filePath)})";

            if (_metaDataManager.MainProjectData == null)
            {
                DiffItems = new ObservableCollection<DiffItem>();
                DropZoneMessage += " — open a project first to compute the diff";
                return;
            }

            List<ChangedFile>? diff = _metaDataManager.ComputeMetaFileDiff(projectData);
            if (diff == null)
            {
                DropZoneMessage += " — diff computation failed";
                DiffItems = new ObservableCollection<DiffItem>();
                return;
            }

            // Pre-select all items that actually differ (not identical)
            var items = diff.Select(cf => new DiffItem(cf,
                    defaultSelected: (cf.DataState & (DataState.Added | DataState.Modified | DataState.Deleted)) != 0))
                .ToList();

            DiffItems = new ObservableCollection<DiffItem>(items);
        }

        private void OnSelectAllChanged()
        {
            foreach (var item in DiffItems)
            {
                if ((item.DataState & (DataState.Added | DataState.Modified | DataState.Deleted)) != 0)
                    item.IsSelected = true;
            }
        }

        private void OnDeselectAll()
        {
            foreach (var item in DiffItems)
                item.IsSelected = false;
        }

        private bool CanImport() =>
            IsMetaFileLoaded && _metaDataManager.MainProjectData != null && DiffItems.Any(i => i.IsSelected);

        private void OnImportSelected()
        {
            var selected = DiffItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            string targetVersion = LoadedMetaFile?.UpdatedVersion ?? "?";
            var confirm = _dialogService.Confirm(
                "Import / Sync",
                $"You are about to stage {selected.Count} file change(s) to match version {targetVersion}.\n\nContinue?");
            if (confirm != DialogChoice.Yes) return;

            foreach (var item in selected)
            {
                var cf = item.ChangedFile;

                // For deletions the DstFile (current project) is the file to remove.
                // For additions and modifications the SrcFile (metafile version) is the target.
                if ((cf.DataState & DataState.Deleted) != 0)
                {
                    if (cf.DstFile != null)
                        _metaDataManager.RequestFileRestore(cf.DstFile, DataState.Deleted);
                }
                else
                {
                    if (cf.SrcFile != null)
                        _metaDataManager.RequestFileRestore(cf.SrcFile, DataState.Restored);
                }
            }
        }

        private bool CanExport() =>
            IsMetaFileLoaded && _metaDataManager.MainProjectData != null && DiffItems.Any(i => i.IsSelected);

        private void OnExportSyncPackage()
        {
            var selectedDiff = DiffItems
                .Where(i => i.IsSelected)
                .Select(i => i.ChangedFile)
                .ToList();
            if (selectedDiff.Count == 0) return;
            if (_metaDataManager.MainProjectData == null) return;
            _metaDataManager.RequestExportDiffPackage(_metaDataManager.MainProjectData, selectedDiff);
        }

        private void RefreshSummaryCounts()
        {
            AddedCount    = DiffItems.Count(i => (i.DataState & DataState.Added)    != 0);
            ModifiedCount = DiffItems.Count(i => (i.DataState & DataState.Modified) != 0);
            DeletedCount  = DiffItems.Count(i => (i.DataState & DataState.Deleted)  != 0);
            TotalCount    = DiffItems.Count;
        }
    }
}
