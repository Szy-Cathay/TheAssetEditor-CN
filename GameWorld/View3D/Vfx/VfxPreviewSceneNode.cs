using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Vfx;

// Transient scene item: the host supplies timeline seconds and the standard parent world transform.
public sealed class VfxPreviewSceneNode : GroupNode, IDrawableItem, IRenderItem, IDisposable
{
    private readonly IVfxPreviewFactory _factory;
    private readonly IDeviceResolver _devices;
    private readonly VfxPreviewDocumentSource _source;
    private IVfxPreview? _preview;
    private volatile bool _reload = true;
    private bool _disposed;
    public float TimeSeconds { get; set; }
    public Func<float, Matrix>? LocalAttachmentAtTime { get; set; }
    public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; private set; } = [];
    public event Action? DiagnosticsChanged;
    public bool DependsOn(object file) => _source.DependsOn(file);
    public bool IncludeInPhotoCapture => false;
    public int ParticleCount => _preview?.ParticleCount ?? 0;
    public float? Duration => _preview?.CycleDuration;
    public VfxPreviewSceneNode(IVfxPreviewFactory factory, IDeviceResolver devices, VfxPreviewDocumentSource source) : base("VFX")
    {
        _factory = factory; _devices = devices; _source = source;
        IsSelectable = false; IsEditable = false;
        source.Changed += OnChanged;
    }
    private void OnChanged() => _reload = true;
    public void Render(RenderEngineComponent renderEngine, Matrix parentWorld)
    {
        if (PrepareFrame(parentWorld)) renderEngine.AddRenderItem(RenderBuckedId.Normal, this);
    }
    public bool PrepareFrame(Matrix parentWorld)
    {
        if (_disposed || !IsVisible) return false;
        try
        {
            if (_reload)
            {
                _reload = false;
                var snapshot = _source.Read();
                if (snapshot == null)
                {
                    _preview?.Dispose(); _preview = null;
                    SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.LiveDocumentInvalid")]);
                    return false;
                }
                _preview ??= _factory.Create(_devices.Device);
                _preview.Reload(snapshot);
                SetDiagnostics(_preview.Diagnostics);
            }
            var time = Math.Max(0, TimeSeconds);
            var canonicalParent = SceneManager != null && Parent != null ? SceneManager.GetWorldPosition(Parent) : parentWorld;
            var attachment = ModelMatrix * canonicalParent;
            if (LocalAttachmentAtTime is { } sample)
            {
                var inverseCurrentLocal = Matrix.Invert(sample(time));
                _preview?.Seek(time, attachment, past => sample(past) * inverseCurrentLocal * attachment);
            }
            else _preview?.Seek(time, attachment);
            return _preview?.EmitterCount > 0;
        }
        catch (Exception exception)
        {
            Logging.Create<VfxPreviewSceneNode>().Error(exception, "VFX metadata preview failed for {Path}", _source.Path);
            _preview?.Dispose(); _preview = null;
            SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.RuntimeUnavailable")]);
            return false;
        }
    }
    void IRenderItem.Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
    {
        if (_disposed || renderingTechnique is RenderingTechnique.Emissive or RenderingTechnique.ViewportGeometry) return;
        try
        {
            _preview?.Draw(parameters);
            if (_preview != null) SetDiagnostics(_preview.Diagnostics);
        }
        catch (Exception exception)
        {
            Logging.Create<VfxPreviewSceneNode>().Error(exception, "VFX metadata drawing failed for {Path}", _source.Path);
            _preview?.Dispose(); _preview = null;
            SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.RuntimeUnavailable")]);
        }
    }
    private void SetDiagnostics(IReadOnlyList<VfxPreviewDiagnostic> diagnostics)
    {
        if (ReferenceEquals(Diagnostics, diagnostics)) return;
        Diagnostics = diagnostics;
        DiagnosticsChanged?.Invoke();
    }
    public override ISceneNode CreateCopyInstance() => throw new NotSupportedException("Transient VFX previews cannot be cloned.");
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.Changed -= OnChanged;
        _source.Dispose();
        _preview?.Dispose(); _preview = null;
        DiagnosticsChanged = null;
        LocalAttachmentAtTime = null;
    }
}
