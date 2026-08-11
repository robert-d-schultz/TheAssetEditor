using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Factories;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Shared.GameFormats.Wwise.Enums;
using HircSettings = Editors.Audio.Shared.AudioProject.Models.HircSettings;

namespace Editors.Audio.AudioEditor.Core.AudioProjectMutation
{
    public interface IActionEventService
    {
        void AddPlayActionEvent(string actionEventTypeName, string actionEventName, List<AudioFile> audioFiles, HircSettings hircSettings);
        void AddSetStateActionEvent(string actionEventTypeName, string actionEventName, string stateGroupName, string stateName, List<AudioFile> audioFiles);
        void AddPauseResumeStopActionEvent(string actionEventTypeName, string actionEventName);
        void RemoveActionEvent(string actionEventNodeName, string actionEventName);
    }

    public class ActionEventService(
        IAudioEditorStateService audioEditorStateService,
        IAudioRepository audioRepository,
        IActionEventFactory actionEventFactory,
        IMusicHierarchyFactory musicHierarchyFactory) : IActionEventService
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IAudioRepository _audioRepository = audioRepository;
        private readonly IActionEventFactory _actionEventFactory = actionEventFactory;
        private readonly IMusicHierarchyFactory _musicHierarchyFactory = musicHierarchyFactory;

        private readonly ILogger _logger = Logging.Create<ActionEventService>();

        public void AddPlayActionEvent(string actionEventTypeName, string actionEventName, List<AudioFile> audioFiles, HircSettings hircSettings)
        {
            var usedHircIds = IdGenerator.GetUsedHircIds(_audioRepository, _audioEditorStateService.AudioProject);
            var usedSourceIds = IdGenerator.GetUsedSourceIds(_audioRepository, _audioEditorStateService.AudioProject);

            var gameSoundBankName = Wh3SoundBankInformation.GetName(Wh3ActionEventInformation.GetSoundBank(actionEventTypeName));
            var audioProjectNameWithoutExtension = Path.GetFileNameWithoutExtension(_audioEditorStateService.AudioProjectFileName);
            var soundBankName = $"{gameSoundBankName}_{audioProjectNameWithoutExtension}";
            var soundBank = _audioEditorStateService.AudioProject.GetSoundBank(soundBankName);

            var actionEventType = Wh3ActionEventInformation.GetActionEventType(actionEventTypeName);
            var playActionEventResult = _actionEventFactory.CreatePlayActionEvent(usedHircIds, usedSourceIds, actionEventType, actionEventName, audioFiles, hircSettings, soundBank.Id, soundBank.Language);
            soundBank.ActionEvents.InsertAlphabetically(playActionEventResult.ActionEvent);

            if (playActionEventResult.Actions.Count > 1)
                throw new NotSupportedException("Multiple Actions are not supported.");

            var action = playActionEventResult.Actions.FirstOrDefault();
            if (action.TargetHircTypeIsSound())
            {
                soundBank.Sounds.TryAdd(playActionEventResult.SoundTarget);

                var audioFile = _audioEditorStateService.AudioProject.GetAudioFile(playActionEventResult.SoundTarget.SourceId);
                if (audioFile == null)
                {
                    audioFile = audioFiles.FirstOrDefault(audioFile => audioFile.Id == playActionEventResult.SoundTarget.SourceId);
                    _audioEditorStateService.AudioProject.AudioFiles.TryAdd(audioFile);
                }

                if (!audioFile.Sounds.Contains(playActionEventResult.SoundTarget.Id))
                    audioFile.Sounds.Add(playActionEventResult.SoundTarget.Id);
            }
            else if (action.TargetHircTypeIsRandomSequenceContainer())
            {
                soundBank.RandomSequenceContainers.TryAdd(playActionEventResult.RandomSequenceContainerTarget);
                soundBank.Sounds.AddRange(playActionEventResult.RandomSequenceContainerSounds);

                foreach (var sound in playActionEventResult.RandomSequenceContainerSounds)
                {
                    var audioFile = _audioEditorStateService.AudioProject.GetAudioFile(sound.SourceId);
                    if (audioFile == null)
                    {
                        audioFile = audioFiles.FirstOrDefault(audioFile => audioFile.Id == sound.SourceId);
                        _audioEditorStateService.AudioProject.AudioFiles.TryAdd(audioFile);
                    }

                    if (!audioFile.Sounds.Contains(sound.Id))
                        audioFile.Sounds.Add(sound.Id);
                }
            }
        }

