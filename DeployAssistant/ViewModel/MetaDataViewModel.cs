using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using DeployAssistant.View;
using System.Collections.ObjectModel;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace DeployAssistant.ViewModel
{
    public class MetaDataViewModel : ViewModelBase
    {
        private string? currentProjectPath; 
        public string CurrentProjectPath
        {
            get => currentProjectPath ?? ""; 
            set => currentProjectPath = value;
        }

        private ProjectData? projectData; 
        public ProjectData? ProjectData
        {
            get => projectData ?? null;
            set
            {
                projectData = value;
                ProjectFiles = value?.ProjectFilesObs;
                ProjectName = value?.ProjectName ?? "Undefined";
                CurrentVersion = value?.UpdatedVersion ?? "Undefined"; 
            }
        }

        private ObservableCollection<ProjectFile>? projectFiles;
        public ObservableCollection<ProjectFile>? ProjectFiles
        {
            get => projectFiles;
            set
            {
                projectFiles = value;
                OnPropertyChanged("ProjectFiles");
            }
        }

        private string? updaterName;
        public string? UpdaterName
        {
            get => updaterName ?? "";
            set
            {
                updaterName = value;
                OnPropertyChanged("UpdaterName");
            }
        }

        private string? updateLog;
        public string? UpdateLog
        {
            get => updateLog ?? "";
            set
            {
                updateLog = value;
                OnPropertyChanged("UpdateLog");
            }
        }

        private string? _currentMetaDataState; 
        public string CurrentMetaDataState
        {
            get => _currentMetaDataState ??= "Idle";
            set
            {
                _currentMetaDataState = value;
                OnPropertyChanged("CurrentMetaDataState");
            }
        }

        private string? projectName;
        public string ProjectName
        {
            get => projectName ?? "Undefined";
            set
            {
                projectName = value ?? "Undefined";
                OnPropertyChanged("ProjectName");
            }
        }

        private string? currentVersion;
        public string CurrentVersion
        {
            get => currentVersion ??= "Undefined";
            set
            {
                currentVersion = value ?? "Undefined";
                OnPropertyChanged("CurrentVersion");
            }
        }

        private ICommand? conductUpdate;
        public ICommand ConductUpdate => conductUpdate ??= new RelayCommand(Update, CanUpdate);

        private ICommand? getProject;
        public ICommand GetProject => getProject ??= new RelayCommand(RetrieveProject, CanRetrieveProject);

        private MetaDataManager _metaDataManager;
        private MetaDataState? _metaDataState = MetaDataState.Idle;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public MetaDataViewModel()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            this._metaDataManager = App.MetaDataManager;
            this._metaDataManager.ProjLoadedEventHandler += MetaDataManager_ProjLoadedCallBack;
            this._metaDataManager.ManagerStateEventHandler += MetaDataStateChangeCallBack;
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
            _metaDataManager.RequestProjectUpdate(updaterName, UpdateLog, CurrentProjectPath);
        }

        private bool CanRetrieveProject(object parameter)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            return true;
        }
        private void RetrieveProject(object parameter)
        {
            if (projectFiles != null && projectFiles.Count != 0) projectFiles.Clear();
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
                    Task.Run (() => _metaDataManager.RequestProjectInitialization(openFD.SelectedPath));
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
            App.Current.Dispatcher.Invoke(() =>
            {
                _metaDataState = state;
                CurrentMetaDataState = state.ToString();
                ((MainWindow)System.Windows.Application.Current.MainWindow).UpdateLayout();
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