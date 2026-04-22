using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for VersionIntegrationView.xaml
    /// </summary>
    public partial class VersionIntegrationView : Window
    {
        public VersionIntegrationView(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            InitializeComponent();
            var vm = new VersionIntegrationViewModel(App.MetaDataManager, srcProject, dstProject, diff);
            this.DataContext = vm;
        }

        private void FileFilterKeyword_TextChanged(object sender, TextChangedEventArgs e)
        {
            FileDifferences.Items.Filter = FilterFilesMethod;
        }

        private bool FilterFilesMethod(object obj)
        {
            var file = (ChangedFile)obj;
            var name = file.SrcFile?.DataName ?? file.DstFile?.DataName ?? string.Empty;
            return name.Contains(FilterDiffInput.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
