using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for OverlapFileWindow.xaml
    /// </summary>
    public partial class OverlapFileWindow : Window
    {
        public OverlapFileWindow(List<ChangedFile> overlapFiles, List<ChangedFile> newFiles)
        {
            InitializeComponent();
            OverlapFileViewModel _overlapFileWindow = new OverlapFileViewModel(overlapFiles, newFiles);
            this.DataContext = _overlapFileWindow;
            _overlapFileWindow.TaskFinishedEventHandler += TaskFinishedCallBack;
        }

        private void TaskFinishedCallBack()
        {
            this.Close(); 
        }

        private void NewFileFilterKeyword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            NewFileDirectories.Items.Filter = FilterFilesMethod;
        }
        private bool FilterFilesMethod(object obj)
        {
            var file = (ChangedFile)obj;

            return file.DstFile.DataName.Contains(NewFileFilterKeyword.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}