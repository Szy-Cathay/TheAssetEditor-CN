using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Shared.Ui.Common.OperationProgress;

public sealed class OperationProgressVisibilityController
{
    public static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MinimumVisibleDuration =
        TimeSpan.FromMilliseconds(300);

    private readonly Dispatcher _dispatcher;
    private readonly Action<bool> _setVisibility;
    private bool _isActive;
    private bool _isVisible;
    private long _shownTimestamp;
    private int _version;

    public OperationProgressVisibilityController(
        Dispatcher dispatcher,
        Action<bool> setVisibility)
    {
        _dispatcher = dispatcher;
        _setVisibility = setVisibility;
        _setVisibility(false);
    }

    public void Begin()
    {
        _dispatcher.VerifyAccess();
        _isActive = true;
        var version = ++_version;
        if (!_isVisible)
            _ = RevealAfterDelayAsync(version);
    }

    public Task EndAsync()
    {
        _dispatcher.VerifyAccess();
        _isActive = false;
        var version = ++_version;
        if (!_isVisible)
        {
            _setVisibility(false);
            return Task.CompletedTask;
        }

        var remaining = MinimumVisibleDuration -
                        Stopwatch.GetElapsedTime(_shownTimestamp);
        return remaining > TimeSpan.Zero
            ? HideAfterDelayAsync(version, remaining)
            : HideAsync(version);
    }

    public void RevealImmediately()
    {
        _dispatcher.VerifyAccess();
        ++_version;
        SetVisibility(true);
    }

    public void ForceHide()
    {
        _dispatcher.VerifyAccess();
        _isActive = false;
        ++_version;
        SetVisibility(false);
    }

    private async Task RevealAfterDelayAsync(int version)
    {
        await Task.Delay(ShowDelay).ConfigureAwait(false);
        if (_dispatcher.HasShutdownStarted)
            return;

        await _dispatcher.InvokeAsync(() =>
        {
            if (_isActive && version == _version)
                SetVisibility(true);
        });
    }

    private async Task HideAfterDelayAsync(
        int version,
        TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        await HideAsync(version).ConfigureAwait(false);
    }

    private async Task HideAsync(int version)
    {
        if (_dispatcher.HasShutdownStarted)
            return;

        await _dispatcher.InvokeAsync(() =>
        {
            if (!_isActive && version == _version)
                SetVisibility(false);
        });
    }

    private void SetVisibility(bool isVisible)
    {
        if (_isVisible == isVisible)
            return;

        _isVisible = isVisible;
        if (isVisible)
            _shownTimestamp = Stopwatch.GetTimestamp();
        _setVisibility(isVisible);
    }
}
