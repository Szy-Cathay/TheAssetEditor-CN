using Shared.Core.Misc;

namespace Editors.CscEditor.Services
{
    /// <summary>
    /// Scene-time and selection state shared by the animation component, the drawable nodes
    /// (which evaluate their channels live at the current time) and the view models.
    /// </summary>
    public class CscPlaybackContext : NotifyPropertyChangedImpl
    {
        float _currentTime;
        public float CurrentTime { get => _currentTime; set => SetAndNotify(ref _currentTime, value); }

        float _duration = 20;
        public float Duration { get => _duration; set => SetAndNotify(ref _duration, value); }

        bool _isPlaying;
        public bool IsPlaying { get => _isPlaying; set => SetAndNotify(ref _isPlaying, value); }

        bool _loop = true;
        public bool Loop { get => _loop; set => SetAndNotify(ref _loop, value); }

        /// <summary>Element id of the current selection, or -1. Selected elements stay visible
        /// (and highlighted) even outside their active time window.</summary>
        public int SelectedElementId { get; set; } = -1;

        int _lookThroughElementId = -1;
        /// <summary>Camera element id the viewport is currently looking through, or -1 for the
        /// normal arc-ball view. Applied every frame by CscAnimationComponent.</summary>
        public int LookThroughElementId { get => _lookThroughElementId; set => SetAndNotify(ref _lookThroughElementId, value); }
    }
}
