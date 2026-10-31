using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private InteractiveSeekOperation? _interactivePreparation;
    internal float? CurrentGroundHeight => _groundHeight;

    internal bool NeedsGroundReplay(float? height) => !Nullable.Equals(_groundHeight, height)
        && conversion.Emitters.Any(emitter => emitter.ModelTerrainBirth || emitter.RequiresFlatTerrain && emitter.Events.Count > 0);

    internal void PrepareInitialGround(float? height, Matrix attachment, Func<float, Matrix>? history,
        bool useCurrentAttachmentForFrame, bool continueMotionTrails, Func<float, float?>? nextAttachmentSample)
    {
        if (!NeedsGroundReplay(height)) return;
        if (_initialized || _interactivePreparation != null)
            throw new InvalidOperationException("Ground preparation requires an unpublished, fresh simulation.");
        _groundHeight = height;
        _attachment = attachment;
        _attachmentAtTime = history;
        _useCurrentAttachmentForFrame = useCurrentAttachmentForFrame;
        _continueMotionTrails = continueMotionTrails;
        _nextAttachmentSample = nextAttachmentSample;
        // Match the old first Draw's final Reset, after the real attachment/history was installed.
        Reset();
    }

    // Owner-thread preparation of an unpublished simulation. Never draw or share it until this returns true.
    internal bool PrepareInteractiveSeek(float seconds, Matrix attachment, Func<float, Matrix>? history = null,
        bool useCurrentAttachmentForFrame = false, bool continueMotionTrails = false,
        Func<float, float?>? nextAttachmentSample = null, TimeSpan? budget = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || !continueMotionTrails && seconds > 600)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var allowance = budget ?? TimeSpan.FromMilliseconds(2);
        if (allowance < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        var started = Stopwatch.GetTimestamp();
        var request = new InteractiveSeekRequest(seconds, attachment, history, useCurrentAttachmentForFrame,
            continueMotionTrails, nextAttachmentSample);
        while (true)
        {
            var operation = _interactivePreparation ??= BeginInteractiveSeek(request);
            if (!ContinueInteractiveSeek(operation, started, allowance)) return false;
            _interactivePreparation = null;
            if (operation.Request == request) return true;
            // Finish the original input chord before accepting a changed target or attachment.
            if (Stopwatch.GetElapsedTime(started) >= allowance) return false;
        }
    }

    private InteractiveSeekOperation BeginInteractiveSeek(InteractiveSeekRequest request)
    {
        var sameTime = _initialized && request.Seconds == _requestedTime;
        _attachment = request.Attachment;
        _attachmentAtTime = request.History;
        _nextAttachmentSample = request.NextAttachmentSample;
        _useCurrentAttachmentForFrame = request.UseCurrentAttachmentForFrame;
        _continueMotionTrails = request.ContinueMotionTrails;
        CheckSeekCheckpointContext(request.Attachment, request.ContinueMotionTrails, request.NextAttachmentSample != null);
        if (!_initialized || request.Seconds + 0.00001 < _requestedTime)
        {
            if (!_initialized || !TryRestoreSeekCheckpoint(request.Seconds)) Reset();
        }
        var previousTime = _requestedTime;
        _requestedTime = request.Seconds;
        return new(request, sameTime, previousTime);
    }

    private bool ContinueInteractiveSeek(InteractiveSeekOperation operation, long started, TimeSpan allowance)
    {
        var request = operation.Request;
        if (request.ContinueMotionTrails)
        {
            while (operation.PreviousTime < request.Seconds)
            {
                if (operation.ChordEnd == null)
                {
                    var end = (double)request.Seconds;
                    if (request.NextAttachmentSample?.Invoke((float)operation.PreviousTime) is { } next
                        && next > operation.PreviousTime && next < end) end = next;
                    operation.ChordEnd = end;
                }
                if (!StepInteractiveTo(operation.ChordEnd.Value, started, allowance)) return false;
                // Native trails emit once per original recorded chord, never once per preparation slice.
                EmitFrameTrails(operation.PreviousTime, operation.ChordEnd.Value);
                operation.PreviousTime = operation.ChordEnd.Value;
                operation.ChordEnd = null;
                if (operation.PreviousTime < request.Seconds && Stopwatch.GetElapsedTime(started) >= allowance) return false;
            }
        }
        else if (!StepInteractiveTo(request.Seconds, started, allowance)) return false;
        ClearExplicitlyStoppedModels(request.Seconds);
        if (operation.SameTime && request.UseCurrentAttachmentForFrame)
            foreach (var emitter in _emitters) UpdateMotionRoot(emitter, request.Seconds, currentFrame: true, preserveVelocity: true);
        return true;
    }

    private bool StepInteractiveTo(double time, long started, TimeSpan allowance)
    {
        while (_simulatedTime + Step <= time + 0.000001)
        {
            _simulatedTime += Step;
            Tick(Step);
            CaptureSeekCheckpoint();
            if (Stopwatch.GetElapsedTime(started) >= allowance) return false;
        }
        return true;
    }

    private readonly record struct InteractiveSeekRequest(float Seconds, Matrix Attachment, Func<float, Matrix>? History,
        bool UseCurrentAttachmentForFrame, bool ContinueMotionTrails, Func<float, float?>? NextAttachmentSample);

    private sealed class InteractiveSeekOperation(InteractiveSeekRequest request, bool sameTime, double previousTime)
    {
        public InteractiveSeekRequest Request { get; } = request;
        public bool SameTime { get; } = sameTime;
        public double PreviousTime { get; set; } = previousTime;
        public double? ChordEnd { get; set; }
    }
}
