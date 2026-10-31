#nullable enable
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal readonly record struct VfxCapturedReplayOrigin(Matrix Attachment, Matrix? HistoryRootAtZero,
    float? GroundHeight, bool CurrentAttachment, bool ContinuousTrail, bool HasNext, float? EmissionEnd);
internal readonly record struct VfxConstantReloadContext(Matrix Attachment, Matrix? HistoryRootAtZero,
    float? GroundHeight, float? EmissionEnd, bool InitializeDuration, bool UseCurrentAttachmentForFrame, bool HasNext)
{
    internal VfxCapturedReplayOrigin? ReplayOrigin { get; init; }
}
internal sealed record VfxConstantReloadResult(TotalWarVfxSimulation Simulation, float Time);

// The owner certifies constant history. Only captured values and an unpublished simulation enter the worker.
internal sealed class VfxConstantReloadPreparation : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<VfxConstantReloadResult> _pending;
    private bool _disposed, _taken;
    internal VfxConstantReloadContext Context { get; }
    internal float RequestedTime { get; }
    internal bool IsCompleted => _pending.IsCompleted;
    internal Exception? Failure { get; private set; }

    internal static VfxConstantReloadContext Capture(VfxInteractiveReloadFrame frame)
        => new(frame.Attachment, frame.History?.Invoke(0), frame.GroundHeight, frame.EmissionEnd,
            frame.InitializeDuration, frame.UseCurrentAttachmentForFrame, frame.NextAttachmentSample != null);

    internal VfxConstantReloadPreparation(VfxConversion conversion, VfxConstantReloadContext context,
        float requestedTime, bool checkpoints, TotalWarVfxSimulation? existing = null)
    {
        if (!float.IsFinite(requestedTime)) throw new ArgumentOutOfRangeException(nameof(requestedTime));
        Context = context;
        RequestedTime = Math.Max(0, requestedTime);
        _pending = VfxInteractivePrewarm.RunPreparation(token => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            Func<float, Matrix>? history = context.HistoryRootAtZero is { } root ? _ => root : null;
            Func<float, float?>? next = context.HasNext ? _ => null : null;
            var simulation = existing;
            if (simulation == null)
            {
                simulation = new(conversion) { EmissionEndTimeSeconds = context.EmissionEnd };
                if (checkpoints) simulation.EnableSeekCheckpoints();
                if (context.ReplayOrigin is { } origin)
                {
                    Func<float, Matrix>? originalHistory = origin.HistoryRootAtZero is { } initial ? _ => initial : null;
                    simulation.InitializeReplayOrigin(new(origin.Attachment, originalHistory, origin.GroundHeight,
                        origin.CurrentAttachment, origin.ContinuousTrail, origin.HasNext ? _ => null : null, origin.EmissionEnd));
                }
                else
                {
                    simulation.PrepareInitialGround(context.GroundHeight, context.Attachment, history,
                        context.UseCurrentAttachmentForFrame, false, next);
                    if (context.InitializeDuration) _ = simulation.CycleDuration;
                }
            }
            var time = context.InitializeDuration ? Math.Min(RequestedTime, simulation.CycleDuration) : RequestedTime;
            simulation.SeekFrameForPreparation(time, context.Attachment, history,
                context.UseCurrentAttachmentForFrame, next, token);
            return new VfxConstantReloadResult(simulation, time);
        }, token), _cancellation.Token);
    }

    internal bool TryTake(out VfxConstantReloadResult? prepared)
    {
        prepared = null;
        if (_disposed || _taken || !_pending.IsCompleted) return false;
        _taken = true;
        try { prepared = _pending.GetAwaiter().GetResult(); return true; }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception) { Failure = exception; return false; }
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
