using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using DeployAssistant.ViewModel.Utils;

namespace DeployAssistant.ViewModel
{
    public class ProjectSelectionViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        public Action? RequestCloseWithSuccess { get; set; }

        public ObservableCollection<ProjectEntry> Projects { get; }

        private ProjectEntry? _selectedProject;
        public ProjectEntry? SelectedProject
        {
            get => _selectedProject;
            set
            {
                _selectedProject = value;
                OnPropertyChanged(nameof(SelectedProject));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string? _selectedPath;
        public string? SelectedPath
        {
            get => _selectedPath;
            private set
            {
                _selectedPath = value;
                OnPropertyChanged(nameof(SelectedPath));
            }
        }

        public ICommand OpenCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand BrowseCommand { get; }

        public ProjectSelectionViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
            Projects = new ObservableCollection<ProjectEntry>(
                ProjectRegistry.Load().OrderByDescending(e => e.LastAccessedUtc)
            );

            OpenCommand = new RelayCommand(OpenProject, CanOpenProject);
            RemoveCommand = new RelayCommand(RemoveProject, CanModifyProject);
            BrowseCommand = new RelayCommand(BrowseProject);
        }

        private bool CanOpenProject(object parameter) => SelectedProject != null;
        private bool CanModifyProject(object parameter) => SelectedProject != null;

        private void OpenProject(object parameter)
        {
            if (SelectedProject == null) return;
            SelectedPath = SelectedProject.Path;
            RequestCloseWithSuccess?.Invoke();
        }

        private void RemoveProject(object parameter)
        {
            if (SelectedProject == null) return;
            
            if (_dialogService.Confirm("Remove Project", $"Remove '{SelectedProject.Name}' from the list?") == DialogChoice.Yes)
            {
                ProjectRegistry.Remove(SelectedProject.Path);
                Projects.Remove(SelectedProject);
                SelectedProject = null;
            }
        }

        public void SaveProjectRename(ProjectEntry entry, string newName)
        {
            ProjectRegistry.SaveOrUpdate(entry.Path, newName);
            entry.Name = newName; // In case the binding doesn't trigger, keep model consistent
        }

        private void BrowseProject(object parameter)
        {
            var path = _dialogService.PickFolder("Select Project Directory");
            if (!string.IsNullOrEmpty(path))
            {
                SelectedPath = path;
                RequestCloseWithSuccess?.Invoke();
            }
        }
    }
}
