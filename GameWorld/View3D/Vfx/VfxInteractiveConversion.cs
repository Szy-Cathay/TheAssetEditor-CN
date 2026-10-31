using System.Collections.Concurrent;
using System.Diagnostics;

namespace GameWorld.Core.Vfx;

internal sealed record VfxPreparedConversion(VfxConversion Conversion, string? ResourceIdentity, string[] ResourcePaths,
    VfxPreparedResources? Resources = null);

// CPU work owns its projection. Pack services and unsaved documents are read only by the owner pump.
internal sealed class VfxInteractiveConversion : IDisposable
{
    private static readonly SemaphoreSlim Workers = new(2, 2);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentQueue<ResourceRequest> _requests = new();
    private readonly Dictionary<string, ResourceRequest> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedResources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, byte[]?> _resolve;
    private readonly Func<string, Func<byte[]?>?>? _captureResourceRead;
    private readonly Task<VfxPreparedConversion> _pending;
    private Action<Action>? _scheduleOwnerWork;
    private int _ownerPumpScheduled;
    private volatile bool _disposed;

    internal VfxInteractiveConversion(VfxPreviewSnapshot snapshot, bool collectEditorInfo, IEnumerable<string>? previousPaths = null,
        string? reusableResourceIdentity = null, Action<Action>? scheduleOwnerWork = null)
    {
        _resolve = snapshot.Resolve;
        _captureResourceRead = snapshot.CaptureResourceRead;
        _scheduleOwnerWork = scheduleOwnerWork;
        foreach (var path in previousPaths ?? []) GetRequest(path);
        var captured = snapshot with
        {
            GameBytes = snapshot.GameBytes.ToArray(),
            RootSelection = snapshot.RootSelection is { } selection
                ? new(selection.EmitterIds.ToArray(), selection.ReferenceNames.ToArray()) : null,
            Resolve = Resolve,
            CaptureResourceRead = null,
        };
        _pending = Run(captured, collectEditorInfo, reusableResourceIdentity, _cancellation.Token);
    }

    private async Task<VfxPreparedConversion> Run(VfxPreviewSnapshot snapshot, bool collectEditorInfo,
        string? reusableResourceIdentity, CancellationToken token)
    {
        await Workers.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var conversion = TotalWarVfxConverter.Convert(snapshot, collectEditorInfo, token, VfxPreviewXmlCache.Shared);
                token.ThrowIfCancellationRequested();
                var identity = conversion.Emitters.Count > 0 && VfxPlaybackResourceIdentity.CanReuse(conversion)
                    ? VfxPlaybackResourceIdentity.Create(conversion) : null;
                var resources = conversion.Emitters.Count == 0 ? new VfxPreparedResources([], [])
                    : identity != null && identity == reusableResourceIdentity ? null : VfxPreparedResources.Prepare(conversion, token);
                return new VfxPreparedConversion(conversion, identity, _usedResources.ToArray(), resources);
            }, token).ConfigureAwait(false);
        }
        finally { Workers.Release(); }
    }

    internal static async Task<VfxPreparedResources> PrepareResourcesAsync(VfxConversion conversion, CancellationToken token)
    {
        await Workers.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => VfxPreparedResources.Prepare(conversion, token), token).ConfigureAwait(false); }
        finally { Workers.Release(); }
    }

    private ResourceRequest GetRequest(string path)
    {
        if (_resources.TryGetValue(path, out var cached)) return cached;
        var request = new ResourceRequest(path);
        _resources.Add(path, request);
        _requests.Enqueue(request);
        ScheduleOwnerPump();
        return request;
    }

    private void ScheduleOwnerPump()
    {
        if (_disposed || _scheduleOwnerWork is not { } schedule || Interlocked.CompareExchange(ref _ownerPumpScheduled, 1, 0) != 0) return;
        try { schedule(PumpScheduledResources); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A shutting down owner may reject dispatches. Explicit frame pumps remain available.
            _scheduleOwnerWork = null;
            Volatile.Write(ref _ownerPumpScheduled, 0);
        }
    }

    private void PumpScheduledResources()
    {
        try
        {
            if (!_disposed) PumpResources(TimeSpan.FromMilliseconds(2));
        }
        finally
        {
            Volatile.Write(ref _ownerPumpScheduled, 0);
            if (!_requests.IsEmpty) ScheduleOwnerPump();
        }
    }

    private byte[]? Resolve(string path)
    {
        var token = _cancellation.Token;
        token.ThrowIfCancellationRequested();
        _usedResources.Add(path);
        var result = GetRequest(path).Result.Task;
        if (result.IsCompleted) return ReadCaptured(result.GetAwaiter().GetResult(), token);
        // A suspended viewport may not pump its owner. Waiting must not monopolize CPU slots.
        Workers.Release();
        Lazy<byte[]?> captured;
        try { captured = result.WaitAsync(token).GetAwaiter().GetResult(); }
        finally { Workers.Wait(); }
        return ReadCaptured(captured, token);
    }

    private static byte[]? ReadCaptured(Lazy<byte[]?> captured, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bytes = captured.Value;
        token.ThrowIfCancellationRequested();
        return bytes;
    }

    internal bool TryTake(out VfxPreparedConversion? result, TimeSpan? budget = null)
    {
        result = null;
        if (_disposed) return false;
        PumpResources(budget ?? TimeSpan.FromMilliseconds(2));
        if (!_pending.IsCompleted) return false;
        result = _pending.GetAwaiter().GetResult();
        return true;
    }

    private void PumpResources(TimeSpan budget)
    {
        var started = Stopwatch.GetTimestamp();
        while (_requests.TryDequeue(out var request))
        {
            try
            {
                Func<byte[]?>? read;
                if (_captureResourceRead != null) read = _captureResourceRead(request.Path);
                else
                {
                    var bytes = _resolve(request.Path);
                    read = () => bytes;
                }
                request.Result.TrySetResult(new(read ?? (() => null), LazyThreadSafetyMode.ExecutionAndPublication));
            }
            catch (Exception exception) { request.Result.TrySetException(exception); }
            if (Stopwatch.GetElapsedTime(started) >= budget) break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _ = _pending.ContinueWith(completed => { _ = completed.Exception; _cancellation.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record ResourceRequest(string Path)
    {
        public TaskCompletionSource<Lazy<byte[]?>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
