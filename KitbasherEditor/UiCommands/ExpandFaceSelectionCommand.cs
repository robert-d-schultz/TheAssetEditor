﻿using CommonControls.Common.MenuSystem;
using CommonControls.Events.UiCommands;
using KitbasherEditor.ViewModels.MenuBarViews;
using System.Windows.Input;
using View3D.Components.Component.Selection;
using View3D.Services;

namespace KitbasherEditor.ViewModels.UiCommands
{
    public class ExpandFaceSelectionCommand : IKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Grow selection";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.FaceSelected;
        public Hotkey HotKey { get; } = null;

        FaceEditor _faceEditor;
        SelectionManager _selectionManager;
        WindowKeyboard _keyboard;

        public ExpandFaceSelectionCommand(FaceEditor faceEditor, SelectionManager selectionManager, WindowKeyboard keyboard)
        {
            _faceEditor = faceEditor;
            _selectionManager = selectionManager;
            _keyboard = keyboard;
        }

        public void Execute()
        {
            _faceEditor.GrowSelection(_selectionManager.GetState() as FaceSelectionState, !_keyboard.IsKeyDown(Key.LeftAlt));
        }
    }
}
