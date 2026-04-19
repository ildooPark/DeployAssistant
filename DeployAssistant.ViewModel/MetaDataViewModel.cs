using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using System.Collections.ObjectModel;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace DeployAssistant.ViewModel
{
    public class MetaDataViewModel : ViewModelBase
    {
        private string? _currentProjectPath;
        public string CurrentProjectPath
        {
            get => _currentProjectPath ?? "";
            set => _currentProjectPath = value;
        }

        private ProjectData? _projectData;
        public ProjectData? ProjectData
        {
            get => _projectData;
            set
            {
                _projectData = value;
                ProjectFiles = value?.ProjectFilesObs;
                ProjectName = value?.ProjectName ?? "Undefined";
                CurrentVersion = value?.UpdatedVersion ?? "Undefined";
            }
        }

        private ObservableCollection<ProjectFile>? _projectFiles;
        public ObservableCollection<ProjectFile>? ProjectFiles
        {
            get => _projectFiles;
            set
            {
                _projectFiles = value;
                OnPropertyChanged(nameof(ProjectFiles));
            }
        }

        private string? _updaterName;
        public string? UpdaterName
        {
            get => _updaterName ?? "";
            set
            {
                _updaterName = value;
                OnPropertyChanged(nameof(UpdaterName));
            }
        }

        private string? _updateLog;
        public string? UpdateLog
        {
            get => _updateLog ?? "";
            set
            {
                _updateLog = value;
                OnPropertyChanged(nameof(UpdateLog));
            }
        }

        private string? _currentMetaDataState;
        public string CurrentMetaDataState
        {
            get => _currentMetaDataState ??= "Idle";
            set
            {
                _currentMetaDataState = value;
                OnPropertyChanged(nameof(CurrentMetaDataState));
            }
        }

        private string? _projectName;
        public string ProjectName
        {
            get => _projectName ?? "Undefined";
            set
            {
                _projectName = value ?? "Undefined";
                OnPropertyChanged(nameof(ProjectName));
            }
        }

        private string? _currentVersion;
        public string CurrentVersion
        {
            get => _currentVersion ??= "Undefined";
            set
            {
                _currentVersion = value ?? "Undefined";
                OnPropertyChanged(nameof(CurrentVersion));
            }
        }

        private ICommand? _conductUpdate;
        public ICommand ConductUpdate => _conductUpdate ??= new RelayCommand(Update, CanUpdate);

        private ICommand? _getProject;
        public ICommand GetProject => _getProject ??= new RelayCommand(RetrieveProject, CanRetrieveProject);

        private readonly MetaDataManager _metaDataManager;
        private MetaDataState? _metaDataState = MetaDataState.Idle;

        public MetaDataViewModel(MetaDataManager metaDataManager)
        {
            _metaDataManager = metaDataManager;
            _metaDataManager.ProjLoadedEventHandler += MetaDataManager_ProjLoadedCallBack;
            _metaDataManager.ManagerStateEventHandler += MetaDataStateChangeCallBack;
        }

        #region Update Version

        private bool CanUpdate(object obj)
        {
            if (ProjectFiles == null || CurrentProjectPath == "") return false;
            if (_metaDataState != MetaDataState.Idle) return false;
            return true;
        }

        private void Update(object obj)
        {
            if (UpdaterName == "" || UpdateLog == "")
            {
                var response = MessageBox.Show("Must Have both Deploy Version AND UpdaterName", "ok", MessageBoxButtons.OK);
                if (response == DialogResult.OK) return;
                return;
            }
            _metaDataManager.RequestProjectUpdate(_updaterName, UpdateLog, CurrentProjectPath);
        }

        private bool CanRetrieveProject(object parameter)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            return true;
        }

        private void RetrieveProject(object parameter)
        {
            if (_projectFiles != null && _projectFiles.Count != 0) _projectFiles.Clear();
            var openFD = new WinForms.FolderBrowserDialog();
            string? projectPath;
            if (openFD.ShowDialog() == DialogResult.OK)
            {
                projectPath = openFD.SelectedPath;
                CurrentProjectPath = openFD.SelectedPath;
            }
            else return;
            openFD.Dispose();
            if (string.IsNullOrEmpty(projectPath)) return;

            bool retrieveProjectResult = _metaDataManager.RequestProjectRetrieval(projectPath);
            if (!retrieveProjectResult)
            {
                var result = MessageBox.Show($"{projectPath}\nVersionLog file not found\nInitialize A New Project?",
                    "Import Project", MessageBoxButtons.YesNo);
                if (result == DialogResult.Yes)
                {
                    Task.Run(() => _metaDataManager.RequestProjectInitialization(openFD.SelectedPath));
                }
                else
                {
                    MessageBox.Show("Please Select Another Project Path");
                    return;
                }
            }
        }

        #endregion

        #region Receiving Model Callbacks

        private void MetaDataStateChangeCallBack(MetaDataState state)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _metaDataState = state;
                CurrentMetaDataState = state.ToString();
                System.Windows.Application.Current?.MainWindow?.UpdateLayout();
            });
        }

        private void MetaDataManager_ProjLoadedCallBack(object projObj)
        {
            if (projObj is not ProjectData projectData) return;
            ProjectData = projectData;
            ProjectName = ProjectData.ProjectName ?? "Undefined";
            CurrentVersion = ProjectData.UpdatedVersion ?? "Undefined";
            CurrentProjectPath = projectData.ProjectPath;
            UpdaterName = "";
            UpdateLog = "";
        }

        #endregion
    }
}
