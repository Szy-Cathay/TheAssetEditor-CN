using System.IO;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Win32;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using Shared.Ui.BaseDialogs.MathViews;

namespace Editors.KitbasherEditor.ChildEditors.PhotoStudio
{
    public partial class PhotoStudioViewModel : ObservableObject, IDisposable
    {
        private readonly Action<SaveRenderImageSettings>
            _requestCapture;
        private readonly SceneRenderParametersStore
            _sceneRenderParameterStore;
        private readonly ArcBallCamera _arcBallCamera;
        private readonly IStandardDialogs _standardDialogs;
        private readonly SynchronizationContext? _uiContext;
        private readonly IDisposable _lightingOverride;
        private bool _allowUpdates;

        [ObservableProperty]
        private Vector3ViewModel _cameraPosition;

        [ObservableProperty]
        private float _cameraYaw;

        [ObservableProperty]
        private float _cameraPitch;

        [ObservableProperty]
        private float _cameraZoom;

        [ObservableProperty]
        private Vector3ViewModel _cameraLookAt;

        [ObservableProperty]
        private float _lightIntensity;

        [ObservableProperty]
        private ColourPickerViewModel _lightColour;

        [ObservableProperty]
        private float _envLightRotationY;

        [ObservableProperty]
        private float _directLightRotationX;

        [ObservableProperty]
        private float _directLightRotationY;

        [ObservableProperty]
        private bool _doubleImageResolution = true;

        public PhotoStudioViewModel(
            RenderEngineComponent renderEngineComponent,
            SceneRenderParametersStore sceneRenderParameterStore,
            ArcBallCamera arcBallCamera,
            IStandardDialogs standardDialogs)
            : this(
                renderEngineComponent.SaveNextFrame,
                sceneRenderParameterStore,
                arcBallCamera,
                standardDialogs)
        {
        }

        internal PhotoStudioViewModel(
            Action<SaveRenderImageSettings> requestCapture,
            SceneRenderParametersStore sceneRenderParameterStore,
            ArcBallCamera arcBallCamera,
            IStandardDialogs standardDialogs)
        {
            _requestCapture = requestCapture;
            _sceneRenderParameterStore =
                sceneRenderParameterStore;
            _arcBallCamera = arcBallCamera;
            _standardDialogs = standardDialogs;
            _uiContext = SynchronizationContext.Current;
            _lightingOverride =
                sceneRenderParameterStore.BeginLightingOverride();

            _cameraPosition = new Vector3ViewModel(
                arcBallCamera.Position,
                OnCameraPositionChanged);
            _cameraLookAt = new Vector3ViewModel(
                arcBallCamera.LookAt,
                OnCameraLookAtChanged);
            _lightColour = new ColourPickerViewModel(
                sceneRenderParameterStore.LightColour,
                OnLightColourChanged);

            RefreshCameraValues();
            RefreshLightValues();
            _allowUpdates = true;
        }

        private void OnCameraPositionChanged(Vector3 position)
        {
            if (!_allowUpdates)
                return;

            if (!IsFinite(position))
            {
                RefreshCameraValues();
                return;
            }

            if (PhotoStudioCameraMath.TryCalculateOrbit(
                    position,
                    _arcBallCamera.LookAt,
                    _arcBallCamera.Yaw,
                    _arcBallCamera.MinPitch,
                    _arcBallCamera.MaxPitch,
                    out var yaw,
                    out var pitch,
                    out var zoom))
            {
                _arcBallCamera.Yaw = yaw;
                _arcBallCamera.Pitch = pitch;
                _arcBallCamera.Zoom = zoom;
            }

            RefreshCameraValues();
        }

        private void OnCameraLookAtChanged(Vector3 lookAt)
        {
            if (!_allowUpdates)
                return;

            if (!IsFinite(lookAt))
            {
                RefreshCameraValues();
                return;
            }

            _arcBallCamera.LookAt = lookAt;
            RefreshCameraValues();
        }

        private void OnLightColourChanged(Vector3 colour)
        {
            if (!_allowUpdates)
                return;

            if (!IsFinite(colour))
            {
                RefreshLightValues();
                return;
            }

            UpdateLight();
        }

        partial void OnCameraPitchChanged(float value)
        {
            if (!_allowUpdates)
                return;

            if (!float.IsFinite(value))
            {
                RefreshCameraValues();
                return;
            }

            _arcBallCamera.Pitch = value;
            RefreshCameraValues();
        }

        partial void OnCameraYawChanged(float value)
        {
            if (!_allowUpdates)
                return;

            if (!float.IsFinite(value))
            {
                RefreshCameraValues();
                return;
            }

            _arcBallCamera.Yaw = value;
            RefreshCameraValues();
        }

