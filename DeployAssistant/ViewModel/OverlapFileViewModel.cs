using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    /// <summary>
    /// Groups all candidate destinations for a single ambiguous source file into one row,
    /// so the user can pick the correct destination from a ComboBox instead of toggling checkboxes.
    /// </summary>
    public class GroupedOverlap : ViewModelBase
    {
        public string SrcFileName { get; }

        /// <summary>All candidate <see cref="ChangedFile"/> entries for this source file.</summary>
        public List<ChangedFile> Candidates { get; }

        private ChangedFile? _selected;
        public ChangedFile? Selected
        {
            get => _selected;
            set
            {
                // Clear previous selection
                foreach (var c in Candidates)
                    if (c.DstFile != null) c.DstFile.IsDstFile = false;

                _selected = value;
                if (_selected?.DstFile != null)
                    _selected.DstFile.IsDstFile = true;

                OnPropertyChanged(nameof(Selected));
            }
        }

        public GroupedOverlap(string srcFileName, List<ChangedFile> candidates)
        {
            SrcFileName = srcFileName;
            Candidates = candidates;
            // Pre-select the first candidate (or whichever was already flagged true)
            _selected = candidates.FirstOrDefault(c => c.DstFile?.IsDstFile == true)
                        ?? candidates.FirstOrDefault();
            if (_selected?.DstFile != null)
                _selected.DstFile.IsDstFile = true;
        }
    }

    public class OverlapFileViewModel : ViewModelBase
    {
        private List<ChangedFile>? _overlapFilesList;
        public List<ChangedFile>? OverlapFilesList
        {
            get => _overlapFilesList ??= new List<ChangedFile>();
            set
            {
                _overlapFilesList = value;
                _groupedOverlapList = null;
                OnPropertyChanged(nameof(OverlapFilesList));
                OnPropertyChanged(nameof(GroupedOverlapList));
            }
        }

        private List<ChangedFile>? _newFilesList;
        public List<ChangedFile>? NewFilesList
        {
            get => _newFilesList ??= new List<ChangedFile>();
            set
            {
                _newFilesList = value;
                _groupedNewList = null;
                OnPropertyChanged(nameof(NewFilesList));
                OnPropertyChanged(nameof(GroupedNewList));
            }
        }

        /// <summary>
        /// Overlap files grouped by source filename — one row per ambiguous file,
        /// ComboBox shows all candidate destination paths.
        /// </summary>
        private List<GroupedOverlap>? _groupedOverlapList;
        public List<GroupedOverlap> GroupedOverlapList =>
            _groupedOverlapList ??= (_overlapFilesList ?? new List<ChangedFile>())
                .GroupBy(f => f.SrcFile?.DataName ?? string.Empty)
                .Select(g => new GroupedOverlap(g.Key, g.ToList()))
                .ToList();

        /// <summary>
        /// New files grouped by source filename — one row per new file,
        /// ComboBox shows all candidate destination folders.
        /// </summary>
        private List<GroupedOverlap>? _groupedNewList;
        public List<GroupedOverlap> GroupedNewList =>
            _groupedNewList ??= (_newFilesList ?? new List<ChangedFile>())
                .GroupBy(f => f.SrcFile?.DataName ?? string.Empty)
                .Select(g => new GroupedOverlap(g.Key, g.ToList()))
                .ToList();

        private ICommand? _confirmCommand;
        public ICommand ConfirmCommand => _confirmCommand ??= new RelayCommand(ConfirmChoices);

        public event Action? TaskFinishedEventHandler;

        private readonly MetaDataManager _metaDataManager;

        public OverlapFileViewModel(MetaDataManager metaDataManager, List<ChangedFile> registeredOverlaps, List<ChangedFile>? registeredNew = null)
        {
            _metaDataManager = metaDataManager;
            OverlapFilesList = registeredOverlaps;
            NewFilesList = registeredNew;
        }

        private void ConfirmChoices(object? obj)
        {
            _metaDataManager.RequestOverlappedFileAllocation(_overlapFilesList, _newFilesList);
            TaskFinishedEventHandler?.Invoke();
        }
    }
}
