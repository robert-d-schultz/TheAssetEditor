using System.IO;
using System.Windows.Forms;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.UiCommands
{
    public class ImportPackAsAsProjectCommand : IAeCommand
    {
        private readonly IPackFileService _packFileService;
        private readonly IPackFileContainerLoader _packFileContainerLoader;
        private readonly ISystemFolderContainerFactory _systemFolderContainerFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ApplicationSettingsService _applicationSettingsService;

        public ImportPackAsAsProjectCommand(
            IPackFileService packFileService,
            IPackFileContainerLoader packFileContainerLoader,
            ISystemFolderContainerFactory systemFolderContainerFactory,
            IStandardDialogs standardDialogs,
            ApplicationSettingsService applicationSettingsService)
        {
            _packFileService = packFileService;
            _packFileContainerLoader = packFileContainerLoader;
            _systemFolderContainerFactory = systemFolderContainerFactory;
            _standardDialogs = standardDialogs;
            _applicationSettingsService = applicationSettingsService;
        }

        public void Execute()
        {
            var packDialog = _standardDialogs.ShowSystemOpenFileDialog(filter: "Pack files (*.pack)|*.pack|All files (*.*)|*.*");
            if (!packDialog.Result || packDialog.FilePaths.Count == 0)
                return;

            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select where the mod project folder and output pack should be created",
                UseDescriptionForTitle = true
            };

            if (folderDialog.ShowDialog() != DialogResult.OK)
                return;

            var packFilePath = packDialog.FilePaths[0];
            var modName = Path.GetFileNameWithoutExtension(packFilePath);
            var destinationFolder = Path.Combine(folderDialog.SelectedPath, modName);
            var outputPackPath = Path.Combine(folderDialog.SelectedPath, $"{modName}.pack");
            Directory.CreateDirectory(destinationFolder);

            var packContainer = _packFileContainerLoader.CreateFromPackFile(PackFileContainerType.Normal, packFilePath, false);
            var allFiles = packContainer.GetAllFiles();

            foreach (var (relativePath, packFile) in allFiles)
            {
                var absolutePath = Path.Combine(destinationFolder, relativePath);
                var directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var data = packFile.DataSource.ReadData();
                File.WriteAllBytes(absolutePath, data);
            }

            var systemContainer = _systemFolderContainerFactory.Create(destinationFolder);
            if (systemContainer.PackFileSettings.GameVersion == null)
                systemContainer.PackFileSettings.GameVersion = _applicationSettingsService.CurrentSettings.CurrentGame;

            systemContainer.PackFileSettings.SaveLocationPath = outputPackPath;
            systemContainer.SaveSettings();
            _packFileService.AddContainer(systemContainer);
            _packFileService.SetEditablePack(systemContainer);

        }
    }
}
