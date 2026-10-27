#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameWorld.Core.WpfWindow;

internal sealed record D3D11FrameDiagnosticOptions(string OutputDirectory, TimeSpan Duration)
{
    internal static D3D11FrameDiagnosticOptions? TryParse(string? output, string? seconds)
    {
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output)) return null;
        var duration = 25d;
        if (seconds != null && (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
            || !double.IsFinite(duration) || duration < 20 || duration > 30)) return null;
        try { return new(Path.GetFullPath(output), TimeSpan.FromSeconds(duration)); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}

internal enum D3D11FramePhase { None, DisposeTargets, Rebuild, Render, Lock, Blit, Flush, QueryWait, Unlock }

internal struct D3D11FrameTiming
{
    public long CallbackTimestamp, CallbackIntervalTicks, CompositionTicks, PreviousCompositionTicks;
    public long TotalTicks, DisposeTargetsTicks, RebuildTicks, RenderTicks, LockTicks, BlitTicks, FlushTicks, QueryWaitTicks, UnlockTicks;
    public long AllocatedBytes, IntervalAllocatedBytes;
    public int Gc0, Gc1, Gc2, IntervalGc0, IntervalGc1, IntervalGc2, ManagedThreadId;
    public int RenderCalls, PresentAttempts, PresentCalls, QueryPolls, ResourceRebuilds;
    public int Width, Height, WidthBefore, HeightBefore;
    public double ActualWidth, ActualHeight, DpiScale;
    public bool IsVisible, IsRendering, WasSuspended, HiddenSkipped, PendingResize, PendingDpi, Faulted, HasPreviousCallback;
    public long ElapsedGameTimeTicks;
    internal long PhaseStart, AllocatedStart;
    internal int Gc0Start, Gc1Start, Gc2Start;
}

internal struct D3D11FrameInterval
{
    private long _timestamp, _allocated;
    private int _thread, _gc0, _gc1, _gc2;

    internal void Begin(ref D3D11FrameTiming sample, long timestamp, int thread, long allocated, int gc0, int gc1, int gc2)
    {
        sample.CallbackTimestamp = timestamp;
        sample.CallbackIntervalTicks = _timestamp == 0 ? 0 : timestamp - _timestamp;
        sample.ManagedThreadId = thread;
        sample.HasPreviousCallback = _timestamp != 0 && _thread == thread;
        if (sample.HasPreviousCallback)
        {
            sample.IntervalAllocatedBytes = Math.Max(0, allocated - _allocated);
            sample.IntervalGc0 = gc0 - _gc0; sample.IntervalGc1 = gc1 - _gc1; sample.IntervalGc2 = gc2 - _gc2;
        }
        _timestamp = timestamp; _thread = thread; _allocated = allocated;
        _gc0 = gc0; _gc1 = gc1; _gc2 = gc2;
    }
}

internal sealed class D3D11FrameTimingReport
{
    public long Callbacks { get; init; }
    public long DroppedSamples { get; init; }
    public long RenderCalls { get; init; }
    public long PresentAttempts { get; init; }
    public long PresentCalls { get; init; }
    public long HiddenCallbacks { get; init; }
    public long ResourceRebuilds { get; init; }
    public long QueryWaitTicks { get; init; }
    public long AllocatedBytes { get; init; }
    public D3D11FrameTiming[] Samples { get; init; } = [];
}