        /// <summary>
        /// A music Action Event, which sets a State instead of playing a Sound. The Event itself has
        /// no target objects, but setting a State only makes noise if the vanilla Music Switch
        /// container has a branch for it - so the audio picked in the Audio Files Explorer becomes
        /// the hierarchy under that branch rather than a Sound the Event points at.
        ///
        /// The State is also added to the project's State Group so it shows up in the Audio Explorer.
        /// </summary>
        public void AddSetStateActionEvent(string actionEventTypeName, string actionEventName, string stateGroupName, string stateName, List<AudioFile> audioFiles)
        {
            var usedHircIds = IdGenerator.GetUsedHircIds(_audioRepository, _audioEditorStateService.AudioProject);

            var gameSoundBankName = Wh3SoundBankInformation.GetName(Wh3ActionEventInformation.GetSoundBank(actionEventTypeName));
            var audioProjectNameWithoutExtension = Path.GetFileNameWithoutExtension(_audioEditorStateService.AudioProjectFileName);
            var soundBankName = $"{gameSoundBankName}_{audioProjectNameWithoutExtension}";
            var soundBank = _audioEditorStateService.AudioProject.GetSoundBank(soundBankName);

            var actionEventType = Wh3ActionEventInformation.GetActionEventType(actionEventTypeName);
            var result = _actionEventFactory.CreateSetStateActionEvent(usedHircIds, actionEventType, actionEventName, stateGroupName, stateName);
            soundBank.ActionEvents.InsertAlphabetically(result.ActionEvent);

            AddStateToStateGroup(stateGroupName, stateName);
            AddMusicBranch(soundBank, stateGroupName, stateName, audioFiles, usedHircIds);
        }

        /// <summary>
        /// The hierarchy the State selects. Nothing is added when no audio was picked - the Event is
        /// still worth having on its own, since it can set a State a branch added earlier already
        /// covers.
        /// </summary>
        private void AddMusicBranch(SoundBank soundBank, string stateGroupName, string stateName, List<AudioFile> audioFiles, HashSet<uint> usedHircIds)
        {
            if (audioFiles == null || audioFiles.Count == 0)
                return;

            var musicSwitchContainerId = Wh3MusicHierarchyInformation.GetMusicSwitchContainerId(stateGroupName);
            if (musicSwitchContainerId == null)
            {
                _logger.Here().Error($"State Group {stateGroupName} has no known Music Switch container, so its audio cannot be reached and was not added");
                return;
            }

            // A branch per State, matched on its Group as well as its name - "Empire" is a State in
            // both the campaign subculture Group and the battle culture one, and they are different
            // branches in different containers.
            //
            // Picking more audio for a State that already has a branch extends its playlist rather
            // than starting a second one, since the decision tree can only point at one place.
            var existingRandomSequence = soundBank.MusicRandomSequences
                .FirstOrDefault(musicRandomSequence =>
                    musicRandomSequence.StateName == stateName && musicRandomSequence.StateGroupName == stateGroupName);

            if (existingRandomSequence != null)
            {
                var addedSegments = _musicHierarchyFactory
                    .CreateMusicBranch(usedHircIds, musicSwitchContainerId.Value, stateGroupName, stateName, audioFiles, soundBank.Language)
                    .MusicSegments;

                foreach (var musicSegment in addedSegments)
                {
                    musicSegment.DirectParentId = existingRandomSequence.Id;
                    existingRandomSequence.Segments.Add(new MusicPlaylistEntry
                    {
                        SegmentId = musicSegment.Id,
                        PlaylistItemId = existingRandomSequence.PlaylistRootItemId + existingRandomSequence.Segments.Count + 1
                    });
                }

                AddMusicSegments(soundBank, addedSegments, audioFiles);
                return;
            }

            var branch = _musicHierarchyFactory.CreateMusicBranch(usedHircIds, musicSwitchContainerId.Value, stateGroupName, stateName, audioFiles, soundBank.Language);
            soundBank.MusicRandomSequences.Add(branch.MusicRandomSequence);
            AddMusicSegments(soundBank, branch.MusicSegments, audioFiles);
        }

