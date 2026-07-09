using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace CommonControls.BaseDialogs
{
    public partial class NewPackFileWindow : Window
    {
        private const string ProjectSettingsFileName = "aeproject.json";

        public string PackName => string.IsNullOrWhiteSpace(SelectedFolderPath) ? string.Empty : System.IO.Path.GetFileName(SelectedFolderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        public string? SelectedFolderPath { get; private set; }
        public string? SelectedOutputFolderPath { get; private set; }
        public bool EnablePackFileCorruptionDetection => EnablePackFileCorruptionDetectionCheckBox.IsChecked == true;

        public NewPackFileWindow()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
        }

        private void BrowseProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = GetLocalizedString("NewPackFileWindow.SelectProjectFolderDescription"),
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dialog.SelectedPath, ProjectSettingsFileName)))
                {
                    MessageBox.Show(GetLocalizedString("NewPackFileWindow.ProjectAlreadyExists"), GetLocalizedString("NewPackFileWindow.ErrorTitle"));
                    return;
                }

                SelectedFolderPath = dialog.SelectedPath;
                ProjectFolderTextBox.Text = SelectedFolderPath;

                SelectedOutputFolderPath = System.IO.Directory.GetParent(SelectedFolderPath)?.FullName ?? SelectedFolderPath;
                OutputFolderTextBox.Text = SelectedOutputFolderPath;
            }
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = GetLocalizedString("NewPackFileWindow.SelectOutputFolderDescription"),
                UseDescriptionForTitle = true,
                SelectedPath = SelectedOutputFolderPath ?? SelectedFolderPath ?? string.Empty
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SelectedOutputFolderPath = dialog.SelectedPath;
                OutputFolderTextBox.Text = SelectedOutputFolderPath;

                if (IsSameFolder(SelectedFolderPath, SelectedOutputFolderPath))
                    MessageBox.Show(GetLocalizedString("NewPackFileWindow.OutputFolderSameAsProjectWarning"), GetLocalizedString("NewPackFileWindow.WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                MessageBox.Show(GetLocalizedString("NewPackFileWindow.NoProjectFolderSelected"), GetLocalizedString("NewPackFileWindow.ErrorTitle"));
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedOutputFolderPath))
            {
                MessageBox.Show(GetLocalizedString("NewPackFileWindow.NoOutputFolderSelected"), GetLocalizedString("NewPackFileWindow.ErrorTitle"));
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

        private static string GetLocalizedString(string key)
        {
            if (Application.Current is IAssetEditorMain appMain)
                return appMain.ServiceProvider.GetRequiredService<LocalizationManager>().Get(key);

            return key;
        }

        private static bool IsSameFolder(string? firstFolderPath, string? secondFolderPath)
        {
            if (string.IsNullOrWhiteSpace(firstFolderPath) || string.IsNullOrWhiteSpace(secondFolderPath))
                return false;

            var firstFullPath = System.IO.Path.GetFullPath(firstFolderPath).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var secondFullPath = System.IO.Path.GetFullPath(secondFolderPath).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

            return string.Equals(firstFullPath, secondFullPath, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
