using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

public sealed partial class EffekseerVfxPreview
{
    private bool _interactiveFrameSeekEnabled, _normalSeekInitializationKnown, _normalSeekInitializeDuration;
    private bool _normalSeekOwnerInitializesDuration;
    private bool _normalSeekAllowForwardReplay = true;
    private float? _normalSeekGroundHeight;
    private NormalFrameSeek? _normalSeekCurrent, _normalSeekLatest;
    private TotalWarVfxSimulation? _normalSeekSimulation;
    private VfxConstantReloadPreparation? _normalSeekWorker;

    public bool IsSeekingFrame => _normalSeekCurrent != null;

    public void ConfigureInteractiveSeekFrame(bool isHistoryConstant, float? groundHeight, bool initializeDuration = false, bool allowForwardReplay = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (groundHeight is { } height && !float.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(groundHeight));
        if (_normalSeekGroundHeight != groundHeight || _interactiveSeekHistoryConstant != isHistoryConstant)
            CancelNormalFrameSeek();
        if (_normalSeekOwnerInitializesDuration != initializeDuration)
        {
            CancelNormalFrameSeek();
            _normalSeekInitializationKnown = false;
        }
        _interactiveFrameSeekEnabled = true;
        _normalSeekGroundHeight = groundHeight;
        _normalSeekOwnerInitializesDuration = initializeDuration;
        _normalSeekAllowForwardReplay = allowForwardReplay;
        if (!_normalSeekInitializationKnown)
        {
            _normalSeekInitializeDuration = initializeDuration;
            _normalSeekInitializationKnown = true;
        }
        ConfigureInteractiveSeekHistory(isHistoryConstant);
    }

    private bool PrepareNormalFrameSeek(float seconds, Matrix attachment, Func<float, Matrix>? history, bool current)
    {
        if (!_interactiveFrameSeekEnabled || _conversion == null || _simulation == null || _simulation.HasMotionTrailEmitters) return true;
        if (!float.IsFinite(seconds) || seconds < 0 || seconds > 600) throw new ArgumentOutOfRangeException(nameof(seconds));
        // A changed cutoff may encode already played emission history. Preserve its original owner route.
        if (_simulation.ReplayOrigin is { } previous && previous.EmissionEnd != _emissionEndTimeSeconds)
        {
            CancelNormalFrameSeek();
            return true;
        }
        // Keep the existing prepared natural-loop zero state instead of replaying its negative interval again.
        if (_normalSeekCurrent == null && (IsPreparing || seconds == 0 && _interactivePrewarm != null)) return true;
        var reset = !_simulation.IsInitialized || seconds + .00001f < _simulation.Time;
        if (_normalSeekCurrent == null && !reset && (!_normalSeekAllowForwardReplay || seconds - _simulation.Time <= 3f / 120 + .000001f)
            && !_simulation.NeedsGroundReplay(_normalSeekGroundHeight)) return true;
        if (_normalSeekCurrent == null && _interactiveSeekHistoryConstant && _simulation.IsInitialized
            && _simulation.ReplayAttachment != attachment) return true;
        var request = new NormalFrameSeek(seconds, attachment, history, history?.Invoke(0), current,
            _interactiveSeekHistoryConstant, _normalSeekGroundHeight, _emissionEndTimeSeconds,
            reset ? false : _normalSeekInitializeDuration);
        if (_normalSeekCurrent is { } active)
        {
            if (!SameNormalScope(active, request))
            {
                CancelNormalFrameSeek();
                BeginNormalFrameSeek(request);
                return false;
            }
            _normalSeekLatest = SameNormalRequest(active, request) ? null : request;
            PumpNormalFrameSeek();
            return false;
        }
        BeginNormalFrameSeek(request);
        return false;
    }

