using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for CompatibleVersionWindow.xaml
    /// </summary>
    public partial class VersionComparisonWindow : Window
    {
        public VersionComparisonWindow(ProjectData srcData, List<ProjectSimilarity> similarities)
        {
            InitializeComponent();
            var vcVM = new VersionCompatibilityViewModel(srcData, similarities);
            this.DataContext = vcVM;
        }
    }
}
