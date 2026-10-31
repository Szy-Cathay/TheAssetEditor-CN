using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed record VfxSimulationReplayOrigin(Matrix Attachment, Func<float, Matrix>? History, float? GroundHeight,
    bool CurrentAttachment, bool ContinuousTrail, Func<float, float?>? NextAttachmentSample, float? EmissionEnd);

internal sealed partial class TotalWarVfxSimulation
{
    internal VfxSimulationReplayOrigin? ReplayOrigin { get; private set; }
    internal Matrix ReplayAttachment => _attachment;

    // Record existing inputs only; capturing must never sample a live history or add a source chord.
    private void CaptureReplayOrigin() => ReplayOrigin = new(_attachment, _attachmentAtTime, _groundHeight,
        _useCurrentAttachmentForFrame, _continueMotionTrails, _nextAttachmentSample, _emissionEndTimeSeconds);

    internal void InitializeReplayOrigin(VfxSimulationReplayOrigin origin)
    {
        if (_initialized || _interactivePreparation != null)
            throw new InvalidOperationException("Replay origin initialization requires a fresh private simulation.");
        if (origin.EmissionEnd != _emissionEndTimeSeconds)
            throw new InvalidOperationException("A replay origin cannot replace emission cutoff history.");
        _attachment = origin.Attachment;
        _attachmentAtTime = origin.History;
        _groundHeight = origin.GroundHeight;
        _useCurrentAttachmentForFrame = origin.CurrentAttachment;
        _continueMotionTrails = origin.ContinuousTrail;
        _nextAttachmentSample = origin.NextAttachmentSample;
        Reset();
    }
}
