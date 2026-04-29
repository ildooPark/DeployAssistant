using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for ErrorLogWindow.xaml
    /// </summary>
    public partial class ErrorLogWindow : Window
    {
        public ErrorLogWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        /// <summary>
        /// The log text displayed in this window.
        /// Callers may append to this before or after <see cref="Show"/>.
        /// </summary>
        public string LogText
        {
            get => (string)GetValue(LogTextProperty);
            set => SetValue(LogTextProperty, value);
        }

        public static readonly DependencyProperty LogTextProperty =
            DependencyProperty.Register(nameof(LogText), typeof(string), typeof(ErrorLogWindow),
                new PropertyMetadata(string.Empty));

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(LogTextBox.Text))
                System.Windows.Clipboard.SetText(LogTextBox.Text);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            LogText = string.Empty;
        }
    }
}
