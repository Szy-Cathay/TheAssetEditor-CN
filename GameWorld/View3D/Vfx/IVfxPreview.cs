using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Vfx;

// Local preview scope; null keeps the authored composition and empty lists select no roots.
public sealed record VfxPreviewRootSelection(IReadOnlyList<string> EmitterIds, IReadOnlyList<string> ReferenceNames);
public sealed record VfxPreviewSnapshot(byte[] GameBytes, string SourcePath, Func<string, byte[]?> Resolve, string Quality = "0",
    VfxPreviewRootSelection? RootSelection = null);
public sealed record VfxPreviewDiagnostic(string Source, string Layer, string Field, string ReasonKey);
public sealed record VfxPreviewInspection(int EmitterCount, int TotalEmitterCount, int DisabledEmitterCount, IReadOnlyList<VfxPreviewDiagnostic> Diagnostics);

public static class VfxPreviewInspector
{
    public static VfxPreviewInspection Inspect(VfxPreviewSnapshot snapshot)
    {
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        return new(conversion.Emitters.Count(e => !e.IsEventTemplate), conversion.TotalEmitterCount, conversion.DisabledEmitterCount, conversion.Diagnostics);
    }
}

// The caller owns timing and the current bone/root world transform; preview never saves.
public interface IVfxPreview : IDisposable
{
    IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; }
    int EmitterCount { get; }
    int TotalEmitterCount { get; }
    int DisabledEmitterCount => 0;
    int ParticleCount { get; }
    float CycleDuration => 8;
    bool HasMotionTrailEmitters => false;
    BoundingBox? PreviewBounds => null;
    void Reload(VfxPreviewSnapshot snapshot);
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
    void Draw(CommonShaderParameters parameters);
}

public interface IVfxPreviewFactory
{
    IVfxPreview Create(GraphicsDevice device);
}

public sealed class EffekseerVfxPreviewFactory : IVfxPreviewFactory
{
    public IVfxPreview Create(GraphicsDevice device) => new EffekseerVfxPreview(device);
}