        private void AddMusicSegments(SoundBank soundBank, List<MusicSegment> musicSegments, List<AudioFile> audioFiles)
        {
            foreach (var musicSegment in musicSegments)
            {
                soundBank.MusicSegments.Add(musicSegment);

                if (_audioEditorStateService.AudioProject.GetAudioFile(musicSegment.SourceId) == null)
                {
                    var audioFile = audioFiles.FirstOrDefault(audioFile => audioFile.Id == musicSegment.SourceId);
                    _audioEditorStateService.AudioProject.AudioFiles.TryAdd(audioFile);
                }
            }
        }

        private void AddStateToStateGroup(string stateGroupName, string stateName)
        {
            var stateGroup = _audioEditorStateService.AudioProject.StateGroups
                .FirstOrDefault(stateGroup => stateGroup.Name == stateGroupName);

            if (stateGroup == null)
                return;

            if (stateGroup.States.Any(state => state.Name == stateName))
                return;

            // Vanilla States are already known to the game, so re-adding one would only produce a
            // duplicate in the project's own list.
            if (_audioRepository.StatesByStateGroup.TryGetValue(stateGroupName, out var vanillaStates) && vanillaStates.Contains(stateName))
                return;

            stateGroup.States.InsertAlphabetically(new State(stateName));
        }

        public void AddPauseResumeStopActionEvent(string actionEventTypeName, string actionEventName)
        {
            var usedHircIds = IdGenerator.GetUsedHircIds(_audioRepository, _audioEditorStateService.AudioProject);
            var gameSoundBankName = Wh3SoundBankInformation.GetName(Wh3ActionEventInformation.GetSoundBank(actionEventTypeName));
            var audioProjectNameWithoutExtension = Path.GetFileNameWithoutExtension(_audioEditorStateService.AudioProjectFileName);
            var soundBankName = $"{gameSoundBankName}_{audioProjectNameWithoutExtension}";
            var soundBank = _audioEditorStateService.AudioProject.GetSoundBank(soundBankName);

            var actionEventSuffix = TableHelpers.RemoveActionEventPrefix(actionEventName);
            var playActionEvent = soundBank.GetActionEvent($"Play_{actionEventSuffix}");

            if (actionEventName.StartsWith("Pause_"))
            {
                var pauseActionEventResult = _actionEventFactory.CreatePauseActionEvent(usedHircIds, playActionEvent);
                soundBank.ActionEvents.InsertAlphabetically(pauseActionEventResult.ActionEvent);
            }
            else if (actionEventName.StartsWith("Resume_"))
            {
                var resumeActionEventResult = _actionEventFactory.CreateResumeActionEvent(usedHircIds, playActionEvent);
                soundBank.ActionEvents.InsertAlphabetically(resumeActionEventResult.ActionEvent);
            }
            else if (actionEventName.StartsWith("Stop_"))
            {
                var stopActionEventResult = _actionEventFactory.CreateStopActionEvent(usedHircIds, playActionEvent);
                soundBank.ActionEvents.InsertAlphabetically(stopActionEventResult.ActionEvent);
            }
        }

