using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// The conversion is immutable; the worker owns its simulation and never samples a live attachment.
internal sealed class VfxInteractiveBounds : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<BoundingBox?> _pending;
    private bool _disposed, _taken;
    internal Exception? Failure { get; private set; }

    internal VfxInteractiveBounds(VfxConversion conversion,
        Func<CancellationToken, Task<BoundingBox?>>? prepare = null)
    {
        _pending = VfxInteractivePrewarm.RunPreparation(
            prepare ?? (token => Task.Run(() => VfxPreviewBounds.Estimate(conversion, token), token)),
            _cancellation.Token);
    }

    internal bool TryTake(out BoundingBox? bounds)
    {
        bounds = null;
        if (_disposed || _taken || !_pending.IsCompleted) return false;
        _taken = true;
        try { bounds = _pending.GetAwaiter().GetResult(); return true; }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception) { Failure = exception; return false; }
    }

    internal async Task StopAsync()
    {
        Dispose();
        try { await _pending.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Failure = exception; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _ = _pending.ContinueWith(completed => { _ = completed.Exception; _cancellation.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