internal sealed class D3D11FrameTimingCollector(int capacity)
{
    private readonly object _gate = new();
    private readonly D3D11FrameTiming[] _samples = new D3D11FrameTiming[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private int _count;
    private long _callbacks, _renders, _presentAttempts, _presents, _hidden, _rebuilds, _queryWait, _allocated;
    private D3D11FrameTimingReport? _report;

    internal void Record(in D3D11FrameTiming sample)
    {
        lock (_gate)
        {
            if (_report != null) return;
            _callbacks++;
            _renders += sample.RenderCalls;
            _presentAttempts += sample.PresentAttempts;
            _presents += sample.PresentCalls;
            _hidden += sample.HiddenSkipped ? 1 : 0;
            _rebuilds += sample.ResourceRebuilds;
            _queryWait += sample.QueryWaitTicks;
            _allocated += sample.AllocatedBytes;
            if (_count < _samples.Length) _samples[_count++] = sample;
        }
    }

    internal D3D11FrameTimingReport Complete()
    {
        lock (_gate)
            return _report ??= new()
            {
                Callbacks = _callbacks, DroppedSamples = _callbacks - _count, RenderCalls = _renders,
                PresentAttempts = _presentAttempts, PresentCalls = _presents, HiddenCallbacks = _hidden,
                ResourceRebuilds = _rebuilds, QueryWaitTicks = _queryWait, AllocatedBytes = _allocated,
                Samples = _samples.AsSpan(0, _count).ToArray(),
            };
    }
}

// Debug-only caller opt-in. No buffer, timer or output exists for ordinary hosts.
internal sealed class D3D11HostFrameDiagnostics : IDisposable
{
    private const int Capacity = 8192;
    private static int _nextHost;
    private readonly D3D11FrameDiagnosticOptions _options;
    private readonly D3D11FrameTimingCollector _collector = new(Capacity);
    private readonly Timer _timer;
    private readonly string _hostType, _hostName, _hostUid;
    private readonly int _hostId = Interlocked.Increment(ref _nextHost);
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly long _deadline;
    private long _phaseStarted, _callbackStarts;
    private D3D11FrameInterval _interval;
    private int _finished, _phase, _callbacksInFlight;

    private D3D11HostFrameDiagnostics(D3D11FrameDiagnosticOptions options, string hostType, string hostName, string hostUid)
    {
        _options = options; _hostType = hostType; _hostName = hostName; _hostUid = hostUid;
        _deadline = _started + (long)(options.Duration.TotalSeconds * Stopwatch.Frequency);
        _timer = new(_ => Finish("wall_clock_deadline"), null, options.Duration, Timeout.InfiniteTimeSpan);
    }

    internal static D3D11HostFrameDiagnostics? TryCreate(bool eligible, D3D11Host? host)
    {
        if (!eligible) return null;
        var options = D3D11FrameDiagnosticOptions.TryParse(
            Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_FRAME_DIAGNOSTICS_OUTPUT"),
            Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_FRAME_DIAGNOSTICS_SECONDS"));
        return options == null || host == null ? null
            : new(options, host.GetType().FullName ?? host.GetType().Name, host.Name, host.Uid);
    }

    internal bool IsCollecting => Volatile.Read(ref _finished) == 0;

    internal void BeginCallback(ref D3D11FrameTiming sample)
    {
        var timestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _callbackStarts);
        sample.AllocatedStart = GC.GetAllocatedBytesForCurrentThread();
        sample.Gc0Start = GC.CollectionCount(0); sample.Gc1Start = GC.CollectionCount(1); sample.Gc2Start = GC.CollectionCount(2);
        _interval.Begin(ref sample, timestamp, Environment.CurrentManagedThreadId, sample.AllocatedStart,
            sample.Gc0Start, sample.Gc1Start, sample.Gc2Start);
        Interlocked.Increment(ref _callbacksInFlight);
    }

    internal void StartPhase(ref D3D11FrameTiming sample, D3D11FramePhase phase)
    {
        sample.PhaseStart = Stopwatch.GetTimestamp();
        Volatile.Write(ref _phaseStarted, sample.PhaseStart);
        Volatile.Write(ref _phase, (int)phase);
    }

