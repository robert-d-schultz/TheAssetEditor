using System.Collections.Generic;
using System.Data;
using System.Linq;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Table;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.Presentation.AudioProjectEditor.Table
{
    public class EditorActionEventTableService(
        IUiCommandFactory uiCommandFactory,
        IEventHub eventHub,
        IAudioEditorStateService audioEditorStateService,
        IAudioRepository audioRepository) : IEditorTableService
    {
        private readonly IUiCommandFactory _uiCommandFactory = uiCommandFactory;
        private readonly IEventHub _eventHub = eventHub;
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IAudioRepository _audioRepository = audioRepository;

        public AudioProjectTreeNodeType NodeType => AudioProjectTreeNodeType.ActionEventType;

        private bool IsMusic => _audioEditorStateService.SelectedAudioProjectExplorerNode.IsMusicActionEvent();
        private bool IsMovie => _audioEditorStateService.SelectedAudioProjectExplorerNode.IsMovieActionEvent();

        public void Load(DataTable table)
        {
            var schema = DefineSchema();
            ConfigureTable(schema);
            ConfigureDataGrid(schema);
            InitialiseTable(table);
        }

        public List<string> DefineSchema()
        {
            var schema = new List<string> { TableInformation.ActionEventColumnName };

            // A music Action Event doesn't play anything - it sets a State - so it needs to say
            // which State Group and which State, the way a Dialogue Event row does.
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
                _eventHub.Publish(new EditorTableColumnAddRequestedEvent(column));
            }
        }

        public void ConfigureDataGrid(List<string> schema)
        {
            var columnWidth = 1.0 / schema.Count;

            if (IsMovie)
            {
                var fileSelectColumnHeader = TableInformation.BrowseMovieColumnName;
                var fileSelectColumn = DataGridTemplates.CreateColumnTemplate(fileSelectColumnHeader, 85, useAbsoluteWidth: true);
                fileSelectColumn.CellTemplate = DataGridTemplates.CreateFileSelectButtonCellTemplate(_uiCommandFactory);
                _eventHub.Publish(new EditorDataGridColumnAddRequestedEvent(fileSelectColumn));

                foreach (var columnName in schema)
                {
                    // We don't allow editing because the Event name must match the path of the movie
                    var eventColumn = DataGridTemplates.CreateColumnTemplate(columnName, columnWidth, isReadOnly: true);
                    eventColumn.CellTemplate = DataGridTemplates.CreateReadOnlyTextBlockTemplate(columnName);
                    _eventHub.Publish(new EditorDataGridColumnAddRequestedEvent(eventColumn));
                }

                return;
            }

            foreach (var columnName in schema)
            {
                var column = DataGridTemplates.CreateColumnTemplate(columnName, columnWidth);
                column.CellTemplate = CreateCellTemplate(columnName);
                _eventHub.Publish(new EditorDataGridColumnAddRequestedEvent(column));
            }
        }

        private System.Windows.DataTemplate CreateCellTemplate(string columnName)
        {
            if (columnName == TableInformation.StateGroupColumnName)
                return DataGridTemplates.CreateStatesComboBoxTemplate(_eventHub, columnName, Wh3StateGroupInformation.MusicStateGroups.ToList());

            if (columnName == TableInformation.StateColumnName)
                return DataGridTemplates.CreateStatesComboBoxTemplate(_eventHub, columnName, GetMusicStates());

            // Music Action Events set a State rather than playing a Sound, so their names don't
            // take the Play_ prefix that the rest of the Action Events do.
            return DataGridTemplates.CreateEditableEventTextBoxTemplate(_eventHub, columnName, forcePlayPrefix: !IsMusic);
        }

        /// <summary>
        /// Every State any of the music State Groups already uses, plus whatever the project has
        /// added to them. The combo box is editable, so a State that doesn't exist yet - which is
        /// the whole point of adding music for a new culture - can just be typed in.
        /// </summary>
        private List<string> GetMusicStates()
        {
            return Wh3StateGroupInformation.MusicStateGroups
                .SelectMany(stateGroup => TableHelpers.GetStatesForStateGroupColumn(_audioEditorStateService, _audioRepository, stateGroup))
                .Where(state => state != "Any") // A music event has to name one State, "Any" means nothing here
                .Distinct()
                .OrderBy(state => state)
                .ToList();
        }

        public void InitialiseTable(DataTable editorTable)
        {
            var row = editorTable.NewRow();

            // Movie events take their name from the file that gets browsed for, and music events
            // are not named Play_anything, so neither is seeded with the prefix.
            if (!IsMovie && !IsMusic)
                row[TableInformation.ActionEventColumnName] = "Play_";
            else
                row[TableInformation.ActionEventColumnName] = string.Empty;

            _eventHub.Publish(new EditorTableRowAddRequestedEvent(row));
        }
    }
}