        partial void OnCameraZoomChanged(float value)
        {
            if (!_allowUpdates)
                return;

            if (!float.IsFinite(value) ||
                value < ArcBallCamera.MinZoom)
            {
                RefreshCameraValues();
                return;
            }

            _arcBallCamera.Zoom = value;
            RefreshCameraValues();
        }

        partial void OnLightIntensityChanged(float value)
        {
            UpdateLight();
        }

        partial void OnEnvLightRotationYChanged(float value)
        {
            UpdateLight();
        }

        partial void OnDirectLightRotationXChanged(float value)
        {
            UpdateLight();
        }

        partial void OnDirectLightRotationYChanged(float value)
        {
            UpdateLight();
        }

        private void UpdateLight()
        {
            if (!_allowUpdates)
                return;

            var lightColour = LightColour.SelectedColour;
            if (!IsFinite(lightColour) ||
                !float.IsFinite(LightIntensity) ||
                !float.IsFinite(EnvLightRotationY) ||
                !float.IsFinite(DirectLightRotationX) ||
                !float.IsFinite(DirectLightRotationY))
            {
                RefreshLightValues();
                return;
            }

            _sceneRenderParameterStore.LightColour =
                lightColour;
            _sceneRenderParameterStore.LightIntensityMult =
                LightIntensity;
            _sceneRenderParameterStore.EnvLightRotationDegrees_Y =
                EnvLightRotationY;
            _sceneRenderParameterStore.DirLightRotationDegrees_X =
                DirectLightRotationX;
            _sceneRenderParameterStore.DirLightRotationDegrees_Y =
                DirectLightRotationY;
        }

        [RelayCommand]
        private void RefreshCameraValues()
        {
            var restoreUpdates = _allowUpdates;
            _allowUpdates = false;
            CameraPosition.DisableCallbacks = true;
            CameraLookAt.DisableCallbacks = true;
            try
            {
                CameraPosition.Set(_arcBallCamera.Position);
                CameraYaw = _arcBallCamera.Yaw;
                CameraPitch = _arcBallCamera.Pitch;
                CameraZoom = _arcBallCamera.Zoom;
                CameraLookAt.Set(_arcBallCamera.LookAt);
            }
            finally
            {
                CameraPosition.DisableCallbacks = false;
                CameraLookAt.DisableCallbacks = false;
                _allowUpdates = restoreUpdates;
            }
        }

        private void RefreshLightValues()
        {
            var restoreUpdates = _allowUpdates;
            _allowUpdates = false;
            try
            {
                LightColour.Set(
                    _sceneRenderParameterStore.LightColour);
                LightIntensity =
                    _sceneRenderParameterStore.LightIntensityMult;
                EnvLightRotationY =
                    _sceneRenderParameterStore
                        .EnvLightRotationDegrees_Y;
                DirectLightRotationX =
                    _sceneRenderParameterStore
                        .DirLightRotationDegrees_X;
                DirectLightRotationY =
                    _sceneRenderParameterStore
                        .DirLightRotationDegrees_Y;
            }
            finally
            {
                _allowUpdates = restoreUpdates;
            }
        }

        [RelayCommand]
        private void SaveSettings()
        {
            var dialog = CreateSettingsSaveDialog();
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var json = JsonSerializer.Serialize(
                    CreateSettings(),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                File.WriteAllText(dialog.FileName, json);
            }
            catch (Exception exception)
            {
                _standardDialogs.ShowExceptionWindow(
                    exception,
                    GetText(
                        "KitbashTool.PhotoStudio.SaveFailed",
                        "无法保存照片工作室设置。"));
            }
        }

        internal static SaveFileDialog
            CreateSettingsSaveDialog()
        {
            return new SaveFileDialog
            {
                Filter = GetText(
                    "KitbashTool.PhotoStudio.JsonFilter",
                    "JSON 文件 (*.json)|*.json"),
                DefaultExt = "json",
                FileName = "照片工作室设置.json"
            };
        }

