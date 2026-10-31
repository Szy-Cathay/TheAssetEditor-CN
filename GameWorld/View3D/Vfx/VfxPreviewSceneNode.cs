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
    private bool _pendingContentReload;
    private bool _disposed;
    private VfxInteractiveFrameBudget? _preparationBudget;
    public bool EnableInteractiveReload { get; set; }
    public bool IsPlaybackAdvancing { get; set; }
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
        var pendingContent = _pendingContentReload;
        _pendingContentReload = false;
        var next = _preview?.CreatePlaybackInstance();
        if (next == null)
            foreach (var tail in _tails)
                if ((next = tail.Preview.CreatePlaybackInstance()) != null) break;
        DisposeTails();
        DisposePreview(_preview); _preview = next;
        _currentPrepared = false;
        _reload = _reload || next == null || pendingContent;
        if (!_reload && EnableInteractiveReload) _preview?.BeginInteractiveRestart();
    }
    public void RetireCurrentPlayback(float emissionEndTimeSeconds)
    {
        var pendingContent = _pendingContentReload;
        _pendingContentReload = false;
        if (_preview != null)
        {
            var next = _preview.CreatePlaybackInstance();
            _preview.EmissionEndTimeSeconds = EmissionEndTimeSeconds is { } end
                ? Math.Min(end, emissionEndTimeSeconds) : emissionEndTimeSeconds;
            _tails.Add(new(_preview, TimeSeconds, _currentPrepared));
            _preview = next;
        }
        _reload = _reload || _preview == null || pendingContent;
        _currentPrepared = false;
        if (!_reload && EnableInteractiveReload) _preview?.BeginInteractiveRestart();
    }
    public void RefreshPlayback()
    {
        if (EnableInteractiveReload && !_pendingContentReload
            && _preview?.BeginInteractiveRestart() == false)
        {
            DisposeTails();
            return;
        }
        ResetPlayback();
    }
    public void AdvancePlaybackTails(float seconds)
    {
        foreach (var tail in _tails) tail.TimeSeconds += seconds;
    }
    public void Render(RenderEngineComponent renderEngine, Matrix parentWorld)
    {
        if (PrepareFrame(parentWorld, EnableInteractiveReload ? renderEngine.VfxPreparationBudget : null))
            renderEngine.AddRenderItem(RenderBuckedId.Normal, this);
    }
    public bool PrepareFrame(Matrix parentWorld) => PrepareFrame(parentWorld, null);
    internal bool PrepareFrame(Matrix parentWorld, VfxInteractiveFrameBudget? budget)
    {
        if (_disposed || !IsVisible) return false;
        if (budget != null) _preparationBudget = budget;
        try
        {
            if (_reload && (CurrentPlaybackEnabled || _discardTailsOnReload))
            {
                _reload = false;
                if (!EnableInteractiveReload) _currentPrepared = false;
                if (_discardTailsOnReload) { DisposeTails(); _discardTailsOnReload = false; }
                var snapshot = _source.Read();
                if (snapshot == null)
                {
                    DisposePreview(_preview); _preview = null;
                    SetDiagnostics([new(_source.Path, "", "", "Vfx.Preview.LiveDocumentInvalid")]);
                    return false;
                }
                if (CurrentPlaybackEnabled)
                {
                    _preview ??= _factory.Create(_devices.Device);
                    if (EnableInteractiveReload) _preview.BeginInteractiveReload(snapshot);
                    else _preview.Reload(snapshot);
                    _pendingContentReload = _preview.HasPendingInteractiveReload;
                    if (!_preview.HasPendingInteractiveReload) SetDiagnostics(_preview.Diagnostics);
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
            {
                if (!tail.Preview.HasPendingInteractiveReload || ContinueReload(tail.Preview,
                    new(tail.TimeSeconds, attachment, history, EmissionEnd: tail.Preview.EmissionEndTimeSeconds,
                        GroundHeight: FlatGroundHeight), budget))
                {
                    Seek(tail.Preview, tail.TimeSeconds, attachment, history);
                    tail.Prepared = true;
                }
                else if (tail.Prepared && !tail.Preview.IsPreparing)
                    Seek(tail.Preview, tail.TimeSeconds, attachment, history);
            }
            for (var index = _tails.Count - 1; index >= 0; --index)
                if (_tails[index].Prepared && !_tails[index].Preview.HasPendingInteractiveReload && _tails[index].Preview.IsDrained)
                {
                    DisposePreview(_tails[index].Preview);
                    _tails.RemoveAt(index);
                }
            if (CurrentPlaybackEnabled && _preview != null)
            {
                if (!_preview.HasPendingInteractiveReload || ContinueReload(_preview, new(time, attachment, history,
                    EmissionEnd: EmissionEndTimeSeconds, GroundHeight: FlatGroundHeight), budget))
                {
                    _preview.EmissionEndTimeSeconds = EmissionEndTimeSeconds;
                    Seek(_preview, time, attachment, history);
                    _currentPrepared = true;
                    _pendingContentReload = false;
                    SetDiagnostics(_preview.Diagnostics);
                }
                else if (_currentPrepared && !_preview.IsPreparing)
                {
                    _preview.EmissionEndTimeSeconds = EmissionEndTimeSeconds;
                    Seek(_preview, time, attachment, history);
                }
            }
            return _tails.Any(tail => tail.Prepared && !tail.Preview.IsPreparing && tail.Preview.EmitterCount > 0)
                || CurrentPlaybackEnabled && _currentPrepared && _preview is { IsPreparing: false, EmitterCount: > 0, IsDrained: false };
        }
        catch (Exception exception)
        {
            Logging.Create<VfxPreviewSceneNode>().Error(exception, "VFX metadata preview failed for {Path}", _source.Path);
            DisposePreview(_preview); _preview = null;
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
            foreach (var tail in _tails)
                if (tail.Prepared && !tail.Preview.IsPreparing) tail.Preview.Draw(frameParameters, scene);
            if (CurrentPlaybackEnabled && _currentPrepared && _preview?.IsPreparing != true) _preview?.Draw(frameParameters, scene);
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
                if (tail.Prepared && !tail.Preview.IsPreparing && tail.Preview.BeginScene(parameters) is { } scene) return scene;
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
        DisposePreview(_preview); _preview = null;
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
        DisposePreview(_preview); _preview = null;
        DisposeTails();
        DiagnosticsChanged = null;
        LocalAttachmentAtTime = null;
        AttachmentHistoryTime = null;
    }
    private void Seek(IVfxPreview preview, float time, Matrix attachment, Func<float, Matrix>? history)
    {
        if (EnableInteractiveReload) preview.ConfigureInteractiveSeekFrame(history == null, FlatGroundHeight,
            allowForwardReplay: !IsPlaybackAdvancing);
        var preparation = preview.IsSeekingFrame ? _preparationBudget : null;
        if (preparation != null && !preparation.TryBegin(preview)) return;
        try
        {
            if (history != null) preview.Seek(Math.Max(0, time), attachment, history);
            else preview.Seek(Math.Max(0, time), attachment);
        }
        finally { preparation?.End(preview, preview.IsSeekingFrame); }
    }
    private static bool ContinueReload(IVfxPreview preview, VfxInteractiveReloadFrame frame, VfxInteractiveFrameBudget? budget)
    {
        if (budget == null) return preview.ContinueInteractiveReload(frame);
        if (!budget.TryBegin(preview)) return false;
        try { return preview.ContinueInteractiveReload(frame); }
        finally { budget.End(preview, preview.HasPendingInteractiveReload); }
    }
    private void DisposePreview(IVfxPreview? preview)
    {
        if (preview == null) return;
        _preparationBudget?.Cancel(preview);
        preview.Dispose();
    }
    private void DisposeTails()
    {
        foreach (var tail in _tails) DisposePreview(tail.Preview);
        _tails.Clear();
    }
    private sealed class PlaybackTail(IVfxPreview preview, float timeSeconds, bool prepared)
    {
        public IVfxPreview Preview { get; } = preview;
        public float TimeSeconds { get; set; } = timeSeconds;
        public bool Prepared { get; set; } = prepared;
    }
}
