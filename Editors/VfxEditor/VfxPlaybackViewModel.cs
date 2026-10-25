using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GameWorld.Core.Vfx;
using Shared.Core.Misc;

namespace Editors.VfxEditor;

// Adapts the shared player's binding contract without inventing an animation frame rate.
public sealed class VfxPlaybackViewModel : NotifyPropertyChangedImpl, IDisposable
{
    private readonly VfxPreviewComponent? _component;
    private readonly Dispatcher _dispatcher;
    private float _playbackPositionSeconds;
    private bool _isScrubbing;
    private bool _updating;
    private bool _resumeAfterExpand;
    private int _refreshQueued;
    private volatile bool _disposed;

    public NotifyAttr<float> SelectedAnimationCurrentTime { get; } = new();
    public NotifyAttr<float> SelectedAnimationMaxTime { get; } = new();
    public NotifyAttr<double> SelectedAnimationFps { get; } = new();
    public NotifyAttr<int> SelectedAnimationCurrentFrame { get; } = new();
    public NotifyAttr<int> SelectedAnimationFrameCount { get; } = new();
    public NotifyAttr<bool> IsEnabled { get; } = new(true);
    public NotifyAttr<bool> IsPlaying { get; } = new();
    public NotifyAttr<bool> LoopAnimation { get; } = new(true);
    public NotifyAttr<Visibility> PlayerControlsVisibility { get; } = new(Visibility.Collapsed);
    public object? TimelineAnnotationContent => null;
    internal bool PlayingIntent => IsEnabled.Value ? _component?.IsPlaying == true : _resumeAfterExpand;

    internal void RestorePlayingIntent(bool playing)
    {
        _resumeAfterExpand = playing;
        if (!_disposed && _component != null) _component.IsPlaying = playing && IsEnabled.Value;
    }

    public float PlaybackPositionSeconds
    {
        get => _playbackPositionSeconds;
        set
        {
            if (_disposed || !float.IsFinite(value)) return;
            if (_updating)
            {
                SetAndNotifyWhenChanged(ref _playbackPositionSeconds, value);
                return;
            }
            if (_component?.IsLoaded != true || !IsEnabled.Value) return;
            var requested = Math.Clamp(value, 0, _component.Duration);
            SetAndNotifyWhenChanged(ref _playbackPositionSeconds, requested);
            _component.Seek(requested);
        }
    }

    public bool IsScrubbing
    {
        get => _isScrubbing;
        set
        {
            if (_disposed || _isScrubbing == value) return;
            SetAndNotifyWhenChanged(ref _isScrubbing, value);
            if (!_updating && _component != null)
                _component.IsScrubbing = value && IsEnabled.Value && _component.IsLoaded;
        }
    }

    public VfxPlaybackViewModel(VfxPreviewComponent? component)
    {
        _component = component;
        _dispatcher = Dispatcher.CurrentDispatcher;
        IsEnabled.PropertyChanged += EnabledChanged;
        LoopAnimation.PropertyChanged += LoopChanged;
        if (_component != null) _component.PlaybackChanged += PlaybackChanged;
        Refresh();
    }

    public void ToggleAnimationPausePlay()
    {
        if (_disposed || _component?.IsLoaded != true || !IsEnabled.Value) return;
        _component.IsPlaying = !_component.IsPlaying;
    }

    public void SetAnimationFirstFrame()
    {
        if (!_disposed && _component?.IsLoaded == true && IsEnabled.Value)
            _component.Restart();
    }

    public void SetAnimationLastFrame()
    {
        if (!_disposed && _component?.IsLoaded == true && IsEnabled.Value)
            _component.Seek(_component.Duration);
    }

    // The shared view hides frame-only commands for this adapter.
    public void SetAnimationNextFrame() { }
    public void SetAnimationPrivFrame() { }

    private void EnabledChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || _updating || args.PropertyName != nameof(NotifyAttr<bool>.Value) || _component == null) return;
        if (IsEnabled.Value)
            _component.IsPlaying = _resumeAfterExpand;
        else
        {
            _resumeAfterExpand = _component.IsPlaying;
            _component.IsScrubbing = false;
            _component.IsPlaying = false;
        }
        Refresh();
    }

    private void LoopChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_disposed && !_updating && args.PropertyName == nameof(NotifyAttr<bool>.Value) && _component != null)
            _component.Loop = LoopAnimation.Value;
    }

    private void PlaybackChanged()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        if (_dispatcher.CheckAccess())
            Refresh();
        else if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            _dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (!_disposed) Refresh();
            }));
    }

    private void Refresh()
    {
        if (_disposed) return;
        _updating = true;
        try
        {
            var loaded = _component?.IsLoaded == true;
            SelectedAnimationMaxTime.Value = loaded ? _component!.Duration : 0;
            SelectedAnimationCurrentTime.Value = loaded ? _component!.CurrentTime : 0;
            SetAndNotifyWhenChanged(ref _playbackPositionSeconds, SelectedAnimationCurrentTime.Value, propertyName: nameof(PlaybackPositionSeconds));
            IsPlaying.Value = loaded && IsEnabled.Value && _component!.IsPlaying;
            LoopAnimation.Value = _component?.Loop ?? true;
            IsScrubbing = loaded && _component!.IsScrubbing;
            PlayerControlsVisibility.Value = loaded ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updating = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_component != null) _component.PlaybackChanged -= PlaybackChanged;
        IsEnabled.PropertyChanged -= EnabledChanged;
        LoopAnimation.PropertyChanged -= LoopChanged;
    }
}
