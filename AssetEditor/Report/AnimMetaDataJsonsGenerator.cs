﻿using AnimationMeta.FileTypes.Parsing;
using CommonControls.Common;
using CommonControls.Editors.AnimationPack.Converters;
using CommonControls.FileTypes.Animation;
using CommonControls.FileTypes.AnimationPack;
using CommonControls.FileTypes.AnimationPack.AnimPackFileTypes.Wh3;
using CommonControls.FileTypes.PackFiles.Models;
using CommonControls.Services;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AssetEditor.Report
{
    class AnimMetaDataJsonsGenerator
    {
        ILogger _logger = Logging.Create<AnimMetaDataJsonsGenerator>();
        PackFileService _pfs;
        ApplicationSettingsService _settingsService;
        private readonly GameInformationFactory _gameInformationFactory;
        JsonSerializerSettings _jsonOptions;

        public AnimMetaDataJsonsGenerator(PackFileService pfs, ApplicationSettingsService settingsService, GameInformationFactory gameInformationFactory)
        {
            _pfs = pfs;
            _settingsService = settingsService;
            _gameInformationFactory = gameInformationFactory;
            _jsonOptions = new JsonSerializerSettings { Formatting = Formatting.Indented };
        }

        public static void Generate(PackFileService pfs, ApplicationSettingsService settingsService, GameInformationFactory gameInformationFactory)
        {
            var instance = new AnimMetaDataJsonsGenerator(pfs, settingsService, gameInformationFactory);
            instance.Create();
        }

        void dumpAsJson(string gameOutputDir, string fileName, object data)
        {
            string jsonString = JsonConvert.SerializeObject(data, _jsonOptions);
            string json_filepath = Path.Join(gameOutputDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(json_filepath));
            File.WriteAllText(json_filepath, jsonString);
        }

        public void Create()
        {
            var gameName = _gameInformationFactory.GetGameById(_settingsService.CurrentSettings.CurrentGame).DisplayName;
            var timeStamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var gameOutputDir = $"{DirectoryHelper.ReportsDirectory}\\MetaDataJsons\\{gameName}_{timeStamp}\\";
            if (Directory.Exists(gameOutputDir))
                Directory.Delete(gameOutputDir, true);
            DirectoryHelper.EnsureCreated(gameOutputDir);

            //dump animtable
            PackFile animPack = _pfs.Database.PackFiles[0].FileList["animations\\database\\battle\\bin\\animation_tables.animpack"];
            AnimationPackFile animPackFile = AnimationPackSerializer.Load(animPack, _pfs);

            var converter = new AnimationBinWh3FileToXmlConverter(new SkeletonAnimationLookUpHelper());
            foreach (var animFile in animPackFile.Files)
            {
                if (animFile is AnimationBinWh3)
                {
                    string text = converter.GetText(animFile.ToByteArray());
                    string xml_filepath = Path.Join(gameOutputDir, animFile.FileName + ".xml");
                    Directory.CreateDirectory(Path.GetDirectoryName(xml_filepath));
                    File.WriteAllText(xml_filepath, text);
                }
            }

            var allMeta = _pfs.FindAllWithExtentionIncludePaths(".meta");
            foreach (var (fileName, packFile) in allMeta)
            {
                try
                {
                    var data = packFile.DataSource.ReadData();
                    if (data.Length == 0)
                        continue;

                    var parser = new MetaDataFileParser();
                    var metaData = parser.ParseFile(data);
                    dumpAsJson(gameOutputDir, fileName + ".json", metaData);
                }
                catch (Exception e)
                {
                    _logger.Here().Information($"Meta parsing failed {fileName} - {e.Message}");
                }
            }

            var allAnimations = _pfs.FindAllWithExtentionIncludePaths(".anim");
            foreach (var (fileName, packFile) in allAnimations)
            {
                try
                {
                    var animationHeader = AnimationFile.GetAnimationHeader(packFile);
                    dumpAsJson(gameOutputDir, fileName + ".header.json", animationHeader);
                }
                catch (Exception e)
                {
                    _logger.Here().Information($"Animation parsing failed {fileName} - {e.Message}");
                }
            }

            MessageBox.Show($"Done - Created at {gameOutputDir}");
            Process.Start("explorer.exe", gameOutputDir);
        }
    }
}
