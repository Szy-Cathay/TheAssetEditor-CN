using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Vfx;

// Local preview scope; null keeps the authored composition and empty lists select no roots.
public sealed record VfxPreviewRootSelection(IReadOnlyList<string> EmitterIds, IReadOnlyList<string> ReferenceNames);
public sealed record VfxPreviewSnapshot(byte[] GameBytes, string SourcePath, Func<string, byte[]?> Resolve, string Quality = "0",
    VfxPreviewRootSelection? RootSelection = null)
{
    // Only the owner locates resources; the returned reader must contain no live Pack/editor access.
    public Func<string, Func<byte[]?>?>? CaptureResourceRead { get; init; }
}
public sealed record VfxPreviewDiagnostic(string Source, string Layer, string Field, string ReasonKey);
public sealed record VfxInteractiveReloadFrame(float Time, Matrix Attachment, Func<float, Matrix>? History = null,
    bool InitializeDuration = false, bool UseCurrentAttachmentForFrame = false, bool ContinuousTrail = false,
    Func<float, float?>? NextAttachmentSample = null, float? EmissionEnd = null, float? GroundHeight = null)
{
    public VfxInteractiveReloadFrame(float time, Matrix attachment, Func<float, Matrix>? history,
        bool initializeDuration, bool useCurrentAttachmentForFrame, bool continuousTrail,
        Func<float, float?>? nextAttachmentSample, float? emissionEnd)
        : this(time, attachment, history, initializeDuration, useCurrentAttachmentForFrame, continuousTrail,
            nextAttachmentSample, emissionEnd, null) { }
}
public sealed record VfxPreviewInspection(int EmitterCount, int TotalEmitterCount, int DisabledEmitterCount, IReadOnlyList<VfxPreviewDiagnostic> Diagnostics)
{
    public VfxPreviewEditorInfo EditorInfo { get; init; } = VfxPreviewEditorInfo.Empty;
}

public static class VfxPreviewInspector
{
    public static VfxPreviewInspection Inspect(VfxPreviewSnapshot snapshot)
    {
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        return new(conversion.Emitters.Count(e => !e.IsEventTemplate), conversion.TotalEmitterCount, conversion.DisabledEmitterCount, conversion.Diagnostics)
        { EditorInfo = conversion.EditorInfo };
    }
}

// The caller owns timing and the current bone/root world transform; preview never saves.
public interface IVfxPreview : IDisposable
{
    IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; }
    VfxPreviewEditorInfo EditorInfo => VfxPreviewEditorInfo.Empty;
    int EmitterCount { get; }
    int TotalEmitterCount { get; }
    int DisabledEmitterCount => 0;
    int ParticleCount { get; }
    float CycleDuration => 8;
    bool HasInfiniteEmitters => false;
    bool HasMotionTrailEmitters => false;
    // Only interactive owners opt in; synchronous/offscreen callers retain the existing contract.
    void EnableInteractivePrewarm() { }
    // Only a caller owning the complete history can certify an unchanged external attachment.
    void ConfigureInteractiveSeekHistory(bool isConstant) { }
    bool IsPreparing => false;
    bool IsSeekingFrame => false;
    // Owners explicitly provide ground and certify history before private exact frame preparation.
    void ConfigureInteractiveSeekFrame(bool isHistoryConstant, float? groundHeight, bool initializeDuration = false, bool allowForwardReplay = true) { }
    void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? attachmentAtTime) { }
    // Instance-local root emission boundary; particles and their event tails keep their authored lifetime.
    float? EmissionEndTimeSeconds { get => null; set { } }
    bool IsDrained => false;
    BoundingBox? PreviewBounds => null;
    IVfxPreview? CreatePlaybackInstance() => null;
    void Reload(VfxPreviewSnapshot snapshot);
    // Interactive hosts pump owner-thread reads and exact replay; synchronous callers retain Reload.
    bool BeginInteractiveReload(VfxPreviewSnapshot snapshot) { Reload(snapshot); return true; }
    bool ContinueInteractiveReload(VfxInteractiveReloadFrame frame) => true;
    bool BeginInteractiveRestart() => true;
    bool HasPendingInteractiveReload => false;
    // Identification only filters subsequent draws; null restores the loaded preview scope.
    void Identify(VfxPreviewLayerIdentity? layer) { }
    void Advance(float seconds, Matrix attachment);
    void Seek(float seconds, Matrix attachment);
    void Seek(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime) => Seek(seconds, attachment);
    void Seek(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime, bool useCurrentAttachmentForFrame)
        => Seek(seconds, attachment, attachmentAtTime);
    // Editor-only motion preview; authored playback remains on the existing Seek entries.
    void SeekContinuousTrail(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime = null)
    {
        if (attachmentAtTime != null) Seek(seconds, attachment, attachmentAtTime, useCurrentAttachmentForFrame: true);
        else Seek(seconds, attachment);
    }
    void SeekContinuousTrail(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime,
        Func<float, float?>? nextAttachmentSample) => SeekContinuousTrail(seconds, attachment, attachmentAtTime);
    void Draw(CommonShaderParameters parameters);
    void Draw(CommonShaderParameters parameters, IDisposable? scene) => Draw(parameters);
    IDisposable? BeginScene(CommonShaderParameters parameters) => null;
}

public interface IVfxPreviewFactory
{
    IVfxPreview Create(GraphicsDevice device);
}

public sealed class EffekseerVfxPreviewFactory : IVfxPreviewFactory
{
    public IVfxPreview Create(GraphicsDevice device) => new EffekseerVfxPreview(device);
}
