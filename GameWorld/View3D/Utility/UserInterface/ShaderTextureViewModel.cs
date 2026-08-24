using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Materials.Capabilities.Utility;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace GameWorld.Core.Utility.UserInterface
{
    public partial class ShaderTextureViewModel : ObservableObject, INotifyDataErrorInfo
    {
        private readonly TextureInput _shaderTextureReference;
        private readonly IPackFileService _packFileService;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IScopedResourceLibrary _resourceLibrary;
        private readonly IStandardDialogs _packFileUiProvider;
        private readonly IDocumentPropertyEditor? _propertyEditor;

        [ObservableProperty] string _path;
        [ObservableProperty] bool _shouldRenderTexture;

        public ShaderTextureViewModel(
            TextureInput shaderTextureReference,
            IPackFileService packFileService,
            IUiCommandFactory uiCommandFactory,
            IScopedResourceLibrary resourceLibrary,
            IStandardDialogs packFileUiProvider,
            IDocumentPropertyEditor? propertyEditor = null)
        {
            _shaderTextureReference = shaderTextureReference;
            _packFileService = packFileService;
            _uiCommandFactory = uiCommandFactory;
            _resourceLibrary = resourceLibrary;
            _packFileUiProvider = packFileUiProvider;
            _propertyEditor = propertyEditor;
            _shouldRenderTexture = _shaderTextureReference.UseTexture;
            Path = _shaderTextureReference.TexturePath;
        }

        partial void OnShouldRenderTextureChanged(bool value) 
        {
            if (_propertyEditor == null)
                _shaderTextureReference.UseTexture = value;
            else
                _propertyEditor.Update(
                    _shaderTextureReference.UseTexture,
                    value,
                    newValue => _shaderTextureReference.UseTexture = newValue,
                    newValue => ShouldRenderTexture = newValue);
            ValidatePath();
        }

        partial void OnPathChanged(string value)
        {
            if (_propertyEditor == null)
                _shaderTextureReference.TexturePath = value;
            else
                _propertyEditor.Update(
                    _shaderTextureReference.TexturePath,
                    value,
                    newValue => _shaderTextureReference.TexturePath = newValue,
                    newValue => Path = newValue);
            ValidatePath();
        }

        [RelayCommand]
        void HandlePreviewTexture()
        {
            if (HasErrors == false)
            {
                TexturePathResolver.FindTextureFile(
                    _packFileService,
                    Path,
                    out var resolvedPath);
                _uiCommandFactory
                    .Create<OpenEditorCommand>()
                    .ExecuteAsWindow(resolvedPath, 800, 900);
            }
        }

        [RelayCommand]
        void HandleBrowseTexture()
        {
            var result = _packFileUiProvider.DisplayBrowseDialog([".dds", ".png"]);
            if (result.Result == true && result.File != null)
            {
                try
                {
                    var path = _packFileService.GetFullPath(result.File);
                    _resourceLibrary.LoadTexture(path);

                    Path = path;
                    ShouldRenderTexture = true;
                }
                catch
                {
                    MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.TextureLoadFailed", result.File));
                    ShouldRenderTexture = false;
                }
            }
        }

        [RelayCommand]
        void HandleClearTexture()
        {
            Path = "test_mask.dds";
        }

        void ValidatePath()
        {
            _errorsByPropertyName[nameof(Path)] = new List<string>();
            if (ShouldRenderTexture && string.IsNullOrWhiteSpace(Path))
            {
                _errorsByPropertyName[nameof(Path)].Add(
                    GetText("Kitbash.Texture.PathRequired"));
            }
            else if (ShouldRenderTexture)
            {
                if (Path.Contains("test_mask.dds") == false)
                {
                    var isFileFound = TexturePathResolver.FindTextureFile(
                        _packFileService,
                        Path,
                        out _) != null;
                    if (isFileFound == false)
                    {
                        _errorsByPropertyName[nameof(Path)].Add(
                            GetText("Kitbash.Texture.PathNotFound"));
                    }
                }
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Path)));
        }

        private static string GetText(string key) =>
            LocalizationManager.Instance?.Get(key) ?? key;

        private readonly Dictionary<string, List<string>> _errorsByPropertyName = [];
        public bool HasErrors => _errorsByPropertyName.Sum(x=>x.Value.Count) != 0;

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return Enumerable.Empty<string>();

            if (_errorsByPropertyName.ContainsKey(propertyName))
                return _errorsByPropertyName[propertyName];

            return Enumerable.Empty<string>();
        }

    }
}
