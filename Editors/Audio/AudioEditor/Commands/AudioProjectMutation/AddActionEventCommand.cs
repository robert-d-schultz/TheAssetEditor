using System.Data;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Core.AudioProjectMutation;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;

namespace Editors.Audio.AudioEditor.Commands.AudioProjectMutation
{
    public class AddActionEventCommand(IAudioEditorStateService audioEditorStateService, IActionEventService actionEventService) : IAudioProjectMutationUICommand
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IActionEventService _actionEventService = actionEventService;

        public MutationType Action => MutationType.Add;
        public AudioProjectTreeNodeType NodeType => AudioProjectTreeNodeType.ActionEventType;

        private DataRow _row = null!;

        public void Configure(DataRow row)
        {
            _row = row;
        }

        public void Execute()
        {
            var actionEventTypeName = _audioEditorStateService.SelectedAudioProjectExplorerNode.Name;
            var audioFiles = _audioEditorStateService.AudioFiles;
            var hircSettings = _audioEditorStateService.HircSettings;
            var actionEventName = TableHelpers.GetActionEventNameFromRow(_row);

            // Music Action Events reach here with no prefix at all, and neither branch below suits
            // them: vanilla music events hold a SetState action against one of the music State
            // Groups rather than a Play action against a Sound, and there is no service method for
            // that yet. Until there is, they are dropped rather than being written out in the wrong
            // shape.
            if (actionEventName.StartsWith("Play_"))
                _actionEventService.AddPlayActionEvent(actionEventTypeName, actionEventName, audioFiles, hircSettings);
            else if (actionEventName.StartsWith("Pause_") || actionEventName.StartsWith("Resume_") || actionEventName.StartsWith("Stop_"))
                _actionEventService.AddPauseResumeStopActionEvent(actionEventTypeName, actionEventName);
        }
    }
}
