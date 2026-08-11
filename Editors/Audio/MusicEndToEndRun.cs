using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Core.AudioProjectMutation;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
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
    /// The whole music path end to end, driven through the same services the Audio Editor uses:
    /// pick a wav, add a music Action Event for a new subculture, compile, and read the .bnks back
    /// out of the pack that comes with it.
    ///
    /// The unit tests all work on hircs in memory. This is the only thing that shows the pieces fit
    /// together - that the branch the merge writes actually points at a random sequence that exists
    /// in another .bnk, that the wem is where the track says it is, and that the .bnks re-parse.
    /// It cannot show the game plays it; that still needs a person and a copy of WH3.
    /// </summary>
    [Explicit("End to end run, needs a WH3 install and a wav on disk, run manually")]
    internal class MusicEndToEndRun
    {
        const string GameDirectory = @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III";
        const string WavDiskFilePath = @"C:\Users\rob\Desktop\tw modding\projects\araby audio projects\music\241 Adrian von Ziegler - Hual Hadi.wav";

        const string AudioProjectName = "araby_music";
        const string StateName = "Araby";

        // The wizard lower-cases the Audio State before building event names off it, since that is
        // what the shipped scripts do. The State itself is case insensitive - Wwise hashes it - but
        // the event name is not.
        const string MusicalCulture = "araby";
        const string SubcultureKey = "ovn_sc_arb_araby";

        const string StateGroupName = "WH3_Campaign_Subcultures";
        const string ActionEventName = "music_c_subculture_araby";
        const uint CampaignSubcultureContainerId = 698158058;

        static string OutputDirectory => Path.Combine(Path.GetTempPath(), "ArabyMusicRun");

        [Test]
        public void CompileAnArabyCampaignMusicProject()
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

            // The mod pack everything is written into. The compiler saves through IFileSaveService,
            // which writes to whichever pack is selected for edit.
            var modPack = packFileService.CreateNewPackFileContainer(
                AudioProjectName, PackFileVersion.PFH5, PackFileCAType.MOD, setEditablePack: true);

            // A wav has to be in the pack before it can be picked, the same as importing one in the
            // editor - the wem generator reads it from there, not from disk.
            var wavPackFileName = $"{AudioProjectName}.wav";
            PackFileUtil.LoadFileFromDisk(packFileService,
                new PackFileUtil.FileRef(WavDiskFilePath, "audio\\wwise", wavPackFileName));
            var wavPackFilePath = $"audio\\wwise\\{wavPackFileName}";
            Assert.That(packFileService.FindFile(wavPackFilePath), Is.Not.Null, "the wav was not added to the mod pack");

            var audioRepository = provider.GetRequiredService<IAudioRepository>();
            var language = Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx);
            audioRepository.Load([language]);

            var stateService = provider.GetRequiredService<IAudioEditorStateService>();
            var audioProject = AudioProjectFile.CreateNew(language, AudioProjectName);
            stateService.StoreAudioProject(audioProject);
            stateService.StoreAudioProjectFileName($"{AudioProjectName}.aproj");

            var audioFileIds = IdGenerator.GenerateIds(IdGenerator.GetUsedSourceIds(audioRepository, audioProject));
            var audioFile = new AudioFile(audioFileIds.Guid, audioFileIds.Id, wavPackFileName, wavPackFilePath);

            provider.GetRequiredService<IActionEventService>()
                .AddSetStateActionEvent("Music", ActionEventName, StateGroupName, StateName, [audioFile]);

            // A new project is created holding every SoundBank and Dialogue Event the game has, and
            // it is the cleaned copy that gets compiled - the same as pressing Compile in the editor.
            var compiledProject = audioProject.Clean();
            var musicSoundBank = compiledProject.SoundBanks
                .Single(soundBank => soundBank.MusicRandomSequences.Count != 0);

            Assert.Multiple(() =>
            {
                Assert.That(musicSoundBank.MusicRandomSequences, Has.Count.EqualTo(1));
                Assert.That(musicSoundBank.MusicSegments, Has.Count.EqualTo(1));
                Assert.That(musicSoundBank.MusicRandomSequences[0].DirectParentId, Is.EqualTo(CampaignSubcultureContainerId));
                Assert.That(musicSoundBank.GetActionEvent(ActionEventName), Is.Not.Null);
            });

            provider.GetRequiredService<IAudioProjectCompilerService>()
                .Compile(compiledProject, $"{AudioProjectName}.aproj", OutputDirectory);

            ReportPackContents(modPack);
            var randomSequenceId = musicSoundBank.MusicRandomSequences[0].Id;
            var segment = musicSoundBank.MusicSegments[0];

            AssertTheModsOwnSoundBankCarriesTheHierarchy(packFileService, musicSoundBank, randomSequenceId, segment, audioFile);
            AssertTheTestingSoundBankSelectsTheNewState(packFileService, audioRepository, randomSequenceId);
            AssertTheMergingSoundBankCarriesOnlyTheModsBranch(packFileService, musicSoundBank, randomSequenceId);
            AssertTheWemIsInThePack(packFileService, audioFile, segment);

            Directory.CreateDirectory(OutputDirectory);
            var packDiskPath = Path.Combine(OutputDirectory, $"{AudioProjectName}.pack");
            packFileService.SavePackContainer(modPack, packDiskPath, false,
                GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3));

            Console.WriteLine($"\nPack written to {packDiskPath}");
        }

        /// <summary>
        /// The other half of the same run: the script that posts the event. The audio project is
        /// only reachable if campaign_music.dat has an arm matching the new subculture and posting
        /// the event name the SoundBank defines, so the two halves are checked against each other
        /// rather than each against what it was asked for.
        /// </summary>
        [Test]
        public void WireArabyIntoTheMusicScripts()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var packFileService = provider.GetRequiredService<IPackFileService>();
            var containerLoader = provider.GetRequiredService<IPackFileContainerLoader>();

            // The music scripts ship alongside the banks rather than with the rest of the audio data.
            var container = containerLoader.CreateFromPackFile(
                PackFileContainerType.Normal, Path.Combine(GameDirectory, "data", "audio_base_bnk.pack"), true);
            container.IsCaPackFile = true;
            packFileService.AddContainer(container);

            var campaign = ParseMusicDat(container, "campaign_music.dat");
            var battle = ParseMusicDat(container, "battle_music.dat");

            // What the wizard sends: the campaign script matches the full subculture key, the battle
            // script the short Audio State, and both build event names off the Audio State.
            var campaignProblems = AddCulture(campaign, SubcultureKey, MusicalCulture);
            var battleProblems = AddCulture(battle, MusicalCulture, MusicalCulture);

            Assert.Multiple(() =>
            {
                Assert.That(campaignProblems, Is.Empty);
                Assert.That(battleProblems, Is.Empty);
            });

            var campaignEvents = PostedEventsFor(campaign, SubcultureKey);
            var battleEvents = PostedEventsFor(battle, MusicalCulture);

            Assert.Multiple(() =>
            {
                // The join: this is the event the compiled SoundBank defines.
                Assert.That(campaignEvents, Does.Contain(ActionEventName));
                Assert.That(battleEvents, Does.Contain($"music_b_faction_{MusicalCulture}"));

                // The patched files have to survive being written and read back, or the game gets a
                // script it cannot run.
                Assert.That(MusicDatParser.Write(campaign), Is.Not.Empty);
                Assert.That(MusicDatParser.Write(battle), Is.Not.Empty);
            });

            Console.WriteLine($"\ncampaign_music.dat '{SubcultureKey}' posts: {string.Join(", ", campaignEvents)}");
            Console.WriteLine($"battle_music.dat '{MusicalCulture}' posts: {string.Join(", ", battleEvents)}");
        }

        static IReadOnlyList<string> AddCulture(MusicDatFile file, string matchKey, string musicalCulture)
        {
            var plans = MusicDatCultureWiring.FindSlots(file)
                .Select(slot => new MusicDatCultureWiring.SlotPlan(slot.EventPrefix, true, null))
                .ToList();

            return MusicDatCultureWiring.AddCulture(file, matchKey, musicalCulture, plans);
        }

        /// <summary>Every event name the spliced arms for this key post, read back out of the
        /// patched file rather than predicted from what was asked for.</summary>
        static List<string> PostedEventsFor(MusicDatFile file, string matchKey)
        {
            return MusicDatCultureWiring.FindSlots(file)
                .SelectMany(slot => slot.Chains
                    .SelectMany(chain => chain.Cases)
                    .Where(matchCase => string.Equals(matchCase.MatchKey, matchKey, StringComparison.OrdinalIgnoreCase)
                        && matchCase.MusicalCulture != null)
                    .Select(matchCase => slot.EventFor(matchCase.MusicalCulture!)))
                .Distinct()
                .ToList();
        }

        static MusicDatFile ParseMusicDat(IPackFileContainer container, string fileName)
        {
            var entry = container.GetAllFiles()
                .Single(file => file.Key.EndsWith($"\\{fileName}", StringComparison.OrdinalIgnoreCase));

            return MusicDatParser.Parse(entry.Value);
        }

        /// <summary>
        /// The .bnk the modder keeps. The hierarchy the State selects lives here rather than in the
        /// merged .bnks, because none of it shares an id with vanilla.
        /// </summary>
        static void AssertTheModsOwnSoundBankCarriesTheHierarchy(
            IPackFileService packFileService, SoundBank soundBank, uint randomSequenceId, MusicSegment segment, AudioFile audioFile)
        {
            var hircs = ReadHircs(packFileService, soundBank.FilePath);

            var randomSequence = (CAkMusicRanSeqCntr_V136)hircs.Single(hirc => hirc.Id == randomSequenceId);
            var musicSegment = (CAkMusicSegment_V136)hircs.Single(hirc => hirc.Id == segment.Id);
            var musicTrack = (CAkMusicTrack_V136)hircs.Single(hirc => hirc.Id == segment.TrackId);
            var actionEvent = hircs.Single(hirc => hirc.Id == WwiseHash.Compute(ActionEventName));

            Assert.Multiple(() =>
            {
                Assert.That(actionEvent.HircType, Is.EqualTo(AkBkHircType.Event));

                // The chain the State has to walk down: container -> sequence -> segment -> track ->
                // the wem. A break anywhere in it is silence rather than an error.
                Assert.That(randomSequence.MusicTransNodeParams.MusicNodeParams.NodeBaseParams.DirectParentId,
                    Is.EqualTo(CampaignSubcultureContainerId));
                Assert.That(randomSequence.MusicTransNodeParams.MusicNodeParams.Children.ChildIds, Does.Contain(segment.Id));
                Assert.That(musicSegment.MusicNodeParams.Children.ChildIds, Does.Contain(segment.TrackId));
                Assert.That(musicTrack.SourceList.Single().AkMediaInformation.SourceId, Is.EqualTo(audioFile.Id));

                // The segment is cut to the audio; a wrong duration desynchronises it silently.
                Assert.That(musicSegment.Duration, Is.GreaterThan(0));
                Assert.That(musicTrack.PlaylistList.Single().SrcDuration, Is.EqualTo(musicSegment.Duration));
            });

            // Nothing here may share an id with vanilla, which is the whole reason the Music Switch
            // container is not in this .bnk.
            Assert.That(hircs.Any(hirc => hirc.Id == CampaignSubcultureContainerId), Is.False,
                "the vanilla Music Switch container must not ship in the mod's own .bnk");

            Console.WriteLine($"\n{soundBank.FilePath}");
            foreach (var hirc in hircs)
                Console.WriteLine($"  {hirc.HircType} {hirc.Id}");
        }

        /// <summary>
        /// The .bnk that overrides the vanilla one so the project can be played on its own. It holds
        /// the vanilla decision tree with the mod's branch merged in.
        /// </summary>
        static void AssertTheTestingSoundBankSelectsTheNewState(
            IPackFileService packFileService, IAudioRepository audioRepository, uint randomSequenceId)
        {
            var testingFilePath = FindSoundBank(packFileService, "_1_", "_for_testing.bnk");
            var container = (CAkMusicSwitchCntr_V136)ReadHircs(packFileService, testingFilePath)
                .Single(hirc => hirc.Id == CampaignSubcultureContainerId);

            var vanillaContainer = (CAkMusicSwitchCntr_V136)audioRepository.GetHircs(AkBkHircType.Music_Switch)
                .Single(hirc => hirc.Id == CampaignSubcultureContainerId && hirc.IsCA);

            var branches = container.AkDecisionTree.DecisionTree.Nodes;
            var arabyBranch = branches.SingleOrDefault(node => node.Key == WwiseHash.Compute(StateName));

            Assert.Multiple(() =>
            {
                Assert.That(arabyBranch, Is.Not.Null, $"no branch for '{StateName}' in the merged container");
                Assert.That(arabyBranch.AudioNodeId, Is.EqualTo(randomSequenceId));

                // This .bnk replaces the vanilla one outright, so anything the merge loses is not
                // merely missing - it is a subculture that had music and now has none.
                foreach (var vanillaBranch in vanillaContainer.AkDecisionTree.DecisionTree.Nodes)
                {
                    var mergedBranch = branches.SingleOrDefault(node => node.Key == vanillaBranch.Key);
                    Assert.That(mergedBranch, Is.Not.Null, $"vanilla branch {vanillaBranch.Key} was dropped");
                    Assert.That(mergedBranch.AudioNodeId, Is.EqualTo(vanillaBranch.AudioNodeId),
                        $"vanilla branch {vanillaBranch.Key} now points somewhere else");
                }

                Assert.That(branches, Has.Count.EqualTo(vanillaContainer.AkDecisionTree.DecisionTree.Nodes.Count + 1));

                // The tree is only readable against the arguments and depth declared alongside it,
                // so the copy the merge builds has to carry them over unchanged.
                Assert.That(container.TreeDepth, Is.EqualTo(vanillaContainer.TreeDepth));
                Assert.That(container.Arguments.Select(argument => argument.GroupId),
                    Is.EqualTo(vanillaContainer.Arguments.Select(argument => argument.GroupId)));
            });

            Console.WriteLine($"\n{testingFilePath}: {branches.Count} branches " +
                $"({vanillaContainer.AkDecisionTree.DecisionTree.Nodes.Count} vanilla, intact), '{StateName}' -> {arabyBranch.AudioNodeId}");
        }

        /// <summary>
        /// What the modder hands to the merger. Vanilla is deliberately absent - the merger folds it
        /// in itself, after combining every mod it was given.
        /// </summary>
        static void AssertTheMergingSoundBankCarriesOnlyTheModsBranch(
            IPackFileService packFileService, SoundBank soundBank, uint randomSequenceId)
        {
            var container = (CAkMusicSwitchCntr_V136)ReadHircs(packFileService, soundBank.MergingFilePath)
                .Single(hirc => hirc.Id == CampaignSubcultureContainerId);

            var branch = container.AkDecisionTree.DecisionTree.Nodes.Single();

            Assert.Multiple(() =>
            {
                Assert.That(branch.Key, Is.EqualTo(WwiseHash.Compute(StateName)));
                Assert.That(branch.AudioNodeId, Is.EqualTo(randomSequenceId));
            });
        }

        static void AssertTheWemIsInThePack(IPackFileService packFileService, AudioFile audioFile, MusicSegment segment)
        {
            var wem = packFileService.FindFile(audioFile.WemPackFilePath);
            Assert.That(wem, Is.Not.Null, $"no wem at {audioFile.WemPackFilePath}");

            // The segment tells the game how much to stream; if it disagrees with the file the tail
            // is cut off or the branch waits on audio that never arrives.
            Assert.That(wem.DataSource.Size, Is.EqualTo(segment.InMemoryMediaSize));

            Console.WriteLine($"\n{audioFile.WemPackFilePath}: {wem.DataSource.Size} bytes, {segment.DurationMs / 1000:0.0}s");
        }

        static string FindSoundBank(IPackFileService packFileService, string nameContains, string nameEndsWith)
        {
            var matches = packFileService.GetEditablePack().GetAllFiles()
                .Select(file => file.Key)
                .Where(path => path.Contains(nameContains) && path.EndsWith(nameEndsWith))
                .ToList();

            Assert.That(matches, Has.Count.EqualTo(1), $"expected one '{nameContains}...{nameEndsWith}' .bnk, found {matches.Count}");
            return matches[0];
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
