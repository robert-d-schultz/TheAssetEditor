using System.Windows;
using System.Windows.Input;

namespace CommonControls.BaseDialogs
{
    public partial class NewPackFileWindow : Window
    {
        public string PackName => string.IsNullOrWhiteSpace(SelectedFolderPath) ? string.Empty : System.IO.Path.GetFileName(SelectedFolderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        public string? SelectedFolderPath { get; private set; }
        public string? SelectedOutputFolderPath { get; private set; }

        public NewPackFileWindow()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
        }

        private void BrowseProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder to use as a packfile project",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SelectedFolderPath = dialog.SelectedPath;
                ProjectFolderTextBox.Text = SelectedFolderPath;

                SelectedOutputFolderPath = SelectedFolderPath;
                OutputFolderTextBox.Text = SelectedOutputFolderPath;
            }
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder where the generated packfile will be saved",
                UseDescriptionForTitle = true,
                SelectedPath = SelectedOutputFolderPath ?? SelectedFolderPath ?? string.Empty
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SelectedOutputFolderPath = dialog.SelectedPath;
                OutputFolderTextBox.Text = SelectedOutputFolderPath;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                MessageBox.Show("No project folder was selected", "Error");
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedOutputFolderPath))
            {
                MessageBox.Show("No output folder was selected", "Error");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Key_Down(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
