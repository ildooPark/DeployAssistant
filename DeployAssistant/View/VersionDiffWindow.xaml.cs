using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for VersionDiffWindow.xaml
    /// </summary>
    public partial class VersionDiffWindow : Window
    {
        public VersionDiffWindow(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            InitializeComponent();
            var versionDiffVM = new VersionDiffViewModel(App.MetaDataManager, srcProject, dstProject, diff);
            this.DataContext = versionDiffVM;
        }

        private void FileFilterKeyword_TextChanged(object sender, TextChangedEventArgs e)
        {
            DiffItemsList.Items.Filter = FilterFilesMethod;
        }

        private bool FilterFilesMethod(object obj)
        {
            var file = (ProjectFile)obj;
            return file.DataName.Contains(FilterDiffInput.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