        /// <summary>
        /// The hierarchy a removed music Event's State selected. Another Event can set the same
        /// State, so the branch only goes when nothing is left pointing at it - otherwise removing
        /// one of two Events would silence both.
        /// </summary>
        private void RemoveMusicBranch(SoundBank soundBank, ActionEvent actionEvent)
        {
            var setStateActions = actionEvent.Actions
                .Where(action => action.ActionType == AkActionType.SetState)
                .ToList();

            foreach (var setStateAction in setStateActions)
            {
                var stillSet = soundBank.ActionEvents
                    .Any(otherActionEvent => otherActionEvent.Actions
                        .Any(action => action.ActionType == AkActionType.SetState
                            && action.StateName == setStateAction.StateName
                            && action.StateGroupName == setStateAction.StateGroupName));

                if (stillSet)
                    continue;

                var musicRandomSequence = soundBank.MusicRandomSequences
                    .FirstOrDefault(randomSequence =>
                        randomSequence.StateName == setStateAction.StateName
                        && randomSequence.StateGroupName == setStateAction.StateGroupName);

                if (musicRandomSequence == null)
                    continue;

                foreach (var musicSegment in soundBank.GetMusicSegments(musicRandomSequence))
                {
                    soundBank.MusicSegments.Remove(musicSegment);

                    var audioFile = _audioEditorStateService.AudioProject.GetAudioFile(musicSegment.SourceId);
                    if (audioFile != null && audioFile.Sounds.Count == 0)
                        _audioEditorStateService.AudioProject.AudioFiles.Remove(audioFile);
                }

                soundBank.MusicRandomSequences.Remove(musicRandomSequence);
            }
        }

        public void RemoveActionEvent(string actionEventNodeName, string actionEventName)
        {
            var gameSoundBankName = Wh3SoundBankInformation.GetName(Wh3ActionEventInformation.GetSoundBank(actionEventNodeName));
            var audioProjectNameWithoutExtension = Path.GetFileNameWithoutExtension(_audioEditorStateService.AudioProjectFileName);
            var soundBankName = $"{gameSoundBankName}_{audioProjectNameWithoutExtension}";

            var soundBank = _audioEditorStateService.AudioProject.GetSoundBank(soundBankName);
            var actionEvent = soundBank.GetActionEvent(actionEventName);
            soundBank.ActionEvents.Remove(actionEvent);

            // We let the Play Action Event remove the target objects rather than Pause / Resume / Stop Action Events as otherwise they'd
            // be removing objects that the Play Action Event removal process has already removed.
            if (TableHelpers.IsPauseResumeStopActionEvent(actionEventName))
            {
                var actionEventSuffix = TableHelpers.RemoveActionEventPrefix(actionEventName);
                var playActionEventName = $"Play_{actionEventSuffix}";
                var playActionEvent = soundBank.GetActionEvent(playActionEventName);

                // To ensure the Play Action Event doesn't exist when it comes to handling the removal of Pause / Resume / Stop Action Events,
                // in the View Model when we send the rows to remove to the command we put the Play Action Events at the top of the list
                // so this should always be null but in case it somehow isn't we check here.
                if (playActionEvent != null)
                    throw new InvalidOperationException("Cannot remove Pause / Resume / Stop Action Event target objects while the Play Action Event still exists.");
                
                return;
            }

            RemoveMusicBranch(soundBank, actionEvent);

            foreach (var action in actionEvent.Actions)
            {
                if (action.TargetHircTypeIsSound())
                {
                    var sound = soundBank.GetSound(action.TargetHircId);
                    var audioFile = _audioEditorStateService.AudioProject.GetAudioFile(sound.SourceId);

                    soundBank.Sounds.Remove(sound);
                    audioFile.Sounds.Remove(sound.Id);

                    if (audioFile.Sounds.Count == 0)
                        _audioEditorStateService.AudioProject.AudioFiles.Remove(audioFile);
                }
                else if (action.TargetHircTypeIsRandomSequenceContainer())
                {
                    var randomSequenceContainer = soundBank.GetRandomSequenceContainer(action.TargetHircId);
                    var sounds = soundBank.GetSounds(randomSequenceContainer.Children);
                    foreach (var sound in sounds)
                    {
                        var audioFile = _audioEditorStateService.AudioProject.GetAudioFile(sound.SourceId);

                        soundBank.Sounds.Remove(sound);
                        audioFile.Sounds.Remove(sound.Id);

                        if (audioFile.Sounds.Count == 0)
                            _audioEditorStateService.AudioProject.AudioFiles.Remove(audioFile);
                    }

                    soundBank.RandomSequenceContainers.Remove(randomSequenceContainer);
                }
            }
        }
    }
}
