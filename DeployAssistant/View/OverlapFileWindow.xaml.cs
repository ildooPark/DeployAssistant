using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
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
            var services = ((App)Application.Current).Services!;
            var overlapFileVM = new OverlapFileViewModel(services.MetaDataManager, overlapFiles, newFiles);
            this.DataContext = overlapFileVM;
            overlapFileVM.TaskFinishedEventHandler += TaskFinishedCallBack;
            Closed += (_, _) => (overlapFileVM as IDisposable)?.Dispose();
        }

        private void TaskFinishedCallBack()
        {
            this.Close();
        }

        private void NewFileFilterKeyword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            NewFileDirectories.Items.Filter = FilterNewFilesMethod;
        }

        private bool FilterNewFilesMethod(object obj)
        {
            var group = (GroupedOverlap)obj;
            return group.SrcFileName.Contains(NewFileFilterKeyword.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
