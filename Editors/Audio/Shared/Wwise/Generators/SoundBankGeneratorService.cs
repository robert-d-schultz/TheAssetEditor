using System.Data;
using System.IO;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.Generators.Bkhd;
using Editors.Audio.Shared.Wwise.Generators.Hirc;
using Editors.Audio.Shared.Wwise.Generators.Hirc.V136;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Bkhd;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators
{
    public interface ISoundBankGeneratorService
    {
        void GenerateSoundBankWithoutMergedHircs(SoundBank soundBank);
        void GenerateDialogueEventsForTestingSoundBank(SoundBank soundBank);
        void GenerateMusicSwitchContainersForTestingSoundBanks(SoundBank soundBank);
        void GenerateMergingSoundBank(SoundBank soundBank);
        void GenerateMergedDialogueEventSoundBanks(List<string> moddedSoundBanks, string soundBankSuffix);
        void GenerateMergedMusicSoundBanks(List<string> moddedSoundBanks, string soundBankSuffix);
    }

    public class SoundBankGeneratorService : ISoundBankGeneratorService
    {
        private readonly IFileSaveService _fileSaveService;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IAudioRepository _audioRepository;
        private readonly HircGeneratorServiceFactory _hircGeneratorServiceFactory;
        private readonly IAudioEditorIntegrityService _audioEditorIntegrityService;
        private readonly IMusicSwitchContainerMergeService _musicSwitchContainerMergeService;

        private readonly ILogger _logger = Logging.Create<SoundBankGeneratorService>();

        public SoundBankGeneratorService(
            IFileSaveService fileSaveService,
            ApplicationSettingsService applicationSettingsService,
            IAudioRepository audioRepository,
            IAudioEditorIntegrityService audioEditorIntegrityService,
            IMusicSwitchContainerMergeService musicSwitchContainerMergeService)
        {
            _fileSaveService = fileSaveService;
            _applicationSettingsService = applicationSettingsService;
            _audioRepository = audioRepository;
            _audioEditorIntegrityService = audioEditorIntegrityService;
            _musicSwitchContainerMergeService = musicSwitchContainerMergeService;

            var bankGeneratorVersion = (uint)GameInformationDatabase.GetGameById(_applicationSettingsService.CurrentSettings.CurrentGame).BankGeneratorVersion;
            _hircGeneratorServiceFactory = HircGeneratorServiceFactory.CreateFactory(bankGeneratorVersion);
        }

        /// <summary>
        /// The .bnk the modder keeps. It leaves out everything that shares an id with vanilla -
        /// Dialogue Events and Music Switch containers - because those cannot simply be shipped
        /// alongside vanilla, they have to be merged into it. They live in the testing and merging
        /// .bnks instead.
        /// </summary>
        public void GenerateSoundBankWithoutMergedHircs(SoundBank soundBank)
        {
            var actionEventToHircLookup = new Dictionary<ActionEvent, HircItem>();
            var hircItems = new List<HircItem>();

            if (soundBank.ActionEvents.Count != 0)
            {
                var actionEventHircs = GenerateActionEventHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionEventHircs);

                var actionHircs = GenerateActionHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionHircs);

                var sourceHircsFromPlay = GenerateSourceHircsFromPlayActions(soundBank);
                hircItems.AddRange(sourceHircsFromPlay);
            }

            if (soundBank.DialogueEvents.Count != 0)
            {
                // We don't generate the Dialogue Events in this .bnk, we keep them in the merging SoundBank instead
                var sourceHircsFromDialogue = GenerateSourceHircsFromDialogueEvents(soundBank);
                hircItems.AddRange(sourceHircsFromDialogue);
            }

            SortHircs(hircItems);

            // Appended after sorting rather than run through it. SortHircs orders Play targets and
            // Events, and music is neither - it is reached through the decision tree, so it has its
            // own ordering: tracks before their segments, segments before the sequence over them.
            hircItems.AddRange(GenerateMusicHierarchyHircs(soundBank));

            WriteSoundBank(soundBank.Id, soundBank.LanguageId, soundBank.FileName, soundBank.FilePath, hircItems);
        }

        /// <summary>
        /// The music hierarchy this bank contributes. These are all new ids so they can ship as they
        /// are - it is only the Music Switch container above them that clashes with vanilla.
        /// </summary>
        private List<HircItem> GenerateMusicHierarchyHircs(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            foreach (var musicRandomSequence in soundBank.MusicRandomSequences)
            {
                foreach (var musicSegment in soundBank.GetMusicSegments(musicRandomSequence))
                {
                    hircItems.Add(new CAkMusicTrackGenerator_V136().GenerateHirc(musicSegment));
                    hircItems.Add(_hircGeneratorServiceFactory.GenerateHirc(musicSegment));
                }

                hircItems.Add(_hircGeneratorServiceFactory.GenerateHirc(musicRandomSequence));
            }

            return hircItems;
        }

        /// <summary>
        /// The branches the mod adds, grouped by the vanilla Music Switch container they belong in.
        /// One container per State Group.
        /// </summary>
        private static Dictionary<uint, List<MusicBranch>> GetMusicBranchesByContainerId(SoundBank soundBank)
        {
            var branchesByContainerId = new Dictionary<uint, List<MusicBranch>>();

            foreach (var musicRandomSequence in soundBank.MusicRandomSequences)
            {
                var containerId = musicRandomSequence.DirectParentId;
                if (!branchesByContainerId.TryGetValue(containerId, out var branches))
                    branchesByContainerId[containerId] = branches = [];

                branches.Add(new MusicBranch(musicRandomSequence.StateName, musicRandomSequence.Id));
            }

            return branchesByContainerId;
        }

        private CAkMusicSwitchCntr_V136 GetVanillaMusicSwitchContainer(uint containerId)
        {
            var vanillaContainer = _audioRepository.GetHircs(AkBkHircType.Music_Switch)
                .FirstOrDefault(hircItem => hircItem.Id == containerId && hircItem.IsCA) as CAkMusicSwitchCntr_V136;

            if (vanillaContainer == null)
                _logger.Here().Error($"Music Switch container {containerId} was not found in the vanilla data, so its branches cannot be merged");

            return vanillaContainer;
        }

        /// <summary>
        /// One .bnk per vanilla .bnk the mod merges into, holding that .bnk's Music Switch containers
        /// with vanilla merged into the mod's branches so a modder can play the project on its own.
        ///
        /// A single project can touch containers from more than one vanilla .bnk - campaign music and
        /// battle music are separate - and each testing .bnk has to be named after the .bnk it is
        /// overriding to win the load order, so they are grouped by that rather than written as one.
        /// </summary>
        public void GenerateMusicSwitchContainersForTestingSoundBanks(SoundBank soundBank)
        {
            var containersByVanillaSoundBankName = new Dictionary<string, List<HircItem>>();

            foreach (var (containerId, branches) in GetMusicBranchesByContainerId(soundBank))
            {
                var vanillaContainer = GetVanillaMusicSwitchContainer(containerId);
                if (vanillaContainer == null)
                    continue;

                var soundBankNameBase = GetSoundBankNameBase(vanillaContainer.BnkFilePath);
                if (!containersByVanillaSoundBankName.TryGetValue(soundBankNameBase, out var containers))
                    containersByVanillaSoundBankName[soundBankNameBase] = containers = [];

                containers.Add(_musicSwitchContainerMergeService.MergeBranches(vanillaContainer, branches));
            }

            foreach (var (soundBankNameBase, containers) in containersByVanillaSoundBankName)
            {
                // The same naming the Dialogue Event testing .bnk uses, for the same reason: '_1_'
                // sorts ahead of the vanilla '__core', which puts it last in the load order and so
                // lets it override.
                var fileName = $"{soundBankNameBase}_1_{soundBank.AudioProjectName}_for_testing.bnk";
                var filePath = GetSoundBankFilePath(fileName, soundBank.Language);
                var id = WwiseHash.Compute(Path.GetFileNameWithoutExtension(fileName));

                _logger.Here().Information($"Generating SoundBank {filePath}");
                WriteSoundBank(id, soundBank.LanguageId, fileName, filePath, containers);
            }
        }

        /// <summary>
        /// The Music Switch containers carrying only the mod's own branches, for the merging .bnk.
        /// Vanilla is deliberately left out: the merger folds it in once, after it has combined the
        /// branches from every mod it was given.
        /// </summary>
        private List<HircItem> GenerateModdedMusicSwitchContainers(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            foreach (var (containerId, branches) in GetMusicBranchesByContainerId(soundBank))
            {
                var vanillaContainer = GetVanillaMusicSwitchContainer(containerId);
                if (vanillaContainer == null)
                    continue;

                hircItems.Add(_musicSwitchContainerMergeService.CreateModdedContainer(vanillaContainer, branches));
            }

            return hircItems;
        }

        /// <summary>
        /// The name a modded .bnk has to be built on to override the vanilla one, taken from the
        /// vanilla .bnk the hirc was read out of. Vanilla names its .bnks '{base}__core', so the base
        /// is what comes before the double underscore.
        /// </summary>
        private static string GetSoundBankNameBase(string bnkFilePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(bnkFilePath);
            var separatorIndex = fileName.IndexOf("__");
            return separatorIndex == -1 ? fileName : fileName[..separatorIndex];
        }

        private static string GetSoundBankFilePath(string fileName, string language)
        {
            if (language == Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx))
                return $"audio\\wwise\\{fileName}";

            return $"audio\\wwise\\{language}\\{fileName}";
        }

        public void GenerateDialogueEventsForTestingSoundBank(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            var dialogueEventHircs = GenerateDialogueEventHircs(soundBank);
            hircItems.AddRange(dialogueEventHircs);

            var vanillaDialogueEvents = _audioRepository.GetHircs(AkBkHircType.Dialogue_Event)
                .Where(hircItem => hircItem.IsCA == true);

            foreach (var hircItem in hircItems)
            {
                var vanillaDialogueEvent = vanillaDialogueEvents.FirstOrDefault(dialogueEvent => dialogueEvent.Id == hircItem.Id) as CAkDialogueEvent_V136;
                var vanillaDecisionTree = vanillaDialogueEvent.AkDecisionTree as AkDecisionTree_V136;

                var compilerDialogueEvent = hircItem as CAkDialogueEvent_V136;
                var compilerDecisionTree = compilerDialogueEvent.AkDecisionTree as AkDecisionTree_V136;

                // Merge the vanilla decision tree into the compiled decision tree so the compiled decision tree takes priority
                var decisionTree = AkDecisionTree_V136.MergeDecisionTrees(compilerDecisionTree.DecisionTree, vanillaDecisionTree.DecisionTree);
                var nodes = AkDecisionTree_V136.FlattenDecisionTree(decisionTree);
                var mergedDecisionTree = new AkDecisionTree_V136
                {
                    DecisionTree = decisionTree,
                    Nodes = nodes
                };
                compilerDialogueEvent.AkDecisionTree = mergedDecisionTree;
                compilerDialogueEvent.TreeDataSize = mergedDecisionTree.GetSize();
                compilerDialogueEvent.UpdateSectionSize();
            }

            SortHircs(hircItems);

            WriteSoundBank(soundBank.TestingId, soundBank.LanguageId, soundBank.TestingFileName, soundBank.TestingFilePath, hircItems);
        }

        public void GenerateMergingSoundBank(SoundBank soundBank)
        {
            // We generate all hircs in the merging SoundBank as when merging we check for ID clashes so we can report them to modders
            var actionEventToHircLookup = new Dictionary<ActionEvent, HircItem>();
            var hircItems = new List<HircItem>();

            if (soundBank.ActionEvents.Count != 0)
            {
                var actionEventHircs = GenerateActionEventHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionEventHircs);

                var actionHircs = GenerateActionHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionHircs);

                var sourceHircsFromPlay = GenerateSourceHircsFromPlayActions(soundBank);
                hircItems.AddRange(sourceHircsFromPlay);
            }

            if (soundBank.DialogueEvents.Count != 0)
            {
                var dialogueEventHircs = GenerateDialogueEventHircs(soundBank);
                hircItems.AddRange(dialogueEventHircs);

                var sourceHircsFromDialogueEvents = GenerateSourceHircsFromDialogueEvents(soundBank);
                hircItems.AddRange(sourceHircsFromDialogueEvents);
            }

            SortHircs(hircItems);

            // Same ordering exception as the modder's .bnk. The containers go in carrying the mod's
            // branches alone so the merger can combine several mods before folding vanilla in.
            hircItems.AddRange(GenerateMusicHierarchyHircs(soundBank));
            hircItems.AddRange(GenerateModdedMusicSwitchContainers(soundBank));

            WriteSoundBank(soundBank.MergingId, soundBank.LanguageId, soundBank.MergingFileName, soundBank.MergingFilePath, hircItems);
        }

        public void GenerateMergedDialogueEventSoundBanks(List<string> moddedSoundBanks, string soundBankSuffix)
        {
            _audioEditorIntegrityService.CheckMergingSoundBanksIdIntegrity();

            var vanillaDialogueEventsByBnkByLanguage = _audioRepository.GetVanillaDialogueEventsByBnkByLanguage();
            var moddedDialogueEventsByLanguage = _audioRepository.GetModdedDialogueEventsByLanguage(moddedSoundBanks);

            var soundBanksToGenerateByLanguage = moddedDialogueEventsByLanguage
                .ToDictionary(
                    languageEntry => languageEntry.Key,
                    languageEntry => languageEntry.Value
                        .Select(hircItem => _audioRepository.GetNameFromId(hircItem.Id))
                        .Select(dialogueEventName => Wh3DialogueEventInformation.GetSoundBank(dialogueEventName))
                        .Distinct()
                        .ToList());

            foreach (var bnkByLanguage in vanillaDialogueEventsByBnkByLanguage)
            {
                var language = bnkByLanguage.Key;
                if (!moddedDialogueEventsByLanguage.ContainsKey(language))
                    continue;

                var soundBanksToGenerate = soundBanksToGenerateByLanguage[language];
                foreach (var hircsByBnk in bnkByLanguage.Value)
                {
                    var firstDialogueEventName = hircsByBnk.Value.Select(hircItem => _audioRepository.GetNameFromId(hircItem.Id)).FirstOrDefault();
                    var currentSoundBank = Wh3DialogueEventInformation.GetSoundBank(firstDialogueEventName);
                    if (!soundBanksToGenerate.Contains(currentSoundBank))
                        continue;

                    _logger.Here().Information($"Merging SoundBanks for language {language}");

                    GenerateMergedDialogueEventSoundBank(hircsByBnk.Value, moddedDialogueEventsByLanguage, language, currentSoundBank, soundBankSuffix);
                }
            }
        }

        /// <summary>
        /// The music counterpart of <see cref="GenerateMergedDialogueEventSoundBanks"/>: takes the
        /// merging .bnks from several mods and writes one .bnk per vanilla .bnk holding the Music
        /// Switch containers with everyone's branches in them.
        /// </summary>
        public void GenerateMergedMusicSoundBanks(List<string> moddedSoundBanks, string soundBankSuffix)
        {
            var moddedContainers = _audioRepository.GetModdedMusicSwitchContainers(moddedSoundBanks);
            if (moddedContainers.Count == 0)
                return;

            foreach (var (vanillaBnkFilePath, vanillaContainers) in _audioRepository.GetVanillaMusicSwitchContainersByBnk())
            {
                var mergedContainers = new List<HircItem>();

                foreach (var vanillaHirc in vanillaContainers)
                {
                    var vanillaContainer = vanillaHirc as CAkMusicSwitchCntr_V136;
                    var matchingModdedContainers = moddedContainers
                        .Where(moddedHirc => moddedHirc.Id == vanillaContainer.Id)
                        .Cast<CAkMusicSwitchCntr_V136>()
                        .ToList();

                    if (matchingModdedContainers.Count == 0)
                        continue;

                    _logger.Here().Information($"Merging Music Switch container {_audioRepository.GetNameFromId(vanillaContainer.Id)}");

                    // The mods are folded together first and vanilla last, so every mod's branches
                    // beat vanilla's. That is the opposite of the Dialogue Event merge, which starts
                    // from vanilla - but a music branch is usually a replacement of a culture vanilla
                    // already covers, and letting vanilla win would silently undo exactly the change
                    // the modder tested and shipped.
                    var mergedContainer = matchingModdedContainers[0];
                    foreach (var moddedContainer in matchingModdedContainers.Skip(1))
                    {
                        _logger.Here().Information($"Merging decision tree from {Path.GetFileName(moddedContainer.BnkFilePath)}");
                        mergedContainer = _musicSwitchContainerMergeService.MergeContainers(mergedContainer, moddedContainer);
                    }

                    mergedContainers.Add(_musicSwitchContainerMergeService.MergeContainers(mergedContainer, vanillaContainer));
                }

                if (mergedContainers.Count == 0)
                    continue;

                var soundBankNameBase = GetSoundBankNameBase(vanillaBnkFilePath);
                var soundBankNameWithoutExtension = $"{soundBankNameBase}_0_{soundBankSuffix}";
                var fileName = $"{soundBankNameWithoutExtension}.bnk";
                var language = _audioRepository.GetNameFromId(vanillaContainers[0].LanguageId);
                var filePath = GetSoundBankFilePath(fileName, language);

                _logger.Here().Information($"Merging Music Switch containers for SoundBank {filePath}");
                WriteSoundBank(WwiseHash.Compute(soundBankNameWithoutExtension), vanillaContainers[0].LanguageId, fileName, filePath, mergedContainers);
            }
        }

        private List<HircItem> GenerateActionEventHircs(SoundBank soundBank, Dictionary<ActionEvent, HircItem> actionEventToHircLookup)
        {
            var hircItems = new List<HircItem>();
            foreach (var actionEvent in soundBank.ActionEvents)
            {
                var actionEventHirc = _hircGeneratorServiceFactory.GenerateHirc(actionEvent);
                hircItems.Add(actionEventHirc);
                actionEventToHircLookup[actionEvent] = actionEventHirc;
            }
            return hircItems;
        }

        private List<HircItem> GenerateActionHircs(SoundBank soundBank, Dictionary<ActionEvent, HircItem> actionEventToHircLookup)
        {
            var hircItems = new List<HircItem>();

            foreach (var actionEvent in soundBank.ActionEvents)
            {
                if (actionEvent.Actions.Count > 1)
                    throw new NotSupportedException("Multiple Actions are not supported");

                var actionEventHirc = actionEventToHircLookup[actionEvent];

                foreach (var action in actionEvent.Actions)
                {
                    var actionHirc = _hircGeneratorServiceFactory.GenerateHirc(action, soundBank);
                    hircItems.Add(actionHirc);

                    if (actionEventHirc.HircChildren == null)
                        actionEventHirc.HircChildren = [];
                    actionEventHirc.HircChildren.Add(actionHirc);
                }
            }

            return hircItems;
        }

        private List<HircItem> GenerateSourceHircsFromPlayActions(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            foreach (var actionEvent in soundBank.ActionEvents)
            {
                foreach (var action in actionEvent.Actions)
                {
                    if (action.ActionType == AkActionType.Play)
                    {
                        if (action.TargetHircTypeIsSound())
                        {
                            var sound = soundBank.GetSound(action.TargetHircId);
                            var soundTargetHircs = GenerateSoundTargetHircs(soundBank, sound);
                            hircItems.AddRange(soundTargetHircs);
                        }
                        else if (action.TargetHircTypeIsRandomSequenceContainer())
                        {
                            var randomSequenceContainer = soundBank.GetRandomSequenceContainer(action.TargetHircId);
                            var randomSequenceContainerTargetHircs = GenerateRandomSequenceContainerTargetHircs(soundBank, randomSequenceContainer);
                            hircItems.AddRange(randomSequenceContainerTargetHircs);
                        }
                    }
                }
            }

            return hircItems;
        }

        private List<HircItem> GenerateDialogueEventHircs(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();
            foreach (var dialogueEvent in soundBank.DialogueEvents)
            {
                var dialogueEventHirc = _hircGeneratorServiceFactory.GenerateHirc(dialogueEvent);
                hircItems.Add(dialogueEventHirc);
            }
            return hircItems;
        }

        private List<HircItem> GenerateSourceHircsFromDialogueEvents(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            foreach (var dialogueEvent in soundBank.DialogueEvents)
            {
                foreach (var statePath in dialogueEvent.StatePaths)
                {

                    if (statePath.TargetHircTypeIsSound())
                    {
                        var sound = soundBank.GetSound(statePath.TargetHircId);
                        var soundTargetHircs = GenerateSoundTargetHircs(soundBank, sound);
                        hircItems.AddRange(soundTargetHircs);
                    }
                    else if (statePath.TargetHircTypeIsRandomSequenceContainer())
                    {
                        var randomSequenceContainer = soundBank.GetRandomSequenceContainer(statePath.TargetHircId);
                        var randomSequenceContainerTargetHircs = GenerateRandomSequenceContainerTargetHircs(soundBank, randomSequenceContainer);
                        hircItems.AddRange(randomSequenceContainerTargetHircs);
                    }
                }
            }

            return hircItems;
        }

        private List<HircItem> GenerateSoundTargetHircs(SoundBank soundBank, Sound sound)
        {
            var hircItems = new List<HircItem>();

            var soundHirc = _hircGeneratorServiceFactory.GenerateHirc(sound);
            soundHirc.IsTarget = true;
            hircItems.Add(soundHirc);
            return hircItems;
        }

        private List<HircItem> GenerateRandomSequenceContainerTargetHircs(SoundBank soundBank, RandomSequenceContainer randomSequenceContainer)
        {
            var hircItems = new List<HircItem>();

            var randomSequenceContainerHirc = _hircGeneratorServiceFactory.GenerateHirc(randomSequenceContainer, soundBank);
            hircItems.Add(randomSequenceContainerHirc);

            randomSequenceContainerHirc.IsTarget = true;
            if (randomSequenceContainerHirc.HircChildren == null)
                randomSequenceContainerHirc.HircChildren = [];

            var sounds = soundBank.GetSounds(randomSequenceContainer.Children);
            foreach (var sound in sounds)
            {
                var soundHirc = _hircGeneratorServiceFactory.GenerateHirc(sound);
                hircItems.Add(soundHirc);
                randomSequenceContainerHirc.HircChildren.Add(soundHirc);
            }

            return hircItems;
        }

        private void GenerateMergedDialogueEventSoundBank(
            List<HircItem> vanillaHircs,
            Dictionary<string, List<HircItem>> moddedDialogueEventsByLanguage,
            string language,
            Wh3SoundBank currentSoundBank, 
            string soundBankSuffix)
        {
            var dialogueEvents = new List<HircItem>();

            var soundBankNameBase = Wh3SoundBankInformation.GetName(currentSoundBank);
            var soundBankNameWithoutExtension = $"{soundBankNameBase}_0_{soundBankSuffix}";
            var soundBankFileName = $"{soundBankNameWithoutExtension}.bnk";

            var soundBankFilePath = $"audio\\wwise\\{language}\\{soundBankFileName}";
            if (language == Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx))
                soundBankFilePath = $"audio\\wwise\\{soundBankFileName}";

            var soundBankId = WwiseHash.Compute(soundBankNameWithoutExtension);
            var languageId = WwiseHash.Compute(language);

            _logger.Here().Information($"Merging Dialogue Events for SoundBank {soundBankFilePath}");

            foreach (var vanillaHirc in vanillaHircs)
            {
                // Clone it as we shouldn't modify the original
                var vanillaDialogueEvent = vanillaHirc as CAkDialogueEvent_V136;
                var mergedDialogueEvent = vanillaDialogueEvent.Clone();

                var matchingModdedDialogueEvents = moddedDialogueEventsByLanguage[language]
                    .Where(moddedHircItem => moddedHircItem.Id == vanillaDialogueEvent.Id)
                    .ToList();

                if (matchingModdedDialogueEvents.Count == 0)
                    continue;

                _logger.Here().Information($"Merging Dialogue Event {_audioRepository.GetNameFromId(vanillaHirc.Id)}");

                foreach (var moddedDialogueEventHirc in matchingModdedDialogueEvents)
                {
                    _logger.Here().Information($"Merging decision tree from {Path.GetFileName(moddedDialogueEventHirc.BnkFilePath)}");

                    var currentDecisionTree = mergedDialogueEvent.AkDecisionTree as AkDecisionTree_V136;
                    var moddedDialogueEvent = moddedDialogueEventHirc as CAkDialogueEvent_V136;
                    var moddedDecisionTree = moddedDialogueEvent.AkDecisionTree as AkDecisionTree_V136;

                    var mergedDecisionTree = new AkDecisionTree_V136();
                    mergedDecisionTree.DecisionTree = AkDecisionTree_V136.MergeDecisionTrees(currentDecisionTree.DecisionTree, moddedDecisionTree.DecisionTree);
                    mergedDecisionTree.Nodes = AkDecisionTree_V136.FlattenDecisionTree(mergedDecisionTree.DecisionTree);

                    mergedDialogueEvent.AkDecisionTree = mergedDecisionTree;
                    mergedDialogueEvent.TreeDataSize = mergedDecisionTree.GetSize();
                    mergedDialogueEvent.UpdateSectionSize();
                }

                dialogueEvents.Add(mergedDialogueEvent);
            }

            SortHircs(dialogueEvents);

            WriteSoundBank(soundBankId, languageId, soundBankFileName, soundBankFilePath, dialogueEvents);
        }

        private static void SortHircs(List<HircItem> hircItems)
        {
            var targetHircs = hircItems
                .Where(hircItem => hircItem.IsTarget)
                .ToList();

            var eventHircs = hircItems
                .Where(hircItem => hircItem.HircType == AkBkHircType.Event || hircItem.HircType == AkBkHircType.Dialogue_Event)
                .ToList();

            var sortedHircItems = new List<HircItem>();

            foreach (var targetHirc in targetHircs.OrderBy(sourceHirc => sourceHirc.Id))
            {
                if (targetHirc.HircType == AkBkHircType.RandomSequenceContainer)
                {
                    var soundHircs = targetHirc.HircChildren.OrderBy(soundHirc => soundHirc.Id);
                    sortedHircItems.AddRange(soundHircs);
                    sortedHircItems.Add(targetHirc);
                }
                else
                    sortedHircItems.Add(targetHirc);
            }

            foreach (var eventHirc in eventHircs.OrderBy(eventHirc => eventHirc.Id))
            {
                if (eventHirc.HircType == AkBkHircType.Event)
                {
                    var actionHircs = eventHirc.HircChildren.OrderBy(eventHirc => eventHirc.Id);
                    sortedHircItems.AddRange(actionHircs);
                    sortedHircItems.Add(eventHirc);
                }
                else
                    sortedHircItems.Add(eventHirc);
            }

            hircItems.Clear();
            hircItems.AddRange(sortedHircItems);
        }

        private void WriteSoundBank(uint id, uint languageId, string fileName, string filePath, List<HircItem> hircItems)
        {
            var gameInformation = GameInformationDatabase.GetGameById(_applicationSettingsService.CurrentSettings.CurrentGame);
            var bankGeneratorVersion = (uint)gameInformation.BankGeneratorVersion;
            var wwiseProjectId = (uint)gameInformation.WwiseProjectId;

            var bkhdChunk = BkhdChunkGenerator.GenerateBkhdChunk(bankGeneratorVersion, id, languageId, wwiseProjectId);
            var bkhdChunkBytes = BkhdChunk.WriteData(bkhdChunk);

            var hircChunk = HircChunkGenerator.GenerateHircChunk(hircItems);
            var hircChunkBytes = HircChunk.WriteData(hircChunk, bankGeneratorVersion);

            using var memStream = new MemoryStream();
            memStream.Write(bkhdChunkBytes);
            memStream.Write(hircChunkBytes);
            var bytes = memStream.ToArray();

            var bnkPackFile = new PackFile(fileName, new MemorySource(bytes));
            var reparsedSanityFile = BnkFile.CreateFromBytes(bnkPackFile.DataSource.ReadData(), "test\\fakefilename.bnk", true);

            _fileSaveService.Save(filePath, bnkPackFile.DataSource.ReadData(), false);
        }
    }
}
