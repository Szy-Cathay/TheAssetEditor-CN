using GameWorld.Core.Components.Rendering;
using Shared.Core.Misc;
using System.Windows;

namespace KitbasherEditor.ViewModels.MenuBarViews
{
    public class ViewportShadingViewModel : NotifyPropertyChangedImpl
    {
        private readonly RenderEngineComponent _renderEngine;

        public ViewportShadingViewModel(
            RenderEngineComponent renderEngine)
        {
            _renderEngine = renderEngine;
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
        }
    }
}
