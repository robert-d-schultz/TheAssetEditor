using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Table;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Shared.Core.Events;
using Shared.GameFormats.Wwise.Enums;

namespace Editors.Audio.AudioEditor.Presentation.AudioProjectViewer.Table
{
    public class ViewerActionEventTableService(IEventHub eventHub, IAudioEditorStateService audioEditorStateService) : IViewerTableService
    {
        private readonly IEventHub _eventHub = eventHub;
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;

        public AudioProjectTreeNodeType NodeType => AudioProjectTreeNodeType.ActionEventType;

        public void Load(DataTable table)
        {
            var schema = DefineSchema();
            ConfigureTable(schema);
            ConfigureDataGrid(schema);
            InitialiseTable(table);
        }

        private bool IsMusic => _audioEditorStateService.SelectedAudioProjectExplorerNode.IsMusicActionEvent();

        public List<string> DefineSchema()
        {
            var schema = new List<string> { TableInformation.ActionEventColumnName };

            // Mirrors the editor table: a music Action Event is only meaningful alongside the State
            // Group and State it sets, so showing the name on its own would say nothing.
            if (IsMusic)
            {
                schema.Add(TableInformation.StateGroupColumnName);
                schema.Add(TableInformation.StateColumnName);
            }

            return schema;
        }

        public void ConfigureTable(List<string> schema)
        {
            foreach (var columnName in schema)
            {
                var column = new DataColumn(columnName, typeof(string));
                _eventHub.Publish(new ViewerTableColumnAddRequestedEvent(column));
            }
        }

        public void ConfigureDataGrid(List<string> schema)
        {
            // The extra column beyond the schema is the one the viewer adds itself for row selection
            var columnsCount = schema.Count + 1;
            var columnWidth = 1.0 / columnsCount;

            foreach (var columnName in schema)
            {
                var column = DataGridTemplates.CreateColumnTemplate(columnName, columnWidth, isReadOnly: true);
                column.CellTemplate = DataGridTemplates.CreateReadOnlyTextBlockTemplate(columnName);
                _eventHub.Publish(new ViewerDataGridColumnAddedEvent(column));
            }
        }

        public void InitialiseTable(DataTable table)
        {
            var actionEventName = _audioEditorStateService.SelectedAudioProjectExplorerNode.Name;
            var gameSoundBank = Wh3SoundBankInformation.GetName(Wh3ActionEventInformation.GetSoundBank(actionEventName));
            var audioProjectNameWithoutExtension = Path.GetFileNameWithoutExtension(_audioEditorStateService.AudioProjectFileName);
            var soundBankName = $"{gameSoundBank}_{audioProjectNameWithoutExtension}";
            var soundBank = _audioEditorStateService.AudioProject.GetSoundBank(soundBankName);
            foreach (var actionEvent in soundBank.ActionEvents)
            {
                var row = table.NewRow();
                row[TableInformation.ActionEventColumnName] = actionEvent.Name;

                if (IsMusic)
                {
                    var setStateAction = actionEvent.Actions
                        .FirstOrDefault(action => action.ActionType == AkActionType.SetState);

                    row[TableInformation.StateGroupColumnName] = setStateAction?.StateGroupName ?? string.Empty;
                    row[TableInformation.StateColumnName] = setStateAction?.StateName ?? string.Empty;
                }

                _eventHub.Publish(new ViewerTableRowAddRequestedEvent(row));
            }
        }
    }
}
