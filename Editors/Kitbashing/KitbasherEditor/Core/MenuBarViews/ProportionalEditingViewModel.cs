using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.EmbeddedResources;

namespace KitbasherEditor.ViewModels.MenuBarViews
{
    /// <summary>
    /// ViewModel for the proportional editing toggle button in the toolbar.
    /// Provides on/off toggle with falloff distance adjustment.
    /// </summary>
    public class ProportionalEditingViewModel : NotifyPropertyChangedImpl
    {
        private const double MinimumFalloffDistance = 0.001;
        private readonly SelectionManager _selectionManager;

        private bool _isEnabled = false;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CurrentIcon));
                UpdateSelectionManagerFalloff();
            }
        }

        private double _falloffDistance = 1.0;
        public double FalloffDistance
        {
            get => _falloffDistance;
            set
            {
                var normalizedValue =
                    double.IsFinite(value) &&
                    value >= MinimumFalloffDistance
                        ? value
                        : MinimumFalloffDistance;
                if (_falloffDistance == normalizedValue)
                    return;

                _falloffDistance = normalizedValue;
                NotifyPropertyChanged();
                UpdateSelectionManagerFalloff();
            }
        }

        /// <summary>
        /// Returns the appropriate icon based on enabled state
        /// </summary>
        public BitmapImage CurrentIcon => IsEnabled ? IconLibrary.ProportionalOnIcon : IconLibrary.ProportionalOffIcon;

        /// <summary>
        /// Visibility - only visible when in edit mode (Vertex/Face/Edge)
        /// </summary>
        public NotifyAttr<bool> IsVisible { get; } = new NotifyAttr<bool>(false);

        public System.Windows.Input.ICommand ToggleCommand { get; }

        public ProportionalEditingViewModel(SelectionManager selectionManager, IEventHub eventHub)
        {
            _selectionManager = selectionManager;
            ToggleCommand = new RelayCommand(Toggle);

            eventHub.Register<SelectionChangedEvent>(this, HandleSelectionChanged);
        }

        private void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        private void UpdateSelectionManagerFalloff()
        {
            // When disabled, pass 0 to disable falloff
            // When enabled, pass the falloff distance
            float falloff = IsEnabled ? (float)FalloffDistance : 0f;
            _selectionManager.UpdateVertexSelectionFallof(falloff);
        }

        private void HandleSelectionChanged(SelectionChangedEvent notification)
        {
            var mode = notification.NewState?.Mode;
            // Visible only in edit modes (Vertex/Face/Edge), not Object mode
            IsVisible.Value = mode == GeometrySelectionMode.Vertex
                           || mode == GeometrySelectionMode.Edge
                           || mode == GeometrySelectionMode.Face;

            // Persist falloff state across mode switches - don't reset IsEnabled
            // When returning to edit mode, the previous settings will still be active
            // Update the SelectionManager with current falloff value when entering edit mode
            if (IsVisible.Value)
                UpdateSelectionManagerFalloff();
            else
                _selectionManager.UpdateVertexSelectionFallof(0f);
        }
    }
}
