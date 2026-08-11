using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Core.AudioProjectMutation;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.MusicDatEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.MusicDat;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Test.Audio
{
    /// <summary>
    /// One run of the whole thing, in the order a modder does it: the MusicDat wizard wires a new
    /// culture into both music scripts and writes the audio project holding the events they now
    /// post, then the Audio Editor gives those events audio and compiles them.
    ///
    /// The unit tests all work on hircs in memory, and a bank that is perfectly well formed is still
    /// silent if no script posts its event. What this run adds is the join: the .dat and the .bnk
    /// have to agree on a name, and they end up in one pack together or the mod is half a mod.
    /// It cannot show the game plays it; that still needs a person and a copy of WH3.
    /// </summary>
    [Explicit("End to end run, needs a WH3 install and a wav on disk, run manually")]
    internal class MusicEndToEndRun
    {
        const string GameDirectory = @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III";
        const string WavDiskFilePath = @"C:\Users\rob\Desktop\tw modding\projects\araby audio projects\music\241 Adrian von Ziegler - Hual Hadi.wav";

        // What a modder types into the wizard. The Audio State is also what event names are built
        // from, lower-cased; the subculture key is what campaign_music.dat matches against.
        const string AudioState = "Araby";
        const string SubcultureKey = "ovn_sc_arb_araby";

        const string AudioProjectFolder = @"audio\audio_projects";
        const uint CampaignSubcultureContainerId = 698158058;
        const uint BattleCultureContainerId = 26264058;

        static string OutputDirectory => Path.Combine(Path.GetTempPath(), "ArabyMusicRun");

        [Test]
        public void AddArabyThroughTheWizardThenGiveItAudioAndCompile()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var packFileService = provider.GetRequiredService<IPackFileService>();
            var containerLoader = provider.GetRequiredService<IPackFileContainerLoader>();

            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack" })
            {
                var container = containerLoader.CreateFromPackFile(
                    PackFileContainerType.Normal, Path.Combine(GameDirectory, "data", packName), true);
                container.IsCaPackFile = true;
                packFileService.AddContainer(container);
            }

            // Everything the run produces goes here, the same as the pack a modder has selected for
            // edit while working.
            var modPack = packFileService.CreateNewPackFileContainer(
                "araby_music", PackFileVersion.PFH5, PackFileCAType.MOD, setEditablePack: true);

            var wizard = RunTheWizard(packFileService, provider.GetRequiredService<IFileSaveService>());
            var audioProjectPath = WriteTheAudioProject(provider, wizard);

            var audioFile = ImportTheWav(packFileService, provider.GetRequiredService<IAudioRepository>());
            var compiledProject = GiveEveryEventTheWavAndCompile(provider, audioProjectPath, audioFile);

            ReportPackContents(modPack);
            AssertTheScriptsAndTheBankAgree(packFileService, compiledProject);
            AssertTheMergedContainersKeepVanilla(packFileService, provider.GetRequiredService<IAudioRepository>(), compiledProject);

            Directory.CreateDirectory(OutputDirectory);
            var packDiskPath = Path.Combine(OutputDirectory, "araby_music.pack");
            packFileService.SavePackContainer(modPack, packDiskPath, false,
                GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3));

            Console.WriteLine($"\nPack written to {packDiskPath}");
        }

        /// <summary>
        /// The Add Culture wizard, driven the way the window drives it. Both patched scripts are
        /// committed into the mod pack, which is what the editor does for whichever file is not
        /// open in a tab - and here neither is.
        /// </summary>
        static AddCultureWizardViewModel RunTheWizard(IPackFileService packFileService, IFileSaveService fileSaveService)
        {
            var battlePackFile = FindMusicDat(packFileService, "battle_music.dat");
            var campaignPackFile = FindMusicDat(packFileService, "campaign_music.dat");

            var wizard = new AddCultureWizardViewModel(
                MusicDatParser.Parse(battlePackFile), MusicDatParser.Parse(campaignPackFile))
            {
                AudioState = AudioState,
                SubcultureKey = SubcultureKey
            };

            // Every row left at its default, which is included and authoring its own audio - the
            // most demanding plan the wizard can be given.
            Assert.That(wizard.CanApply, Is.True);
            Assert.That(wizard.TryApply(), Is.True, wizard.Error);

            Commit(packFileService, fileSaveService, battlePackFile, wizard.EditedBattle);
            Commit(packFileService, fileSaveService, campaignPackFile, wizard.EditedCampaign);

            Console.WriteLine($"Wizard wants audio for: {string.Join(", ", wizard.EventsNeedingAudio)}");
            return wizard;
        }

        static void Commit(IPackFileService packFileService, IFileSaveService fileSaveService, PackFile vanillaFile, MusicDatFile edited)
        {
            var path = packFileService.GetFullPath(vanillaFile);
            fileSaveService.Save(path, MusicDatParser.Write(edited), prompOnConflict: false);
        }

        static string WriteTheAudioProject(ServiceProvider provider, AddCultureWizardViewModel wizard)
        {
            var result = provider.GetRequiredService<IMusicAudioProjectService>()
                .CreateForMusicEvents(wizard.AudioProjectName, AudioProjectFolder, wizard.EventsNeedingAudio);

            Assert.That(result.SkippedEvents, Is.Empty,
                string.Join("; ", result.SkippedEvents.Select(skipped => $"{skipped.EventName}: {skipped.Reason}")));
            Assert.That(result.CreatedEvents, Is.EquivalentTo(wizard.EventsNeedingAudio));

            return result.FilePath;
        }

        static AudioFile ImportTheWav(IPackFileService packFileService, IAudioRepository audioRepository)
        {
            // A wav has to be in the pack before it can be picked, the same as importing one in the
            // editor - the wem generator reads it from there, not from disk.
            const string wavPackFileName = "araby_music.wav";
            PackFileUtil.LoadFileFromDisk(packFileService,
                new PackFileUtil.FileRef(WavDiskFilePath, "audio\\wwise", wavPackFileName));

            var wavPackFilePath = $"audio\\wwise\\{wavPackFileName}";
            Assert.That(packFileService.FindFile(wavPackFilePath), Is.Not.Null, "the wav was not added to the mod pack");

            var language = Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx);
            audioRepository.Load([language]);

            var ids = IdGenerator.GenerateIds(audioRepository.GetUsedVanillaSourceIdsByLanguageId(WwiseHash.Compute(language)));
            return new AudioFile(ids.Guid, ids.Id, wavPackFileName, wavPackFilePath);
        }

        /// <summary>
        /// Opens the wizard's audio project and gives every event the same wav, which at the service
        /// level is what editing a row in the viewer does: the row is removed and re-added, this
        /// time with audio picked in the Audio Files Explorer.
        /// </summary>
        static AudioProjectFile GiveEveryEventTheWavAndCompile(ServiceProvider provider, string audioProjectPath, AudioFile audioFile)
        {
            var fileName = Path.GetFileName(audioProjectPath);
            var loaded = provider.GetRequiredService<IAudioProjectFileService>().Load(fileName, audioProjectPath);

            var stateService = provider.GetRequiredService<IAudioEditorStateService>();
            stateService.StoreAudioProject(loaded.AudioProject);
            stateService.StoreAudioProjectFileName(fileName);

            var actionEventService = provider.GetRequiredService<IActionEventService>();
            var musicSoundBank = loaded.AudioProject.SoundBanks.Single(soundBank => soundBank.ActionEvents.Count != 0);
            var actionEventTypeName = Wh3ActionEventInformation.GetName(Wh3ActionEventType.Music);

            foreach (var actionEvent in musicSoundBank.ActionEvents.ToList())
            {
                var setStateAction = actionEvent.Actions.Single();
                actionEventService.RemoveActionEvent(actionEventTypeName, actionEvent.Name);
                actionEventService.AddSetStateActionEvent(actionEventTypeName, actionEvent.Name,
                    setStateAction.StateGroupName, setStateAction.StateName, [audioFile]);
            }

            // The editor compiles the cleaned project, not the live one.
            var compiledProject = loaded.AudioProject.Clean();
            provider.GetRequiredService<IAudioProjectCompilerService>()
                .Compile(compiledProject, fileName, audioProjectPath);

            return compiledProject;
        }

        /// <summary>
        /// The join between the two halves. Every event the scripts now post has to exist as an
        /// Event hirc in a compiled .bnk, or the script posts a name into nothing.
        /// </summary>
        static void AssertTheScriptsAndTheBankAgree(IPackFileService packFileService, AudioProjectFile compiledProject)
        {
            var musicSoundBank = compiledProject.SoundBanks.Single(soundBank => soundBank.ActionEvents.Count != 0);
            var hircs = ReadHircs(packFileService, musicSoundBank.FilePath);

            var battle = MusicDatParser.Parse(packFileService.FindFile(@"audio\scripts\battle_music.dat"));
            var campaign = MusicDatParser.Parse(packFileService.FindFile(@"audio\scripts\campaign_music.dat"));

            var postedEvents = PostedEventsFor(battle, AudioState.ToLowerInvariant())
                .Concat(PostedEventsFor(campaign, SubcultureKey))
                .ToList();

            Assert.That(postedEvents, Is.Not.Empty, "the patched scripts post nothing for the new culture");

            Assert.Multiple(() =>
            {
                foreach (var eventName in postedEvents)
                {
                    var eventHirc = hircs.FirstOrDefault(hirc => hirc.Id == WwiseHash.Compute(eventName));
                    Assert.That(eventHirc, Is.Not.Null, $"the scripts post '{eventName}' but no .bnk defines it");
                    Assert.That(eventHirc.HircType, Is.EqualTo(AkBkHircType.Event));
                }
            });

            Console.WriteLine($"\nScripts post {postedEvents.Count} event(s), all defined in {musicSoundBank.FilePath}:");
            foreach (var eventName in postedEvents)
                Console.WriteLine($"  {eventName} -> {WwiseHash.Compute(eventName)}");
        }

        /// <summary>
        /// The testing .bnks replace the vanilla ones outright, so a branch lost in the merge is not
        /// merely missing - it is a culture that had music and now has none.
        /// </summary>
        static void AssertTheMergedContainersKeepVanilla(
            IPackFileService packFileService, IAudioRepository audioRepository, AudioProjectFile compiledProject)
        {
            var musicSoundBank = compiledProject.SoundBanks.Single(soundBank => soundBank.MusicRandomSequences.Count != 0);
            var containerIds = musicSoundBank.MusicRandomSequences
                .Select(randomSequence => randomSequence.DirectParentId)
                .Distinct()
                .ToList();

            Assert.That(containerIds, Is.EquivalentTo(new[] { CampaignSubcultureContainerId, BattleCultureContainerId }),
                "expected a branch in both the campaign subculture and the battle culture container");

            var testingBankPaths = packFileService.GetEditablePack().GetAllFiles()
                .Select(file => file.Key)
                .Where(path => path.EndsWith("_for_testing.bnk"))
                .ToList();

            var mergedContainers = testingBankPaths
                .SelectMany(path => ReadHircs(packFileService, path))
                .OfType<CAkMusicSwitchCntr_V136>()
                .ToDictionary(container => container.Id);

            Assert.Multiple(() =>
            {
                foreach (var containerId in containerIds)
                {
                    Assert.That(mergedContainers.ContainsKey(containerId), Is.True,
                        $"no testing .bnk carries container {containerId}");

                    var vanilla = (CAkMusicSwitchCntr_V136)audioRepository.GetHircs(AkBkHircType.Music_Switch)
                        .Single(hirc => hirc.Id == containerId && hirc.IsCA);

                    var merged = mergedContainers[containerId];

                    // The tree is only readable against the arguments and depth declared alongside
                    // it, so the copy the merge builds has to carry them over unchanged.
                    Assert.That(merged.TreeDepth, Is.EqualTo(vanilla.TreeDepth));
                    Assert.That(merged.Arguments.Select(argument => argument.GroupId),
                        Is.EqualTo(vanilla.Arguments.Select(argument => argument.GroupId)));

                    AssertVanillaBranchesSurvive(vanilla.AkDecisionTree.DecisionTree, merged.AkDecisionTree.DecisionTree, containerId);
                }
            });

            foreach (var path in testingBankPaths)
                Console.WriteLine($"\n{path}: {string.Join(", ", ReadHircs(packFileService, path).Select(hirc => $"{hirc.HircType} {hirc.Id}"))}");
        }

        /// <summary>Walks both trees together, since a battle branch is a path rather than a single
        /// node and a loss further down is just as silent as one at the top.</summary>
        static void AssertVanillaBranchesSurvive(
            AkDecisionTree_V136.Node_V136 vanillaNode, AkDecisionTree_V136.Node_V136 mergedNode, uint containerId)
        {
            foreach (var vanillaChild in vanillaNode.Nodes)
            {
                var mergedChild = mergedNode.Nodes.SingleOrDefault(node => node.Key == vanillaChild.Key);
                Assert.That(mergedChild, Is.Not.Null, $"container {containerId}: vanilla branch {vanillaChild.Key} was dropped");

                if (vanillaChild.Nodes.Count == 0)
                    Assert.That(mergedChild.AudioNodeId, Is.EqualTo(vanillaChild.AudioNodeId),
                        $"container {containerId}: vanilla branch {vanillaChild.Key} now points somewhere else");
                else
                    AssertVanillaBranchesSurvive(vanillaChild, mergedChild, containerId);
            }
        }

        /// <summary>
        /// Every event name the spliced arms for this key post, read back out of the patched file.
        ///
        /// An arm names its event one of two ways and both have to be read, or this silently checks
        /// a subset: most spell the event out as a literal, while the subculture chain only stores
        /// the culture name and the script concatenates the prefix onto it at run time.
        /// </summary>
        static List<string> PostedEventsFor(MusicDatFile file, string matchKey)
        {
            return MusicDatCultureWiring.FindSlots(file)
                .SelectMany(slot => slot.Chains
                    .SelectMany(chain => chain.Cases)
                    .Where(matchCase => string.Equals(matchCase.MatchKey, matchKey, StringComparison.OrdinalIgnoreCase))
                    .Select(matchCase => matchCase.SoundEvent
                        ?? (matchCase.MusicalCulture == null ? null : slot.EventFor(matchCase.MusicalCulture))))
                .Where(eventName => eventName != null)
                .Distinct()
                .ToList()!;
        }

        static PackFile FindMusicDat(IPackFileService packFileService, string name)
        {
            var packFile = packFileService.FindAllWithExtention(".dat")
                .FirstOrDefault(file => string.Equals(Path.GetFileName(file.FileName), name, StringComparison.OrdinalIgnoreCase))
                .Pack;

            Assert.That(packFile, Is.Not.Null, $"{name} was not found in the loaded packs");
            return packFile;
        }

        static List<HircItem> ReadHircs(IPackFileService packFileService, string packFilePath)
        {
            var packFile = packFileService.FindFile(packFilePath);
            Assert.That(packFile, Is.Not.Null, $"no .bnk at {packFilePath}");

            var bnkFile = BnkFile.CreateFromBytes(packFile.DataSource.ReadData(), packFilePath, false);
            return bnkFile.HircChunk.HircItems;
        }

        static void ReportPackContents(IPackFileContainer modPack)
        {
            Console.WriteLine("\nPack contents:");
            foreach (var file in modPack.GetAllFiles().OrderBy(file => file.Key))
                Console.WriteLine($"  {file.Key} ({file.Value.DataSource.Size} bytes)");
        }
    }
}
