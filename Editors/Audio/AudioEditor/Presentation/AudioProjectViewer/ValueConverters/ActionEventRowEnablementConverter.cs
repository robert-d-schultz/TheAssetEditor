using System;
using System.Data;
using System.Globalization;
using System.Windows.Data;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;

namespace Editors.Audio.AudioEditor.Presentation.AudioProjectViewer.ValueConverters
{
    [ValueConversion(typeof(object[]), typeof(bool))]
    public class ActionEventRowEnablementConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not DataRowView rowView || values[1] is not AudioProjectTreeNode selectedNode)
                return false;

            if (!selectedNode.IsActionEvent())
                return false;

            var row = rowView.Row;
            if (row == null)
                return false;

            if (row.RowState == DataRowState.Detached || row.RowState == DataRowState.Deleted)
                return false;

            // The selected node changes before the grid swaps its rows over, so for one layout pass
            // this can be handed a row belonging to the previously selected node's table - a State
            // Group row, say, which has no Action Event column. Indexing it by name throws rather
            // than returning null, so the column has to be checked for.
            if (!row.Table.Columns.Contains(TableInformation.ActionEventColumnName))
                return false;

            var actionEventName = row[TableInformation.ActionEventColumnName] as string;
            if (string.IsNullOrWhiteSpace(actionEventName))
                return false;

            return actionEventName.StartsWith("Stop_", StringComparison.Ordinal)
                || actionEventName.StartsWith("Resume_", StringComparison.Ordinal)
                || actionEventName.StartsWith("Pause_", StringComparison.Ordinal);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