    internal void EndPhase(ref D3D11FrameTiming sample, D3D11FramePhase phase)
    {
        var ticks = Stopwatch.GetTimestamp() - sample.PhaseStart;
        switch (phase)
        {
            case D3D11FramePhase.DisposeTargets: sample.DisposeTargetsTicks += ticks; break;
            case D3D11FramePhase.Rebuild: sample.RebuildTicks += ticks; sample.ResourceRebuilds++; break;
            case D3D11FramePhase.Render: sample.RenderTicks += ticks; sample.RenderCalls++; break;
            case D3D11FramePhase.Lock: sample.LockTicks += ticks; break;
            case D3D11FramePhase.Blit: sample.BlitTicks += ticks; break;
            case D3D11FramePhase.Flush: sample.FlushTicks += ticks; break;
            case D3D11FramePhase.QueryWait: sample.QueryWaitTicks += ticks; break;
            case D3D11FramePhase.Unlock: sample.UnlockTicks += ticks; break;
        }
        Volatile.Write(ref _phase, (int)D3D11FramePhase.None);
    }

    internal void EndCallback(ref D3D11FrameTiming sample)
    {
        var now = Stopwatch.GetTimestamp();
        sample.TotalTicks = now - sample.CallbackTimestamp;
        sample.AllocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - sample.AllocatedStart);
        sample.Gc0 = GC.CollectionCount(0) - sample.Gc0Start;
        sample.Gc1 = GC.CollectionCount(1) - sample.Gc1Start;
        sample.Gc2 = GC.CollectionCount(2) - sample.Gc2Start;
        if (now <= _deadline && IsCollecting) _collector.Record(sample);
        Interlocked.Decrement(ref _callbacksInFlight);
    }

    private void Finish(string reason)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        _timer.Dispose();
        var report = _collector.Complete();
        var completed = Stopwatch.GetTimestamp();
        var activePhase = (D3D11FramePhase)Volatile.Read(ref _phase);
        var inFlight = Volatile.Read(ref _callbacksInFlight);
        var callbackStarts = Interlocked.Read(ref _callbackStarts);
        var phaseTicks = activePhase == D3D11FramePhase.None ? 0 : completed - Volatile.Read(ref _phaseStarted);
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(_options.OutputDirectory);
                var path = Path.Combine(_options.OutputDirectory,
                    $"host-frame-{Environment.ProcessId}-{_hostId}-{_startedUtc:yyyyMMddTHHmmssfff}.json");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(stream, new
                {
                    Schema = 1, HostId = _hostId, ProcessId = Environment.ProcessId, HostType = _hostType,
                    HostName = _hostName, HostUid = _hostUid, CoreAssembly = typeof(D3D11Host).Assembly.Location,
                    CoreModuleVersionId = typeof(D3D11Host).Module.ModuleVersionId, StartedUtc = _startedUtc,
                    SamplingSeconds = _options.Duration.TotalSeconds, StopwatchFrequency = Stopwatch.Frequency,
                    StartedTimestamp = _started, DeadlineTimestamp = _deadline, CompletionTimestamp = completed,
                    ActualCollectionSeconds = (completed - _started) / (double)Stopwatch.Frequency,
                    StoppedBeforeDeadline = completed < _deadline, HostOptIn = "SuspendWhenHidden=true",
                    CompletionReason = reason, BufferCapacity = Capacity, CallbacksInFlightAtCompletion = inFlight,
                    CallbackStarts = callbackStarts,
                    ActivePhaseAtCompletion = activePhase.ToString(), ActivePhaseElapsedTicks = phaseTicks,
                    Boundary = "Completed UI callbacks only; callbacks in flight at the wall-clock deadline are excluded. ActivePhaseAtCompletion is a best-effort cross-thread snapshot. Sampling starts at host opt-in, before scene loading. Phase times include diagnostic clock overhead. AllocatedBytes covers this callback; IntervalAllocatedBytes covers Begin-to-Begin on the same managed thread, including between-callback UI work. HasPreviousCallback guards interval counters. GC counts are process-wide. Final batch export is excluded. No GPU timestamp, texture readback, or FPS claim. Zero fields are retained.",
                    Timing = report,
                }, new JsonSerializerOptions { IncludeFields = true });
            }
            catch (Exception exception) { Trace.WriteLine($"AECN frame diagnostic export failed: {exception}"); }
        });
    }

    public void Dispose() => Finish("host_disposed_or_opt_out");
}
