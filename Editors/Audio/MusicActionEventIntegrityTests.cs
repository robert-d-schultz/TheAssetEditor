using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Moq;
using Shared.Core.PackFiles;
using Action = Editors.Audio.Shared.AudioProject.Models.Action;

namespace Test.Audio
{
    // Regression cover for the integrity check rejecting music Action Events. It required every
    // Action to carry a non-zero BankId, which is true of a Play action - it names the bank the
    // sound has to be loaded from - but not of a SetState action, which loads nothing. Loading any
    // project containing a music event threw "Action.BankId should not be 0." and took the app down.
    internal class MusicActionEventIntegrityTests
    {
        const string ProjectName = "music_test_culture";
        const string StateGroupName = "Battle_Music_WH3_Culture";
        const string StateName = "test_culture";

        [Test]
        public void AProjectContainingAMusicEventPassesTheIntegrityCheck()
        {
            var audioProject = CreateProjectWithMusicEvent(out _);
            var service = CreateService();

            Assert.DoesNotThrow(() => service.CheckAudioProjectDataIntegrity(audioProject, ProjectName));
        }

        [Test]
        public void AMusicEventWhoseStateIdDoesNotMatchItsStateNameIsRejected()
        {
            var audioProject = CreateProjectWithMusicEvent(out var action);

            // The ids are what the game actually resolves; the names are only there so the editor
            // can show what the event does. If they drift apart the event silently selects nothing,
            // so the check has to catch it rather than trust the name.
            action.IdExt = 1;

            var service = CreateService();

            Assert.That(
                () => service.CheckAudioProjectDataIntegrity(audioProject, ProjectName),
                Throws.InvalidOperationException.With.Message.Contains(StateName));
        }

        [Test]
        public void AMusicEventWithNoStateGroupNameIsRejected()
        {
            var audioProject = CreateProjectWithMusicEvent(out var action);
            action.StateGroupName = null;

            var service = CreateService();

            Assert.That(
                () => service.CheckAudioProjectDataIntegrity(audioProject, ProjectName),
                Throws.InvalidOperationException);
        }

        static AudioProjectFile CreateProjectWithMusicEvent(out Action setStateAction)
        {
            var language = Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx);
            var audioProject = AudioProjectFile.CreateNew(language, ProjectName);

            var soundBankName = $"{Wh3SoundBankInformation.GetName(Wh3SoundBank.GlobalMusic)}_{ProjectName}";
            var soundBank = audioProject.GetSoundBank(soundBankName);

            setStateAction = Action.CreateSetState(id: 12345, StateGroupName, StateName);
            var actionEvent = new ActionEvent(
                id: 54321,
                name: $"music_b_faction_{StateName}",
                actions: [setStateAction],
                actionEventType: Wh3ActionEventType.Music);

            soundBank.ActionEvents.InsertAlphabetically(actionEvent);
            return audioProject;
        }

        static AudioEditorIntegrityService CreateService()
        {
            // The check only consults the repository for which ids vanilla already uses. Reporting
            // none of them is enough here - the point is the shape of the Action, not id collisions.
            var audioRepository = new Mock<IAudioRepository>();
            audioRepository.Setup(x => x.GetUsedVanillaHircIdsByLanguageId(It.IsAny<uint>())).Returns([]);
            audioRepository.Setup(x => x.GetUsedVanillaSourceIdsByLanguageId(It.IsAny<uint>())).Returns([]);
            return new AudioEditorIntegrityService(new Mock<IPackFileService>().Object, audioRepository.Object);
        }
    }
}
