﻿using CommonControls.Common;
using CommonControls.FileTypes.RigidModel;
using CommonControls.FileTypes.RigidModel.Types;
using CommonControls.PackFileBrowser;
using CommonControls.Services;
using MonoGame.Framework.WpfInterop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using TextureEditor.ViewModels;
using View3D.Components.Component;
using View3D.SceneNodes;

namespace KitbasherEditor.ViewModels.SceneExplorerNodeViews.Rmv2
{
    public class MaterialGeneralViewModel : NotifyPropertyChangedImpl
    {
        public class TextureViewModel : NotifyPropertyChangedImpl, INotifyDataErrorInfo
        {
            PackFileService _packfileService;
            Rmv2MeshNode _meshNode;
            TexureType _texureType;

            bool _useTexture = true;
            public bool UseTexture { get { return _useTexture; } set { SetAndNotify(ref _useTexture, value); UpdatePreviewTexture(value); } }
            public string TextureTypeStr { get; set; }

            public string Path
            {
                get => _path;
                set
                {
                    if (_path == value)
                        return;

                    _path = value;
                    ValidateTexturePath();
                    NotifyPropertyChanged();

                    UpdateTexturePath(value);
                }
            }

            public NotifyAttr<bool> IsVisible { get; set; } = new NotifyAttr<bool>(true);

            private readonly Dictionary<string, List<string>> _errorsByPropertyName = new Dictionary<string, List<string>>();
            private string _path = "";

            public TextureViewModel(Rmv2MeshNode meshNode, PackFileService packfileService, TexureType texureType)
            {
                _packfileService = packfileService;
                _meshNode = meshNode;
                _texureType = texureType;
                TextureTypeStr = _texureType.ToString();
                Path = _meshNode.Material.GetTexture(texureType)?.Path;
            }

            public void Preview() => TexturePreviewController.CreateWindow(Path, _packfileService);

            public void Browse()
            {
                using (var browser = new PackFileBrowserWindow(_packfileService))
                {
                    browser.ViewModel.Filter.SetExtentions(new List<string>() { ".dds", ".png", });
                    if (browser.ShowDialog() == true && browser.SelectedFile != null)
                    {
                        try
                        {
                            var path = _packfileService.GetFullPath(browser.SelectedFile);
                            UpdateTexturePath(path);  
                        }
                        catch
                        {
                            UpdatePreviewTexture(false);
                        }
                    }
                }
            }

            private void ValidateTexturePath()
            {
                if (Path == null)
                    return;

                var path = Path.Replace("/", @"\");

                if (!_packfileService.Database.PackFiles.Any(pf => pf.FileList.Any(pair => pair.Key.Equals(path, StringComparison.OrdinalIgnoreCase))))
                {
                    var errorMessage = "Invalid Texture Path!" +
                                       (TextureTypeStr == "Mask" ? " This is fine on mask textures." : "");
                    _errorsByPropertyName[nameof(Path)] = new List<string>() {errorMessage};
                }
                else
                {
                    _errorsByPropertyName[nameof(Path)] = null;
                }

                OnErrorsChanged(nameof(Path));
            }

            public void Remove()
            {
                UseTexture = false;
                UpdateTexturePath("");
            }

            void UpdateTexturePath(string newPath)
            {
                Path = newPath;
                _meshNode.UpdateTexture(Path, _texureType);
            }

            void UpdatePreviewTexture(bool value)
            {
                _meshNode.UseTexture(_texureType, value);
            }

            public IEnumerable GetErrors(string propertyName)
            {
                return _errorsByPropertyName.ContainsKey(propertyName) ?
                    _errorsByPropertyName[propertyName] : null;
            }

            public bool HasErrors => _errorsByPropertyName.Any();
            public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

            private void OnErrorsChanged(string propertyName)
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            }
        }

        Rmv2MeshNode _meshNode;
        IComponentManager _componentManager;

        public bool UseAlpha {
            get { return _meshNode.Material.AlphaMode == AlphaMode.Transparent; } 
            set 
            {   if (value)
                    _meshNode.Material.AlphaMode = AlphaMode.Transparent;
                else
                     _meshNode.Material.AlphaMode = AlphaMode.Opaque;
                NotifyPropertyChanged();
            } 
        }
        public string TextureDirectory { get { return _meshNode.Material.TextureDirectory; } set { _meshNode.Material.TextureDirectory = value; NotifyPropertyChanged(); } }
        public ObservableCollection<TextureViewModel> TextureList { get; set; } = new ObservableCollection<TextureViewModel>();


        bool _onlyShowUsedTextures = true;
        public bool OnlyShowUsedTextures { get { return _onlyShowUsedTextures; } set { SetAndNotify(ref _onlyShowUsedTextures, value); UpdateTextureListVisibility(value); } }

        public UiVertexFormat VertexType { get { return _meshNode.Geometry.VertexFormat; } set { ChangeVertexType(value); } }
        public IEnumerable<UiVertexFormat> PossibleVertexTypes { get; set; }

        public MaterialGeneralViewModel(Rmv2MeshNode meshNode, PackFileService pf, IComponentManager componentManager)
        {
            _componentManager = componentManager;
            _meshNode = meshNode;
            PossibleVertexTypes = new UiVertexFormat[] { UiVertexFormat.Static, UiVertexFormat.Weighted, UiVertexFormat.Cinematic };
     
            var enumValues = Enum.GetValues(typeof(TexureType)).Cast<TexureType>().ToList();
            var textureEnumValues = _meshNode.Material.GetAllTextures().Select(x => x.TexureType).ToList();
            enumValues.AddRange(textureEnumValues);
            var distinctEnumList = enumValues.Distinct();

            foreach (var enumValue in distinctEnumList)
            {
                var textureView = new TextureViewModel(_meshNode, pf, enumValue);
                TextureList.Add(textureView);
            }

            UpdateTextureListVisibility(OnlyShowUsedTextures);
        }

        void UpdateTextureListVisibility(bool onlyShowUsedTextures)
        {
            foreach (var texture in TextureList)
            {
                if (onlyShowUsedTextures == false)
                    texture.IsVisible.Value = true;
                else
                    texture.IsVisible.Value = !string.IsNullOrWhiteSpace(texture.Path);
            }
        }

        void ChangeVertexType(UiVertexFormat newFormat)
        {
            var mainNode = _componentManager.GetComponent<IEditableMeshResolver>();
            var skeletonName = mainNode.GeEditableMeshRootNode()?.Skeleton.Name;
            _meshNode.Geometry.ChangeVertexType(newFormat, skeletonName);
            NotifyPropertyChanged(nameof(VertexType));
        }
    }
}