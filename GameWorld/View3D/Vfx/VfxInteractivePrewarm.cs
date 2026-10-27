using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal readonly record struct VfxInteractivePrewarmKey(Matrix RootAtZero, Matrix Attachment, float? GroundHeight, float? EmissionEnd);
internal sealed record VfxPreparedPlayback(VfxInteractivePrewarmKey Key, TotalWarVfxSimulation Simulation, BoundingBox? Bounds);

// Each worker owns its complete simulation, including Random and particle state. No GPU or live history enters it.
internal sealed class VfxInteractivePrewarm : IDisposable
{
    private static readonly SemaphoreSlim Worker = new(1, 1);
    private readonly VfxConversion _conversion;
    private readonly Func<VfxInteractivePrewarmKey, bool, BoundingBox?, CancellationToken, Task<VfxPreparedPlayback>> _start;
    private Task<VfxPreparedPlayback>? _pending;
    private VfxPreparedPlayback? _ready;
    private VfxInteractivePrewarmKey? _pendingKey;
    private CancellationTokenSource? _cancellation;
    private BoundingBox? _bounds;
    private bool _boundsReady, _playbackTransferred, _disposed;
    internal bool Supported { get; }
    internal Exception? Failure { get; private set; }

    internal VfxInteractivePrewarm(VfxConversion conversion,
        Func<VfxInteractivePrewarmKey, bool, BoundingBox?, CancellationToken, Task<VfxPreparedPlayback>>? start = null)
    {
        _conversion = conversion;
        Supported = CanPrepare(conversion);
        _start = start ?? Start;
    }

    internal static bool CanPrepare(VfxConversion conversion)
    {
        if (conversion.Emitters.Count == 0 || conversion.Models.Count != 0) return false;
        var negative = false;
        foreach (var emitter in conversion.Emitters)
        {
            if (emitter.Kind != 0 || emitter.IsEventTemplate || emitter.IsEventHelper || emitter.Events.Count != 0
                || emitter.IsMotionTrail || emitter.RequiresFlatTerrain || emitter.SimulationSpace != 2
                || emitter.SpawnInheritedSpeed != 0 || emitter.Render.MeshMaterialIndex >= 0
                || emitter.AdditionalModelParts.Count != 0 || emitter.ModelVariants.Count != 0
                || !emitter.Position.IsConstant || !emitter.Orientation.IsConstant) return false;
            var earliest = Math.Min(emitter.Start.Min, emitter.Start.Max);
            foreach (var reference in emitter.References)
                earliest = Math.Min(reference.Start.Min, reference.Start.Max) + earliest / reference.Speed;
            if (!float.IsFinite(earliest) || earliest < -600) return false;
            negative |= earliest < 0;
        }
        return negative;
    }

    internal bool Request(VfxInteractivePrewarmKey key)
    {
        if (_disposed || !Supported || Failure != null || !Finite(key)) return false;
        if (_ready != null && _ready.Key != key) _ready = null;
        if (_pending != null && _pendingKey != key) _cancellation?.Cancel();
        if (_pending?.IsCompleted == true)
        {
            var completed = _pending;
            _pending = null;
            _cancellation?.Dispose(); _cancellation = null; _pendingKey = null;
            try
            {
                var result = completed.GetAwaiter().GetResult();
                _bounds = result.Bounds; _boundsReady = true;
                if (result.Key == key) _ready = result;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Failure = exception; return false; }
        }
        if (_pending == null && _ready == null) Schedule(key);
        return true;
    }

    internal bool TryTake(VfxInteractivePrewarmKey key, out VfxPreparedPlayback? playback)
    {
        playback = null;
        if (!Request(key) || _ready is not { } ready || ready.Key != key) return false;
        playback = ready;
        _ready = null;
        _playbackTransferred = true;
        // Keep at most the current playback and one independently prepared successor.
        Schedule(key);
        return true;
    }

    private void Schedule(VfxInteractivePrewarmKey key)
    {
        _cancellation = new(); _pendingKey = key;
        _pending = RunExclusive(key, _boundsReady, _bounds, _cancellation.Token);
    }

    private async Task<VfxPreparedPlayback> RunExclusive(VfxInteractivePrewarmKey key, bool boundsReady, BoundingBox? bounds, CancellationToken token)
        => await RunPreparation(current => _start(key, boundsReady, bounds, current), token).ConfigureAwait(false);

    internal static async Task<T> RunPreparation<T>(Func<CancellationToken, Task<T>> prepare, CancellationToken token)
    {
        await Worker.WaitAsync(token).ConfigureAwait(false);
        try { return await prepare(token).ConfigureAwait(false); }
        finally { Worker.Release(); }
    }

    private Task<VfxPreparedPlayback> Start(VfxInteractivePrewarmKey key, bool boundsReady, BoundingBox? cachedBounds, CancellationToken token)
    {
        var initializeCycleDuration = !_playbackTransferred;
        TotalWarVfxSimulation PrepareSimulation()
        {
            token.ThrowIfCancellationRequested();
            var simulation = new TotalWarVfxSimulation(_conversion) { EmissionEndTimeSeconds = key.EmissionEnd };
            // The component's first duration read resets before its first attachment seek; later rewinds reset inside Seek.
            if (initializeCycleDuration) _ = simulation.CycleDuration;
            simulation.SeekForPreparation(0, key.RootAtZero, token);
            return simulation;
        }
        async Task<VfxPreparedPlayback> PrepareInitialTogether()
        {
            // RunExclusive retains the one global permit until both independent CPU tasks finish.
            var bounds = Task.Run(() => VfxPreviewBounds.Estimate(_conversion, token), token);
            var simulation = Task.Run(PrepareSimulation, token);
            await Task.WhenAll(bounds, simulation).ConfigureAwait(false);
            return new(key, simulation.Result, bounds.Result);
        }
        if (!boundsReady && Environment.ProcessorCount >= 4) return PrepareInitialTogether();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var bounds = boundsReady ? cachedBounds : VfxPreviewBounds.Estimate(_conversion, token);
            return new VfxPreparedPlayback(key, PrepareSimulation(), bounds);
        }, token);
    }

    private static bool Finite(VfxInteractivePrewarmKey key)
    {
        static bool MatrixFinite(Matrix matrix)
            => float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
                && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
                && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
                && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
        return MatrixFinite(key.RootAtZero) && MatrixFinite(key.Attachment)
            && (key.GroundHeight == null || float.IsFinite(key.GroundHeight.Value))
            && (key.EmissionEnd == null || float.IsFinite(key.EmissionEnd.Value));
    }

    internal async Task StopAsync()
    {
        var pending = _pending;
        Dispose();
        if (pending == null) return;
        try { await pending.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Failure = exception; }
    }

    public void Dispose()
    {
        _disposed = true;
        _ready = null;
        var cancellation = _cancellation;
        cancellation?.Cancel();
        if (_pending != null && cancellation != null)
            _ = _pending.ContinueWith(completed => { _ = completed.Exception; cancellation.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else cancellation?.Dispose();
        _cancellation = null; _pendingKey = null;
        _pending = null;
    }
}