    private void BeginNormalFrameSeek(NormalFrameSeek request)
    {
        request = request with { InitializeDuration = !_simulation!.IsInitialized || request.Time + .00001f < _simulation.Time
            ? false : _normalSeekInitializeDuration };
        _normalSeekCurrent = request;
        var origin = _simulation.IsInitialized && request.Time + .00001f >= _simulation.Time
            && !_simulation.NeedsGroundReplay(request.GroundHeight) ? _simulation.ReplayOrigin : null;
        TotalWarVfxSimulation? simulation = null;
        if (origin != null && !request.ConstantHistory)
        {
            simulation = CreateSimulation(_conversion!);
            simulation.InitializeReplayOrigin(origin);
        }
        if (request.ConstantHistory)
        {
            var context = new VfxConstantReloadContext(request.Attachment, request.HistoryRootAtZero,
                request.GroundHeight, request.EmissionEnd, false, request.CurrentAttachment, false);
            if (origin != null)
                context = context with { ReplayOrigin = new VfxCapturedReplayOrigin(origin.Attachment, origin.History?.Invoke(0),
                    origin.GroundHeight, origin.CurrentAttachment, origin.ContinuousTrail,
                    origin.NextAttachmentSample != null, origin.EmissionEnd) };
            _normalSeekWorker = new(_conversion!, context, request.Time, _interactivePrewarmEnabled);
        }
        else
        {
            if (simulation == null)
            {
                simulation = CreateSimulation(_conversion!);
                simulation.PrepareInitialGround(request.GroundHeight, request.Attachment, request.History,
                    request.CurrentAttachment, false, null);
                if (request.InitializeDuration) _ = simulation.CycleDuration;
            }
            _normalSeekSimulation = simulation;
        }
    }

    private void PrepareNormalFrameSeek(Matrix attachment, Func<float, Matrix>? history)
    {
        if (_normalSeekCurrent == null) return;
        var expected = _normalSeekLatest ?? _normalSeekCurrent;
        if (expected.Attachment != attachment || expected.HistoryRootAtZero != history?.Invoke(0)
            || expected.GroundHeight != _normalSeekGroundHeight || expected.EmissionEnd != _emissionEndTimeSeconds)
        {
            CancelNormalFrameSeek();
            return;
        }
        PumpNormalFrameSeek();
    }

    private void PumpNormalFrameSeek()
    {
        if (_normalSeekCurrent is not { } request) return;
        TotalWarVfxSimulation prepared;
        if (_normalSeekWorker is { } worker)
        {
            if (!worker.TryTake(out var result))
            {
                if (worker.Failure is { } failure) throw new InvalidOperationException("VFX requested frame preparation failed.", failure);
                return;
            }
            prepared = result!.Simulation;
            worker.Dispose();
            _normalSeekWorker = null;
        }
        else
        {
            prepared = _normalSeekSimulation!;
            if (!prepared.PrepareInteractiveSeek(request.Time, request.Attachment, request.History, request.CurrentAttachment)) return;
        }
        // The worker/sliced owner has finished. Only a complete simulation becomes visible to Draw.
        _simulation = prepared;
        _normalSeekInitializeDuration = request.InitializeDuration;
        _normalSeekSimulation = null;
        _normalSeekCurrent = null;
        var latest = _normalSeekLatest;
        _normalSeekLatest = null;
        ReportCapacity();
        if (latest != null && !SameNormalRequest(request, latest)) BeginNormalFrameSeek(latest);
    }

    private void CancelNormalFrameSeek()
    {
        _normalSeekWorker?.Dispose();
        _normalSeekWorker = null;
        _normalSeekSimulation = null;
        _normalSeekCurrent = null;
        _normalSeekLatest = null;
    }

    private static bool SameNormalScope(NormalFrameSeek left, NormalFrameSeek right)
        => left.ConstantHistory == right.ConstantHistory && left.GroundHeight == right.GroundHeight
            && left.EmissionEnd == right.EmissionEnd && left.CurrentAttachment == right.CurrentAttachment
            && left.HistoryRootAtZero == right.HistoryRootAtZero
            && (left.History == null) == (right.History == null)
            && (left.Time != right.Time || left.Attachment == right.Attachment);

    private static bool SameNormalRequest(NormalFrameSeek left, NormalFrameSeek right)
        => left.Time == right.Time && left.Attachment == right.Attachment && SameNormalScope(left, right);

    private sealed record NormalFrameSeek(float Time, Matrix Attachment, Func<float, Matrix>? History,
        Matrix? HistoryRootAtZero, bool CurrentAttachment, bool ConstantHistory, float? GroundHeight,
        float? EmissionEnd, bool InitializeDuration);
}
