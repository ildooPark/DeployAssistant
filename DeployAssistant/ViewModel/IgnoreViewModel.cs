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
    /// <summary>One row of the ignore-configuration tab.</summary>
    public class IgnoreEntryRow
    {
        public RecordedFile Entry { get; }
        public bool IsDeletable { get; }
        public string Pattern => Entry.DataName;
        public string TypeDisplay => Entry.DataType == ProjectDataType.Directory
            ? Loc.T("S.Ign.TypeFolder", "Folder")
            : Loc.T("S.Ign.TypeFile", "File pattern");
        public string ScopeDisplay { get; }

        public IgnoreEntryRow(RecordedFile entry, bool isDeletable)
        {
            Entry = entry;
            IsDeletable = isDeletable;
            ScopeDisplay = BuildScopeDisplay(entry.IgnoreType);
        }

        private static string BuildScopeDisplay(IgnoreType scope)
        {
            if (scope == IgnoreType.All) return Loc.T("S.Ign.ScopeAll", "All operations");
            var parts = new List<string>();
            if ((scope & IgnoreType.Deploy) != 0) parts.Add(Loc.T("S.Ign.ScopeDeploy", "Deploy"));
            if ((scope & IgnoreType.IntegrityCheck) != 0) parts.Add(Loc.T("S.Ign.ScopeIntegrity", "Integrity check"));
            if ((scope & IgnoreType.Integration) != 0) parts.Add(Loc.T("S.Ign.ScopeIntegration", "Integration"));
            if ((scope & IgnoreType.Initialization) != 0) parts.Add(Loc.T("S.Ign.ScopeInit", "Initialization"));
            return parts.Count == 0 ? Loc.T("S.Ign.ScopeNone", "(none)") : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Drives the ignore-configuration tab: edits are collected locally and only hit
    /// <c>DeployAssistant.ignore</c> when the user presses Save
    /// (<see cref="MetaDataManager.RequestSaveIgnoreEntries"/>). Well-known default
    /// entries are listed but not deletable.
    /// </summary>
    public class IgnoreViewModel : ViewModelBase
    {
        private readonly MetaDataManager _metaDataManager;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;
        private MetaDataState _metaDataState;

        public ObservableCollection<IgnoreEntryRow> Entries { get; } = new ObservableCollection<IgnoreEntryRow>();

        private string? _newPattern;
        public string? NewPattern
        {
            get => _newPattern;
            set => SetField(ref _newPattern, value);
        }

        private int _newTypeIndex;
        public int NewTypeIndex
        {
            get => _newTypeIndex;
            set => SetField(ref _newTypeIndex, value);
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetField(ref _isDirty, value);
        }

        private ICommand? _addEntry;
        public ICommand AddEntry => _addEntry ??= new RelayCommand(AddNewEntry, CanEdit);
        private ICommand? _removeEntry;
        public ICommand RemoveEntry => _removeEntry ??= new RelayCommand(RemoveExistingEntry, CanRemoveEntry);
        private ICommand? _saveEntries;
        public ICommand SaveEntries => _saveEntries ??= new RelayCommand(SaveAllEntries, CanSave);
        private ICommand? _revertEntries;
        public ICommand RevertEntries => _revertEntries ??= new RelayCommand(_ => _metaDataManager.RequestIgnoreEntries(), CanEdit);

        public IgnoreViewModel(MetaDataManager metaDataManager, IDialogService dialogService, IUiDispatcher uiDispatcher)
        {
            _metaDataManager = metaDataManager;
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
            _metaDataManager.IgnoreEntriesEventHandler += IgnoreEntriesCallBack;
            TrackUnsubscribe(() => _metaDataManager.IgnoreEntriesEventHandler -= IgnoreEntriesCallBack);
            _metaDataManager.ProjLoadedEventHandler += ProjLoadedCallBack;
            TrackUnsubscribe(() => _metaDataManager.ProjLoadedEventHandler -= ProjLoadedCallBack);
            _metaDataManager.ManagerStateEventHandler += state => _metaDataState = state;
        }

        private void ProjLoadedCallBack(object projObj) => _metaDataManager.RequestIgnoreEntries();

        private void IgnoreEntriesCallBack(List<RecordedFile> entries)
        {
            _uiDispatcher.Invoke(() =>
            {
                Entries.Clear();
                foreach (RecordedFile entry in entries)
                    Entries.Add(new IgnoreEntryRow(entry, !_metaDataManager.IsWellKnownIgnoreEntry(entry)));
                IsDirty = false;
            });
        }

        private bool CanEdit(object? obj) => _metaDataState == MetaDataState.Idle && Entries.Count > 0;

        private bool CanRemoveEntry(object? obj)
            => _metaDataState == MetaDataState.Idle && obj is IgnoreEntryRow row && row.IsDeletable;

        private bool CanSave(object? obj) => _metaDataState == MetaDataState.Idle && IsDirty;

        private void AddNewEntry(object? obj)
        {
            string pattern = (NewPattern ?? "").Trim().Trim('\\', '/');
            if (pattern.Length == 0) return;
            if (pattern.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c != '*' && c != '?').ToArray()) >= 0)
            {
                _dialogService.Inform(Loc.T("S.IgnoreTab", "Ignore List"),
                    Loc.T("S.Ign.InvalidPattern", "The pattern contains invalid characters."));
                return;
            }
            ProjectDataType type = NewTypeIndex == 1 ? ProjectDataType.Directory : ProjectDataType.File;
            if (Entries.Any(r => r.Entry.DataType == type &&
                                 string.Equals(r.Entry.DataName, pattern, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogService.Inform(Loc.T("S.IgnoreTab", "Ignore List"),
                    Loc.T("S.Ign.Duplicate", "That entry already exists."));
                return;
            }
            Entries.Add(new IgnoreEntryRow(new RecordedFile(pattern, type, IgnoreType.All), isDeletable: true));
            NewPattern = "";
            IsDirty = true;
        }

        private void RemoveExistingEntry(object? obj)
        {
            if (obj is not IgnoreEntryRow row || !row.IsDeletable) return;
            Entries.Remove(row);
            IsDirty = true;
        }

        private void SaveAllEntries(object? obj)
        {
            bool saved = _metaDataManager.RequestSaveIgnoreEntries(Entries.Select(r => r.Entry).ToList());
            if (!saved)
                _dialogService.Inform(Loc.T("S.IgnoreTab", "Ignore List"),
                    Loc.T("S.Ign.SaveFailed", "Could not save the ignore list."));
        }
    }
}