        [RelayCommand]
        private void ImportSettings()
        {
            var dialog = new OpenFileDialog
            {
                Filter = GetText(
                    "KitbashTool.PhotoStudio.JsonFilter",
                    "JSON 文件 (*.json)|*.json"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true ||
                string.IsNullOrEmpty(dialog.FileName))
            {
                return;
            }

            try
            {
                TryImportSettings(
                    File.ReadAllText(dialog.FileName));
            }
            catch (Exception exception)
            {
                _standardDialogs.ShowExceptionWindow(
                    exception,
                    GetText(
                        "KitbashTool.PhotoStudio.ImportFailed",
                        "无法导入照片工作室设置。"));
            }
        }

        internal bool TryImportSettings(string json)
        {
            try
            {
                var settings =
                    JsonSerializer.Deserialize<PhotoStudioSettings>(
                        json) ??
                    throw new InvalidDataException(
                        "Photo Studio settings are empty.");
                Validate(settings);
                Apply(settings);
                return true;
            }
            catch (Exception exception)
            {
                _standardDialogs.ShowExceptionWindow(
                    exception,
                    GetText(
                        "KitbashTool.PhotoStudio.ImportFailed",
                        "无法导入照片工作室设置。"));
                return false;
            }
        }

        [RelayCommand]
        private void TakeScreenshot()
        {
            var outputFolder = Path.Combine(
                DirectoryHelper.ApplicationDirectory,
                "照片工作室",
                "截图");
            _requestCapture(
                new SaveRenderImageSettings(
                    "截图",
                    true,
                    DoubleImageResolution ? 2.0f : 1.0f,
                    outputFolder)
                {
                    FailureHandler = OnCaptureFailed
                });
        }

        private void OnCaptureFailed(Exception exception)
        {
            if (_uiContext != null &&
                _uiContext != SynchronizationContext.Current)
            {
                _uiContext.Post(
                    _ => ShowCaptureFailure(exception),
                    null);
                return;
            }

            ShowCaptureFailure(exception);
        }

        private void ShowCaptureFailure(Exception exception)
        {
            _standardDialogs.ShowExceptionWindow(
                exception,
                GetText(
                    "KitbashTool.PhotoStudio.CaptureFailed",
                    "无法导出照片工作室图片。"));
        }

        private PhotoStudioSettings CreateSettings()
        {
            var position = _arcBallCamera.Position;
            var lookAt = _arcBallCamera.LookAt;
            var lightColour = LightColour.SelectedColour;
            return new PhotoStudioSettings
            {
                CameraPositionX = position.X,
                CameraPositionY = position.Y,
                CameraPositionZ = position.Z,
                CameraYaw = _arcBallCamera.Yaw,
                CameraPitch = _arcBallCamera.Pitch,
                CameraZoom = _arcBallCamera.Zoom,
                CameraLookAtX = lookAt.X,
                CameraLookAtY = lookAt.Y,
                CameraLookAtZ = lookAt.Z,
                LightIntensity = LightIntensity,
                LightColourX = lightColour.X,
                LightColourY = lightColour.Y,
                LightColourZ = lightColour.Z,
                EnvLightRotationY = EnvLightRotationY,
                DirectLightRotationX = DirectLightRotationX,
                DirectLightRotationY = DirectLightRotationY,
                DoubleImageResolution = DoubleImageResolution
            };
        }

        private void Apply(PhotoStudioSettings settings)
        {
            _allowUpdates = false;
            try
            {
                _arcBallCamera.LookAt = new Vector3(
                    settings.CameraLookAtX,
                    settings.CameraLookAtY,
                    settings.CameraLookAtZ);
                _arcBallCamera.Yaw = settings.CameraYaw;
                _arcBallCamera.Pitch = settings.CameraPitch;
                _arcBallCamera.Zoom = settings.CameraZoom;

                _sceneRenderParameterStore.LightColour =
                    new Vector3(
                        settings.LightColourX,
                        settings.LightColourY,
                        settings.LightColourZ);
                _sceneRenderParameterStore.LightIntensityMult =
                    settings.LightIntensity;
                _sceneRenderParameterStore
                    .EnvLightRotationDegrees_Y =
                    settings.EnvLightRotationY;
                _sceneRenderParameterStore
                    .DirLightRotationDegrees_X =
                    settings.DirectLightRotationX;
                _sceneRenderParameterStore
                    .DirLightRotationDegrees_Y =
                    settings.DirectLightRotationY;
                DoubleImageResolution =
                    settings.DoubleImageResolution;

                RefreshCameraValues();
                RefreshLightValues();
            }
            finally
            {
                _allowUpdates = true;
            }
        }

        private static void Validate(
            PhotoStudioSettings settings)
        {
            var values = new[]
            {
                settings.CameraPositionX,
                settings.CameraPositionY,
                settings.CameraPositionZ,
                settings.CameraYaw,
                settings.CameraPitch,
                settings.CameraZoom,
                settings.CameraLookAtX,
                settings.CameraLookAtY,
                settings.CameraLookAtZ,
                settings.LightIntensity,
                settings.LightColourX,
                settings.LightColourY,
                settings.LightColourZ,
                settings.EnvLightRotationY,
                settings.DirectLightRotationX,
                settings.DirectLightRotationY
            };
            if (values.Any(value => !float.IsFinite(value)) ||
                settings.CameraZoom < ArcBallCamera.MinZoom)
            {
                throw new InvalidDataException(
                    "Photo Studio settings contain invalid values.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z);
        }

        private static string GetText(
            string key,
            string fallback)
        {
            return LocalizationManager.Instance is { } localization
                ? localization.Get(key)
                : fallback;
        }

        public void Dispose()
        {
            _lightingOverride.Dispose();
        }
    }
}
