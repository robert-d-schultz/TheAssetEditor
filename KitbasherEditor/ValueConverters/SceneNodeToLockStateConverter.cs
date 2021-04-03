﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using View3D.SceneNodes;

namespace KitbasherEditor.ValueConverters
{
   /* [ValueConversion(typeof(SceneNode), typeof(Visibility))]
    public class SceneNodeToLockStateConverter : IValueConverter
    {

        public SceneNodeToLockStateConverter()
        {
            // set defaults
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var node = value as SceneNode;
            if (node.IsEditable == true && node is ISelectable selectable)
            {
                if (selectable.IsSelectable == false)
                {
                    if (node is Rmv2ModelNode)
                        return Visibility.Visible;
                    if (node is Rmv2MeshNode)
                        return Visibility.Visible;
                }
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }*/


    public class SceneNodeToLockStateConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var node = values[0] as SceneNode;
            if (node.IsEditable == true)
            {
                if (node is ISelectable selectable)
                {
                    if (selectable.IsSelectable == false)
                    {
                        if (node is Rmv2ModelNode)
                            return Visibility.Visible;
                        if (node is Rmv2MeshNode)
                            return Visibility.Visible;
                    }
                }
                else if (node is GroupNode groupNode)
                {
                    if (groupNode.IsSelectable == false && groupNode.IsLockable)
                        return Visibility.Visible;
                }
            }

            return Visibility.Collapsed;

        }

        public object[] ConvertBack(
            object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}