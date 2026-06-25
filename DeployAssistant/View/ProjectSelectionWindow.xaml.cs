using System.Windows;
using System.Windows.Controls;
using DeployAssistant.ViewModel;

namespace DeployAssistant.View
{
    public partial class ProjectSelectionWindow : Window
    {
        public ProjectSelectionWindow()
        {
            InitializeComponent();
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Row.Item is DeployAssistant.DataComponent.ProjectEntry entry)
                {
                    var vm = this.DataContext as ProjectSelectionViewModel;
                    
                    // The binding updates Name after commit, but we can do it now by taking the TextBox text.
                    var el = e.EditingElement as TextBox;
                    if (el != null)
                    {
                        vm?.SaveProjectRename(entry, el.Text);
                    }
                }
            }
        }
    }
}
