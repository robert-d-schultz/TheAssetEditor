﻿using CommonControls.FileTypes.Animation;
using AssetManagement.GenericFormats.DataStructures.Managed;
using CommonControls.Common;

namespace AssetManagement.Strategies.Fbx.ImportDialog.DataModels
{
    public class FbxSettingsModel : NotifyPropertyChangedImpl
    {
        public FBXFileInfo FileInfoData { get; set; } = new FBXFileInfo();
        public AnimationFile SkeletonPackFile { get; set; } = null;
        public string SkeletonFileName { get; set; } = "";
        public string SkeletonName { get; set; } = "";
        public bool ApplyRiggingData { get; set; } = true;      

    }
}
