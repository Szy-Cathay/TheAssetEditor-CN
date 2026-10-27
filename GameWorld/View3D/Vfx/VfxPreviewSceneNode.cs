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
    private readonly List<PlaybackTail> _tails = [];
    private volatile bool _reload = true;
    private bool _discardTailsOnReload;
    private bool _currentPrepared;
    private bool _disposed;
    public float TimeSeconds { get; set; }
    public float? EmissionEndTimeSeconds { get; set; }
    public float? AttachmentSampleTimeSeconds { get; set; }
    public bool CurrentPlaybackEnabled { get; set; } = true;
    public float? FlatGroundHeight { get; set; }
    public Func<float, Matrix>? LocalAttachmentAtTime { get; set; }
    public Func<float, float>? AttachmentHistoryTime { get; set; }
    public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; private set; } = [];
    public event Action? DiagnosticsChanged;
    public bool DependsOn(object file) => _source.DependsOn(file);
    public bool IncludeInPhotoCapture => false;
    public int ParticleCount => (_preview?.ParticleCount ?? 0) + _tails.Sum(tail => tail.Preview.ParticleCount);
    public float? Duration => _preview?.CycleDuration;
    public bool HasInfiniteEmitters => _preview?.HasInfiniteEmitters == true;
    public bool HasPlaybackContent => _tails.Count != 0 || CurrentPlaybackEnabled
        && (_reload || _preview != null && (!_currentPrepared || !_preview.IsDrained));
    public VfxPreviewSceneNode(IVfxPreviewFactory factory, IDeviceResolver devices, VfxPreviewDocumentSource source) : base("VFX")
    {
        _factory = factory; _devices = devices; _source = source;
        IsSelectable = false; IsEditable = false;
        source.Changed += OnChanged;
    }
    private void OnChanged() { _reload = true; _discardTailsOnReload = true; }
    public void ResetPlayback()
    {
        var next = _preview?.CreatePlaybackInstance();
        if (next == null)
            foreach (var tail in _tails)
                if ((next = tail.Preview.CreatePlaybackInstance()) != null) break;
        DisposeTails();
        _preview?.Dispose(); _preview = next;
        _currentPrepared = false;
        _reload = _reload || next == null;
    }
    public void RetireCurrentPlayback(float emissionEndTimeSeconds)
    {
        if (_preview != null)
        {
            var next = _preview.CreatePlaybackInstance();
            _preview.EmissionEndTimeSeconds = EmissionEndTimeSeconds is { } end
                ? Math.Min(end, emissionEndTimeSeconds) : emissionEndTimeSeconds;
            _tails.Add(new(_preview, TimeSeconds));
            _preview = next;
        }
        _reload = _reload || _preview == null;
        _currentPrepared = false;
    }
    public void AdvancePlaybackTails(float seconds)
    {
        foreach (var tail in _tails) tail.TimeSeconds += seconds;
    }
    public void Render(RenderEngineComponent renderEngine, Matrix parentWorld)
    {
        if (PrepareFrame(parentWorld)) renderEngine.AddRenderItem(RenderBuckedId.Normal, this);
    }
    public bool PrepareFrame(Matrix parentWorld)
    {
        if (_disposed || !IsVisible) return false;
        try
        {
            if (_reload && (CurrentPlaybackEnabled || _discardTailsOnReload))
            {
                _reload = false;
                _currentPrepared = false;
                if (_discardTailsOnReload) { DisposeTails(); _discardTailsOnReload = false; }
                var snapshot = _source.Read();
                if (snapshot == null)
                {
                    _preview?.Dispose(); _preview = null;
                    SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.LiveDocumentInvalid")]);
                    return false;
                }
                if (CurrentPlaybackEnabled)
                {
                    _preview ??= _factory.Create(_devices.Device);
                    _preview.Reload(snapshot);
                    SetDiagnostics(_preview.Diagnostics);
                }
                else _reload = true;
            }
            var time = Math.Max(0, TimeSeconds);
            var canonicalParent = SceneManager != null && Parent != null ? SceneManager.GetWorldPosition(Parent) : parentWorld;
            var attachment = ModelMatrix * canonicalParent;
            Func<float, Matrix>? history = null;
            if (LocalAttachmentAtTime is { } sample)
            {
                var inverseCurrentLocal = Matrix.Invert(sample(AttachmentSampleTimeSeconds ?? time));
                history = past => sample(AttachmentHistoryTime?.Invoke(past) ?? past) * inverseCurrentLocal * attachment;
            }
            foreach (var tail in _tails)
                Seek(tail.Preview, tail.TimeSeconds, attachment, history);
            for (var index = _tails.Count - 1; index >= 0; --index)
                if (_tails[index].Preview.IsDrained)
                {
                    _tails[index].Preview.Dispose();
                    _tails.RemoveAt(index);
                }
            if (CurrentPlaybackEnabled && _preview != null)
            {
                _preview.EmissionEndTimeSeconds = EmissionEndTimeSeconds;
                Seek(_preview, time, attachment, history);
                _currentPrepared = true;
            }
            return _tails.Any(tail => tail.Preview.EmitterCount > 0) || CurrentPlaybackEnabled && _preview is { EmitterCount: > 0, IsDrained: false };
        }
        catch (Exception exception)
        {
            Logging.Create<VfxPreviewSceneNode>().Error(exception, "VFX metadata preview failed for {Path}", _source.Path);
            _preview?.Dispose(); _preview = null;
            DisposeTails();
            SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.RuntimeUnavailable")]);
            return false;
        }
    }
    void IRenderItem.Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        => DrawInScene(device, parameters, renderingTechnique, null);
    internal void DrawInScene(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique, IDisposable? scene)
    {
        if (_disposed || renderingTechnique is RenderingTechnique.Emissive or RenderingTechnique.ViewportGeometry) return;
        try
        {
            var frameParameters = parameters with
            {
                VfxGroundHeight = parameters.VfxGroundHeight ?? FlatGroundHeight,
                VfxWriteGroundDepth = parameters.VfxWriteGroundDepth || FlatGroundHeight.HasValue,
            };
            foreach (var tail in _tails) tail.Preview.Draw(frameParameters, scene);
            if (CurrentPlaybackEnabled) _preview?.Draw(frameParameters, scene);
            if (_preview != null) SetDiagnostics(_preview.Diagnostics);
        }
        catch (Exception exception)
        {
            DrawingFailed(exception);
        }
    }
    internal IDisposable? BeginScene(CommonShaderParameters parameters)
    {
        try
        {
            if (_disposed) return null;
            foreach (var tail in _tails)
                if (tail.Preview.BeginScene(parameters) is { } scene) return scene;
            return CurrentPlaybackEnabled ? _preview?.BeginScene(parameters) : null;
        }
        catch (Exception exception) { DrawingFailed(exception); return null; }
    }
    internal void EndScene(IDisposable scene)
    {
        try { scene.Dispose(); }
        catch (Exception exception) { DrawingFailed(exception); }
    }
    private void DrawingFailed(Exception exception)
    {
        Logging.Create<VfxPreviewSceneNode>().Error(exception, "VFX metadata drawing failed for {Path}", _source.Path);
        _preview?.Dispose(); _preview = null;
        DisposeTails();
        SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.RuntimeUnavailable")]);
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
        DisposeTails();
        DiagnosticsChanged = null;
        LocalAttachmentAtTime = null;
        AttachmentHistoryTime = null;
    }
    private static void Seek(IVfxPreview preview, float time, Matrix attachment, Func<float, Matrix>? history)
    {
        if (history != null) preview.Seek(Math.Max(0, time), attachment, history);
        else preview.Seek(Math.Max(0, time), attachment);
    }
    private void DisposeTails()
    {
        foreach (var tail in _tails) tail.Preview.Dispose();
        _tails.Clear();
    }
    private sealed class PlaybackTail(IVfxPreview preview, float timeSeconds)
    {
        public IVfxPreview Preview { get; } = preview;
        public float TimeSeconds { get; set; } = timeSeconds;
    }
}
