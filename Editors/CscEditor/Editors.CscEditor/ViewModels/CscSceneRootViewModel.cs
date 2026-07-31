using System;
using System.Collections.ObjectModel;
using Editors.CscEditor.Data;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;

namespace Editors.CscEditor.ViewModels
{
    /// <summary>Synthetic tree root above the top-level elements - lets the component tree show a
    /// single "Root" node, gives drag-drop a visible target for detaching an element back to top
    /// level (dropping on it behaves the same as dropping on empty space), and doubles as the
    /// selectable/editable adapter over the .csc's own ROOT header fields (scene duration, focus
    /// point, radius, weather/environment path).</summary>
    public class CscSceneRootViewModel : NotifyPropertyChangedImpl
    {
        public ObservableCollection<CscElementViewModel> Children { get; }
        public string DisplayName { get; }

        readonly Action _onModified;
        readonly LocalizationManager _localization;

        /// <summary>Set by the owning view model on load/reload. Null (and every header-field
        /// property a harmless default) before a file is loaded.</summary>
        public CscScene? Scene { get; private set; }

        public void SetScene(CscScene? scene)
        {
            Scene = scene;
            NotifyPropertyChanged(nameof(SceneDuration));
            NotifyPropertyChanged(nameof(FocusPointX));
            NotifyPropertyChanged(nameof(FocusPointY));
            NotifyPropertyChanged(nameof(FocusPointZ));
            NotifyPropertyChanged(nameof(Radius));
            NotifyPropertyChanged(nameof(HasWeatherPath));
            NotifyPropertyChanged(nameof(WeatherPath));
        }

        double _duration;
        /// <summary>The scene's overall *computed* playback duration (seconds) - the longest of the
        /// ROOT header's own Duration field and every element's own end time, kept in sync by the
        /// owning view model whenever it changes, so the root tree item doubles as a duration
        /// readout. Distinct from <see cref="SceneDuration"/>, which is the raw, directly-authored
        /// ROOT header field shown/edited in the details panel.</summary>
        public double Duration
        {
            get => _duration;
            set => SetAndNotify(ref _duration, value, _ => NotifyPropertyChanged(nameof(Subtitle)));
        }

        public string Subtitle => _localization.GetFormat("Csc.Root.Duration", Duration);

        bool _isExpanded = true;
        public bool IsExpanded { get => _isExpanded; set => SetAndNotify(ref _isExpanded, value); }

        bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetAndNotify(ref _isSelected, value); }

        public CscSceneRootViewModel(
            ObservableCollection<CscElementViewModel> children,
            string displayName,
            Action onModified,
            LocalizationManager localization)
        {
            Children = children;
            DisplayName = displayName;
            _onModified = onModified;
            _localization = localization;
        }

        void Modified() => _onModified();

        // ---------------------------------------------------------------------
        // ROOT header fields (see RootStructureDetector / CscScene for field-position docs)
        // ---------------------------------------------------------------------

        public float SceneDuration
        {
            get => Scene?.Duration ?? 0f;
            set { if (Scene != null) { Scene.Duration = value; NotifyPropertyChanged(); Modified(); } }
        }

        public float FocusPointX
        {
            get => Scene?.FocusPoint.X ?? 0f;
            set { if (Scene != null) { var p = Scene.FocusPoint; Scene.FocusPoint = new Vector3(value, p.Y, p.Z); NotifyPropertyChanged(); Modified(); } }
        }

        public float FocusPointY
        {
            get => Scene?.FocusPoint.Y ?? 0f;
            set { if (Scene != null) { var p = Scene.FocusPoint; Scene.FocusPoint = new Vector3(p.X, value, p.Z); NotifyPropertyChanged(); Modified(); } }
        }

        public float FocusPointZ
        {
            get => Scene?.FocusPoint.Z ?? 0f;
            set { if (Scene != null) { var p = Scene.FocusPoint; Scene.FocusPoint = new Vector3(p.X, p.Y, value); NotifyPropertyChanged(); Modified(); } }
        }

        public float Radius
        {
            get => Scene?.Radius ?? 0f;
            set { if (Scene != null) { Scene.Radius = value; NotifyPropertyChanged(); Modified(); } }
        }

        public bool HasWeatherPath => Scene?.HasWeatherPath ?? false;

        public string WeatherPath
        {
            get => Scene?.WeatherPath ?? "";
            set { if (Scene != null) { Scene.WeatherPath = value; NotifyPropertyChanged(); Modified(); } }
        }
    }
}
