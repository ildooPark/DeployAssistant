using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using DeployAssistant.Model;
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
    /// Interaction logic for CompatibleVersionWindow.xaml
    /// </summary>
    public partial class VersionComparisonWindow : Window
    {
        public VersionComparisonWindow(ProjectData srcData, List<ProjectSimilarity> similarities)
        {
            InitializeComponent();
            VersionCompatibilityViewModel vcVM = new VersionCompatibilityViewModel (srcData, similarities);
            this.DataContext = vcVM;
        }
    }
}
