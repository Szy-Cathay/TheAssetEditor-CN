using GameWorld.Core.Components.Rendering;
using Shared.Core.Misc;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KitbasherEditor.ViewModels.MenuBarViews
{
    public class ViewportShadingViewModel : NotifyPropertyChangedImpl
    {
        private readonly RenderEngineComponent _renderEngine;
        private readonly SceneRenderParametersStore _sceneLighting;
        public ICommand ResetCommand { get; }
        private ViewportShadingSettings Settings => _renderEngine.ShadingSettings;
        public bool HasSurface => !IsWireframe;

        public ViewportShadingViewModel(
            RenderEngineComponent renderEngine,
            SceneRenderParametersStore? sceneLighting = null)
        {
            _renderEngine = renderEngine;
            _sceneLighting = sceneLighting ?? new();
            _renderEngine.ShadingSettings = Settings with { CavityStrength = 0.5f, ShadowStrength = 0.6f };
            ResetCommand = new RelayCommand(ResetCurrentMode);
            _renderEngine.ShadingMode =
                ViewportShadingMode.MaterialPreview;
            _renderEngine.ShadingModeChanged += OnShadingModeChanged;
        }

        public bool IsWireframe
        {
            get => _renderEngine.ShadingMode ==
                ViewportShadingMode.Wireframe;
            set
            {
                if (value)
                    _renderEngine.ShadingMode =
                        ViewportShadingMode.Wireframe;
            }
        }

        public bool IsSolid
        {
            get => _renderEngine.ShadingMode ==
                ViewportShadingMode.Solid;
            set
            {
                if (value)
                    _renderEngine.ShadingMode =
                        ViewportShadingMode.Solid;
            }
        }

        public bool IsMaterialPreview
        {
            get => _renderEngine.ShadingMode ==
                ViewportShadingMode.MaterialPreview;
            set
            {
                if (value)
                    _renderEngine.ShadingMode =
                        ViewportShadingMode.MaterialPreview;
            }
        }

        public int SolidLighting
        {
            get => (int)Settings.SolidLighting;
            set { if (Enum.IsDefined((ViewportSolidLighting)value)) SetSettings(Settings with { SolidLighting = (ViewportSolidLighting)value }); }
        }

        public double CavityStrength
        {
            get => Settings.CavityStrength;
            set { if (double.IsFinite(value)) SetSettings(Settings with { CavityStrength = (float)Math.Clamp(value, 0, 1) }); }
        }

        public double ShadowStrength
        {
            get => Settings.ShadowStrength;
            set { if (double.IsFinite(value)) SetSettings(Settings with { ShadowStrength = (float)Math.Clamp(value, 0, 1) }); }
        }

        public double XRayOpacity
        {
            get => Settings.XRayOpacity;
            set { if (double.IsFinite(value)) SetSettings(Settings with { XRayOpacity = (float)Math.Clamp(value, 0.05, 1) }); }
        }

        public double WireframeOpacity
        {
            get => Settings.WireframeOpacity;
            set { if (double.IsFinite(value)) SetSettings(Settings with { WireframeOpacity = (float)Math.Clamp(value, 0.1, 1) }); }
        }

        private ViewportShadingSettings LocalLighting => Settings.UseLocalLighting ? Settings : Settings with
        {
            UseLocalLighting = true,
            LightIntensity = _sceneLighting.LightIntensityMult,
            EnvironmentRotation = _sceneLighting.EnvLightRotationDegrees_Y
        };

        public int Environment
        {
            get => (int)Settings.Environment;
            set { if (Enum.IsDefined((ViewportEnvironment)value)) SetSettings(LocalLighting with { Environment = (ViewportEnvironment)value }); }
        }

        public double LightIntensity
        {
            get => Settings.UseLocalLighting ? Settings.LightIntensity : _sceneLighting.LightIntensityMult;
            set { if (double.IsFinite(value)) SetSettings(LocalLighting with { LightIntensity = (float)Math.Clamp(value, 0.1, 3) }); }
        }

        public double EnvironmentRotation
        {
            get => Settings.UseLocalLighting ? Settings.EnvironmentRotation : _sceneLighting.EnvLightRotationDegrees_Y;
            set { if (double.IsFinite(value)) SetSettings(LocalLighting with { EnvironmentRotation = (float)Math.Clamp(value, 0, 360) }); }
        }

        private void SetSettings(ViewportShadingSettings settings, [CallerMemberName] string? propertyName = null)
        {
            if (Settings == settings)
                return;
            _renderEngine.ShadingSettings = settings;
            NotifyPropertyChanged(propertyName!);
        }

        public void RefreshSettings()
        {
            foreach (var name in new[] { nameof(SolidLighting), nameof(CavityStrength), nameof(ShadowStrength),
                nameof(XRayOpacity), nameof(WireframeOpacity), nameof(Environment), nameof(LightIntensity), nameof(EnvironmentRotation) })
                NotifyPropertyChanged(name);
        }

        private void ResetCurrentMode()
        {
            if (IsMaterialPreview)
                _renderEngine.ShadingSettings = Settings with { UseLocalLighting = false, Environment = ViewportEnvironment.Game };
            else if (IsSolid)
                _renderEngine.ShadingSettings = Settings with { SolidLighting = ViewportSolidLighting.Studio, CavityStrength = 0.5f, ShadowStrength = 0.6f, XRayOpacity = 0.35f };
            else
                _renderEngine.ShadingSettings = Settings with { WireframeOpacity = 1, XRayOpacity = 0.35f };
            RefreshSettings();
        }

        private void OnShadingModeChanged(
            ViewportShadingMode shadingMode)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(NotifyModeProperties);
                return;
            }

            NotifyModeProperties();
        }

        private void NotifyModeProperties()
        {
            NotifyPropertyChanged(nameof(IsWireframe));
            NotifyPropertyChanged(nameof(IsSolid));
            NotifyPropertyChanged(nameof(IsMaterialPreview));
            NotifyPropertyChanged(nameof(HasSurface));
        }
    }
}
