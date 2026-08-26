using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace DeployAssistant.View
{
    /// <summary>
    /// Integrity-check results dashboard / full-version file view.
    /// Grouping and filtering are view concerns and live here.
    /// </summary>
    public partial class IntegrityLogWindow : Window
    {
        public IntegrityLogWindow(ProjectData projData, string versionLog, ObservableCollection<ProjectFile> fileList,
                                  IReadOnlyDictionary<string, IntegrityFileOutcome>? outcomes = null)
        {
            InitializeComponent();
            var services = ((App)Application.Current).Services!;
            var vm = new VersionCheckViewModel(services.MetaDataManager, projData, versionLog, fileList, outcomes);
            this.DataContext = vm;
            ConfigureView(vm);
            Closed += (_, _) => (vm as IDisposable)?.Dispose();
        }

        public IntegrityLogWindow(ProjectData projectData)
        {
            InitializeComponent();
            var services = ((App)Application.Current).Services!;
            var vm = new VersionCheckViewModel(services.MetaDataManager, projectData);
            this.DataContext = vm;
            ConfigureView(vm);
            Closed += (_, _) => (vm as IDisposable)?.Dispose();
        }

        private ICollectionView? _view;

        private void ConfigureView(VersionCheckViewModel vm)
        {
            _view = CollectionViewSource.GetDefaultView(vm.Rows);
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VersionCheckViewModel.IntegrityRow.GroupDate)));
            _view.SortDescriptions.Add(new SortDescription(nameof(VersionCheckViewModel.IntegrityRow.GroupDate), ListSortDirection.Descending));
            _view.SortDescriptions.Add(new SortDescription(nameof(VersionCheckViewModel.IntegrityRow.Time), ListSortDirection.Descending));
            _view.Filter = FilterRow;
        }

        private bool FilterRow(object obj)
        {
            if (obj is not VersionCheckViewModel.IntegrityRow row) return true;

            bool anyChip = ChipModified.IsChecked == true || ChipAdded.IsChecked == true || ChipDeleted.IsChecked == true;
            if (anyChip)
            {
                bool match =
                    (ChipModified.IsChecked == true && row.Outcome is IntegrityFileOutcome.Modified or IntegrityFileOutcome.Checked or IntegrityFileOutcome.MetadataFallback or IntegrityFileOutcome.HashFailed) ||
                    (ChipAdded.IsChecked == true && row.Outcome == IntegrityFileOutcome.Added) ||
                    (ChipDeleted.IsChecked == true && row.Outcome == IntegrityFileOutcome.Deleted);
                if (!match) return false;
            }

            string keyword = FileFilterKeyword.Text;
            if (keyword.Length > 0
                && row.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0
                && row.RelPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            return true;
        }

        private void FileFilterKeyword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => _view?.Refresh();

        private void Chip_Changed(object sender, RoutedEventArgs e)
            => _view?.Refresh();
    }
}
