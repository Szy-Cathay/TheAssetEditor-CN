using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// The conversion is immutable; the worker owns its simulation and never samples a live attachment.
internal sealed class VfxInteractiveBounds : IDisposable
{
    private static readonly SemaphoreSlim Worker = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<BoundingBox?> _pending;
    private bool _disposed, _taken;
    internal Exception? Failure { get; private set; }

    internal VfxInteractiveBounds(VfxConversion conversion,
        Func<CancellationToken, Task<BoundingBox?>>? prepare = null)
    {
        _pending = prepare == null ? Estimate(conversion, _cancellation.Token)
            : RunPreparation(prepare, _cancellation.Token);
    }

    internal static Task<BoundingBox?> Estimate(VfxConversion conversion, CancellationToken token)
        => RunPreparation(current => Task.Run(() => VfxPreviewBounds.Estimate(conversion, current), current), token);

    private static async Task<BoundingBox?> RunPreparation(Func<CancellationToken, Task<BoundingBox?>> prepare, CancellationToken token)
    {
        await Worker.WaitAsync(token).ConfigureAwait(false);
        try { return await prepare(token).ConfigureAwait(false); }
        finally { Worker.Release(); }
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
