using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace Shared.Ui.Common.OperationProgress;

public sealed class OperationProgressWindowTransition
{
    public static readonly DependencyProperty KeepTitleBarProperty =
        DependencyProperty.RegisterAttached("KeepTitleBar", typeof(bool),
            typeof(OperationProgressWindowTransition), new PropertyMetadata(false));

    public static bool GetKeepTitleBar(DependencyObject element) =>
        (bool)element.GetValue(KeepTitleBarProperty);

    private readonly Window _window;
    private readonly Func<bool> _animationsEnabled;
    private bool _rendered;
    private bool _visible;
    private bool _closed;
    private bool _revealPending;
    private double _target;
    private ScaleTransform? _scale;
    private TaskCompletionSource? _completion;

    public OperationProgressWindowTransition(Window window)
        : this(window, () => SystemParameters.ClientAreaAnimation &&
                             SystemParameters.MinimizeAnimation)
    {
    }

    internal OperationProgressWindowTransition(Window window, Func<bool> animationsEnabled)
    {
        _window = window;
        _animationsEnabled = animationsEnabled;
        // Per-pixel alpha hides the HWND as well as its WPF content. An ordinary
        // opaque HWND shows its clear colour when only the visual root is faded.
        window.SetValue(KeepTitleBarProperty, true);
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Opacity = 0;
        window.IsHitTestVisible = false;
        if (WindowChrome.GetWindowChrome(window) is { } chrome)
        {
            var progressChrome = (WindowChrome)chrome.Clone();
            progressChrome.GlassFrameThickness = new Thickness(0);
            WindowChrome.SetWindowChrome(window, progressChrome);
        }
        window.SourceInitialized += OnSourceInitialized;
        window.ContentRendered += OnContentRendered;
        window.Closed += OnClosed;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public void Show()
    {
        _window.Dispatcher.VerifyAccess();
        if (_closed)
            return;
        _visible = true;
        if (_rendered)
        {
            _window.IsHitTestVisible = true;
            if (!_revealPending)
            {
                _revealPending = true;
                CompositionTarget.Rendering += OnRevealFrame;
            }
        }
    }

    public Task HideAsync()
    {
        _window.Dispatcher.VerifyAccess();
        _visible = false;
        _window.IsHitTestVisible = false;
        if (_closed)
            return Task.CompletedTask;
        if (_completion != null && _target == 0)
            return _completion.Task;
        return AnimateTo(0, "AeMotion.WindowExit");
    }

    public void HideImmediately()
    {
        _window.Dispatcher.VerifyAccess();
        _visible = false;
        _window.IsHitTestVisible = false;
        StopAnimation();
        _window.Opacity = 0;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Do not combine DWM's show/close snapshots with our rendered transition.
        var disabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(_window).Handle,
            3, ref disabled, sizeof(int));
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _window.ContentRendered -= OnContentRendered;
        _rendered = true;
        if (_window.Template.FindName("WindowSurface", _window) is FrameworkElement surface)
        {
            var initialScale = GetOverlayScale();
            _scale = new ScaleTransform(initialScale, initialScale);
            surface.RenderTransformOrigin = new Point(0.5, 0.5);
            surface.RenderTransform = _scale;
        }
        if (_visible)
            Show();
    }

    private Task AnimateTo(double target, string durationKey)
    {
        var from = _window.Opacity;
        var scaleFrom = _scale?.ScaleX ?? 1;
        var scaleTarget = target == 1 ? 1 : GetOverlayScale();
        StopAnimation();
        _window.Opacity = target;
        if (_scale != null)
            _scale.ScaleX = _scale.ScaleY = scaleTarget;
        var duration = _window.TryFindResource(durationKey) as Duration? ?? new Duration(TimeSpan.Zero);
        if (from == target || !_animationsEnabled() || duration.TimeSpan <= TimeSpan.Zero)
            return Task.CompletedTask;

        _target = target;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        var animation = new DoubleAnimation(from, target, duration)
        {
            EasingFunction = new CubicEase { EasingMode = target == 1 ? EasingMode.EaseOut : EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (ReferenceEquals(_completion, completion))
                StopAnimation();
        };
        _window.BeginAnimation(UIElement.OpacityProperty, animation);
        if (_scale != null)
        {
            var scaleAnimation = new DoubleAnimation(scaleFrom, scaleTarget, duration)
            {
                EasingFunction = new CubicEase { EasingMode = target == 1 ? EasingMode.EaseOut : EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop,
            };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }
        return completion.Task;
    }

    private double GetOverlayScale() =>
        _window.TryFindResource("AeMotion.WindowScale") as double? ?? 1;

    private void OnRevealFrame(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRevealFrame;
        _revealPending = false;
        if (_visible && !_closed)
            _ = AnimateTo(1, "AeMotion.WindowEnter");
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.MinimizeAnimation))
        {
            _window.Dispatcher.InvokeAsync(() =>
            {
                if (!_animationsEnabled())
                    StopAnimation();
            });
        }
    }

    private void StopAnimation()
    {
        CompositionTarget.Rendering -= OnRevealFrame;
        _revealPending = false;
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        _scale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _completion?.TrySetResult();
        _completion = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        StopAnimation();
        _window.SourceInitialized -= OnSourceInitialized;
        _window.ContentRendered -= OnContentRendered;
        _window.Closed -= OnClosed;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
