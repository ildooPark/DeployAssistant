using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
            VersionDiffViewModel VersionDiffVM = new VersionDiffViewModel(srcProject, dstProject, diff);
            this.DataContext = VersionDiffVM;
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
