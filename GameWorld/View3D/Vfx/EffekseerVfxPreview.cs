using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Vfx;

public sealed partial class EffekseerVfxPreview(GraphicsDevice device) : IVfxPreview
{
    private EffekseerHandle? _handle;
    private PlaybackResources? _resources;
    private VfxConversion? _conversion;
    private TotalWarVfxSimulation? _simulation;
    private VfxPreviewLayerIdentity? _identification;
    private bool[]? _identifiedEmitters;
    private bool _interactivePrewarmEnabled;
    private bool _interactiveSeekHistoryConstant;
    private VfxInteractivePrewarm? _interactivePrewarm;
    private VfxInteractiveBounds? _interactiveBounds;
    private bool _interactiveRestartPending;
    private float? _emissionEndTimeSeconds;
    private string _sourcePath = "";
    private RenderLayers[][] _renderLayers = [];
    private EffekseerParticle[] _submissionParticles = [];
    private readonly List<VfxPreviewParticle> _projectedParticles = [];
    private readonly List<EffekseerParticle> _renderedParticles = [];
    private readonly List<EffekseerParticle> _opaqueParticles = [];
    private readonly HashSet<int> _opaqueLayers = [];
    private VfxFlatGroundDepth? _flatGroundDepth;
    private bool _disposed;
    private VfxPreviewDiagnostic? _spriteSpinRangeDiagnostic;
    public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; private set; } = [];
    public VfxPreviewEditorInfo EditorInfo => _conversion?.EditorInfo ?? VfxPreviewEditorInfo.Empty;
    public int EmitterCount => _handle == null ? 0 : _conversion?.Emitters.Count(e => !e.IsEventTemplate) ?? 0;
    public int TotalEmitterCount => _conversion?.TotalEmitterCount ?? 0;
    public int ParticleCount => _simulation?.ParticleCount ?? 0;
    public int DisabledEmitterCount => _conversion?.DisabledEmitterCount ?? 0;
    public float CycleDuration => _simulation?.CycleDuration ?? 8;
    public bool HasInfiniteEmitters => (_preparedReload?.Conversion ?? _conversion)?.Emitters
        .Any(emitter => !emitter.IsEventTemplate && emitter.Infinite) == true;
    public bool HasMotionTrailEmitters => _simulation?.HasMotionTrailEmitters == true;
    public void EnableInteractivePrewarm() => _interactivePrewarmEnabled = true;
    public void ConfigureInteractiveSeekHistory(bool isConstant)
    {
        if (_interactiveSeekHistoryConstant != isConstant) CancelNormalFrameSeek();
        _interactiveSeekHistoryConstant = isConstant;
        if (!isConstant) _simulation?.DisableSeekCheckpoints();
        else if (_interactivePrewarmEnabled) _simulation?.EnableSeekCheckpoints();
    }
    public bool IsPreparing => _reloadUploaded && !_reloadRetainsPlayback && HasPendingInteractiveReload
        || _interactivePrewarm != null && (_simulation == null || _interactiveRestartPending);
    public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? attachmentAtTime)
    {
        PrepareNormalFrameSeek(attachment, attachmentAtTime);
        if (_interactiveBounds is { } bounds)
        {
            if (bounds.TryTake(out var preparedBounds))
            {
                PreviewBounds = preparedBounds;
                _resources?.PublishBounds(preparedBounds);
                bounds.Dispose();
                _interactiveBounds = null;
            }
            else if (bounds.Failure is { } failure)
            {
                Logging.Create<EffekseerVfxPreview>().Error(failure, "VFX bounds preparation failed for {Path}", _sourcePath);
                bounds.Dispose();
                _interactiveBounds = null;
            }
        }
        if (IsSeekingFrame || _interactivePrewarm == null) return;
        var key = PrewarmKey(attachment, attachmentAtTime);
        if (!_interactivePrewarm.Request(key))
        {
            _interactivePrewarm.Dispose(); _interactivePrewarm = null; _interactiveRestartPending = false;
            _simulation ??= CreateSimulation(_conversion!);
            return;
        }
        if (IsPreparing && _interactivePrewarm.TryTake(key, out var prepared))
        {
            if (_interactiveRestartPending && _interactiveFrameSeekEnabled)
            {
                _normalSeekInitializeDuration = false;
                _normalSeekInitializationKnown = true;
            }
            _simulation = prepared!.Simulation;
            if (!_interactiveSeekHistoryConstant) _simulation.DisableSeekCheckpoints();
            if (prepared.Bounds is { } preparedBounds)
            {
                PreviewBounds = preparedBounds;
                _resources?.PublishBounds(preparedBounds);
            }
            _interactiveRestartPending = false;
        }
    }

    private VfxInteractivePrewarmKey PrewarmKey(Matrix attachment, Func<float, Matrix>? history)
        => new(history?.Invoke(0) ?? attachment, attachment, attachment.Translation.Y, _emissionEndTimeSeconds);

    private bool PrepareInteractiveSeek(float seconds, Matrix attachment, Func<float, Matrix>? history)
    {
        if (_interactivePrewarm == null) return true;
        if (!_interactiveRestartPending && _simulation != null && seconds + .00001f >= _simulation.Time) return true;
        if (_interactiveSeekHistoryConstant && _simulation?.HasSeekCheckpoint(seconds) == true) return true;
        _interactiveRestartPending = true;
        PrepareInteractiveFrame(attachment, history);
        return !IsPreparing;
    }

    private TotalWarVfxSimulation CreateSimulation(VfxConversion conversion)
    {
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = _emissionEndTimeSeconds };
        if (_interactivePrewarmEnabled && _interactiveSeekHistoryConstant) simulation.EnableSeekCheckpoints();
        return simulation;
    }
    public float? EmissionEndTimeSeconds
    {
        get => _emissionEndTimeSeconds;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value is { } end && !float.IsFinite(end)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_interactivePrewarm != null && _emissionEndTimeSeconds != value) _interactiveRestartPending = true;
            if (_emissionEndTimeSeconds != value) CancelNormalFrameSeek();
            _emissionEndTimeSeconds = value;
            if (_simulation != null) _simulation.EmissionEndTimeSeconds = value;
        }
    }
    public bool IsDrained => !IsSeekingFrame && (_simulation?.IsDrained ?? true);
    private BoundingBox? _previewBounds;
    public BoundingBox? PreviewBounds { get => _previewBounds ?? _resources?.Bounds; private set => _previewBounds = value; }
    internal int DrawCallCount => _handle == null ? 0 : EffekseerNative.AecnVfxDrawCalls(_handle);

    public IVfxPreview? CreatePlaybackInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_resources is not { } resources || !resources.TryRetain()) return null;
        try
        {
            var playback = new EffekseerVfxPreview(device)
            {
                _resources = resources,
                _handle = resources.Handle,
                _conversion = resources.Conversion,
                _simulation = new(resources.Conversion),
                _renderLayers = resources.RenderLayers,
                _sourcePath = resources.SourcePath,
                Diagnostics = resources.Diagnostics,
                PreviewBounds = resources.Bounds,
            };
            playback._opaqueLayers.UnionWith(resources.OpaqueLayers);
            return playback;
        }
        catch { resources.Release(); throw; }
    }

    public void Reload(VfxPreviewSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelInteractiveReload();
        CancelPreparation();
        VfxConversion conversion;
        string? resourceIdentity;
        try
        {
            conversion = TotalWarVfxConverter.Convert(snapshot, collectEditorInfo: _interactivePrewarmEnabled);
            resourceIdentity = conversion.Emitters.Count > 0 && VfxPlaybackResourceIdentity.CanReuse(conversion)
                ? VfxPlaybackResourceIdentity.Create(conversion) : null;
        }
        catch { ReleaseResources(); throw; }
        ReloadConverted(snapshot, conversion, resourceIdentity);
    }

    private void CompleteReload(EffekseerHandle handle, byte?[] constantAlphas, string? resourceIdentity, bool reused = false)
    {
        UpdateIdentificationMask();
        var configuration = BuildRenderLayers(constantAlphas);
        if (reused && configuration.Layers.Length == 0)
            throw new InvalidOperationException("An empty configuration requires a fresh native playback handle.");
        if (configuration.Layers.Length > 0)
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, configuration.Layers, configuration.Layers.Length));
        if ((configuration.HalfLayers.Length > 0 || configuration.FullLayers.Length > 0)
            && System.Runtime.InteropServices.Marshal.SizeOf<EffekseerHalfLayer>() != EffekseerHalfLayer.Bytes)
            throw new InvalidOperationException("Unexpected raw-alpha layer configuration size.");
        if (configuration.HalfLayers.Length > 0)
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigureHalf(handle, configuration.HalfLayers,
                configuration.HalfLayers.Length, EffekseerHalfLayer.Bytes));
        if (configuration.FullLayers.Length > 0)
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigureFull(handle, configuration.FullLayers,
                configuration.FullLayers.Length, EffekseerHalfLayer.Bytes));
        if (_applyingInteractiveReload) { }
        else if (_interactivePrewarmEnabled && VfxInteractivePrewarm.CanPrepare(_conversion!))
        {
            _interactivePrewarm = new(_conversion!, enableSeekCheckpoints: _interactiveSeekHistoryConstant);
            _interactiveBounds = new(_conversion!);
            _simulation = null;
        }
        else if (_interactivePrewarmEnabled) _interactiveBounds = new(_conversion!);
        else PreviewBounds = VfxPreviewBounds.Estimate(_conversion!);
        _resources = new(handle, _conversion!, _renderLayers, _opaqueLayers.ToArray(), PreviewBounds, Diagnostics, _sourcePath,
            resourceIdentity, constantAlphas);
        _handle = handle;
    }

    internal (EffekseerLayer[] Layers, EffekseerHalfLayer[] HalfLayers, EffekseerHalfLayer[] FullLayers) BuildRenderLayers(byte?[] constantAlphas)
    {
        var layers = new List<EffekseerLayer>();
        var candidates = new List<HalfCandidate>();
        _opaqueLayers.Clear();
        _renderLayers = new RenderLayers[_conversion!.Emitters.Count][];
        for (var index = 0; index < _conversion.Emitters.Count; ++index)
        {
            var emitter = _conversion.Emitters[index];
            var emitterIndex = index;
            _renderLayers[index] = Enumerable.Range(0, emitter.ModelVariants.Count + 1).Select(variant =>
            {
                var coverage = new List<int>(); var emissive = new List<int>();
                foreach (var part in emitter.RenderParts(variant))
                {
                    var layer = part;
                    var regularDecal = layer.Kind == 2 && layer.MaterialMode < 3;
                    var split = emitter.PremultipliedAlpha && layer.Kind != 2 && layer.OpaqueModel == 0;
                    var plainTexture = split && layer.Kind != 2 && layer.MeshMaterialIndex < 0 && layer.GradientIndex < 0;
                    var alpha = layer.TextureIndex >= 0 && layer.TextureIndex < constantAlphas.Length ? constantAlphas[layer.TextureIndex] : null;
                    // Constant alpha survives filtering. MAX alpha is unchanged by either empty pass.
                    var drawEmission = split && emitter.AdditiveMultiplier > 0 && (!plainTexture || alpha != 255);
                    var drawCoverage = !plainTexture || alpha != 0 || !drawEmission;
                    coverage.Add(drawCoverage ? layers.Count : -1);
                    if (drawCoverage)
                    {
                        if (layer.OpaqueModel != 0) _opaqueLayers.Add(layers.Count);
                        if (regularDecal) { layer.MaterialMode = emitter.PremultipliedAlpha ? 1 : 0; layer.Blend = 1; }
                        if (split) { layer.MaterialMode = plainTexture && alpha == 255 && emitter.AdditiveMultiplier > 0 ? EffekseerLayer.PreciseCoverageMode : 1; layer.Blend = 1; }
                        layers.Add(layer);
                    }
                    if (drawEmission)
                    {
                        emissive.Add(layers.Count); layer.MaterialMode = !drawCoverage ? EffekseerLayer.PreciseEmissionMode : 2; layer.Blend = 2; layers.Add(layer);
                    }
                    else emissive.Add(-1);
                    if ((emitter.HalfResolution || emitter.FullResolution) && part.Kind == 0 && part.Distortion == 0 && part.ModelIndex < 0 && part.TextureIndex >= 0
                        && part.OpaqueModel == 0 && part.MeshMaterialIndex < 0 && part.GradientIndex < 0
                        && part.MaterialMode is 0 or 1 && (part.Blend == 1 || emitter.PremultipliedAlpha))
                        candidates.Add(new(emitterIndex, variant, coverage.Count - 1, part));
                }
                return new RenderLayers(coverage.ToArray(), emissive.ToArray()) { RawHalf = new bool[coverage.Count] };
            }).ToArray();
        }
        var halfLayers = new List<EffekseerHalfLayer>();
        var fullLayers = new List<EffekseerHalfLayer>();
        // Reserve the complete legacy configuration first, including its constant-alpha optimizations.
        foreach (var candidate in candidates)
        {
            if (layers.Count >= 512) break;
            var emitter = _conversion.Emitters[candidate.Emitter];
            var mapping = _renderLayers[candidate.Emitter][candidate.Variant];
            var rawIndex = layers.Count;
            var raw = candidate.Layer;
            raw.MaterialMode = emitter.PremultipliedAlpha ? 1 : 0;
            raw.Blend = 1;
            layers.Add(raw);
            var configuration = new EffekseerHalfLayer
            {
                LayerIndex = rawIndex, LegacyCoverageIndex = mapping.Coverage[candidate.Part],
                LegacyEmissionIndex = mapping.Emissive[candidate.Part], Premultiplied = emitter.PremultipliedAlpha ? 1 : 0,
                CoveredColourMultiplier = emitter.CoveredColourMultiplier, EmissionMultiplier = emitter.AdditiveMultiplier,
            };
            if (emitter.HalfResolution) halfLayers.Add(configuration); else fullLayers.Add(configuration);
            mapping.Coverage[candidate.Part] = rawIndex;
            mapping.Emissive[candidate.Part] = -1;
            mapping.RawHalf[candidate.Part] = true;
        }
        return (layers.ToArray(), halfLayers.ToArray(), fullLayers.ToArray());
    }

    private static (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) DecodeTexture(byte[] gameBytes, VfxTextureUsage usage)
        => VfxPreparedResources.DecodeTexture(gameBytes, usage);

    public void Advance(float seconds, Matrix attachment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(seconds) || seconds < 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        _simulation?.Advance(seconds, attachment);
        ReportCapacity();
    }

    public void Seek(float seconds, Matrix attachment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PrepareNormalFrameSeek(seconds, attachment, null, false)) return;
        if (!PrepareInteractiveSeek(seconds, attachment, null)) return;
        _simulation?.Seek(seconds, attachment);
        ReportCapacity();
    }
    public void Seek(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PrepareNormalFrameSeek(seconds, attachment, attachmentAtTime, false)) return;
        if (!PrepareInteractiveSeek(seconds, attachment, attachmentAtTime)) return;
        _simulation?.Seek(seconds, attachment, attachmentAtTime);
        ReportCapacity();
    }

    public void Seek(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime, bool useCurrentAttachmentForFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PrepareNormalFrameSeek(seconds, attachment, attachmentAtTime, useCurrentAttachmentForFrame)) return;
        if (!PrepareInteractiveSeek(seconds, attachment, attachmentAtTime)) return;
        if (useCurrentAttachmentForFrame) _simulation?.SeekWithCurrentAttachment(seconds, attachment, attachmentAtTime);
        else _simulation?.Seek(seconds, attachment, attachmentAtTime);
        ReportCapacity();
    }

    public void SeekContinuousTrail(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime = null)
        => SeekContinuousTrail(seconds, attachment, attachmentAtTime, null);

    public void SeekContinuousTrail(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime,
        Func<float, float?>? nextAttachmentSample)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelNormalFrameSeek();
        _simulation?.SeekContinuousTrail(seconds, attachment, attachmentAtTime, nextAttachmentSample);
        ReportCapacity();
    }

    public void Identify(VfxPreviewLayerIdentity? layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_identification == layer) return;
        _identification = layer;
        UpdateIdentificationMask();
    }

    private void UpdateIdentificationMask()
    {
        if (_identification is not { } selected || _conversion == null)
        {
            _identifiedEmitters = null;
            return;
        }
        var included = new bool[_conversion.Emitters.Count];
        for (var index = 0; index < included.Length; index++)
        {
            var emitter = _conversion.Emitters[index];
            included[index] = emitter.PreviewRootKind == selected.Kind && emitter.PreviewRootName == selected.Name
                && emitter.PreviewRootOccurrence == selected.Occurrence;
        }
        _identifiedEmitters = included;
    }

    public void Draw(CommonShaderParameters parameters) => DrawCore(parameters, null);
    public void Draw(CommonShaderParameters parameters, IDisposable? scene) => DrawCore(parameters, scene as SceneScope);
    private void DrawCore(CommonShaderParameters parameters, SceneScope? scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == null || _simulation == null || _reloadUploaded && !_reloadRetainsPlayback && HasPendingInteractiveReload) return;
        if (IsSeekingFrame && _simulation.NeedsGroundReplay(parameters.VfxGroundHeight))
            parameters = parameters with { VfxGroundHeight = _simulation.CurrentGroundHeight };
        parameters = parameters with
        {
            ViewportWidth = float.IsFinite(parameters.ViewportWidth) && parameters.ViewportWidth > 0
                ? parameters.ViewportWidth : device.Viewport.Width,
            ViewportHeight = float.IsFinite(parameters.ViewportHeight) && parameters.ViewportHeight > 0
                ? parameters.ViewportHeight : device.Viewport.Height,
        };
        var weather = parameters.GameWeather;
        var hasExposureSettings = weather != null && (weather.ToneMappingBrightness.HasValue || weather.ToneMappingBurn.HasValue
            || weather.AutomaticExposure.HasValue || weather.AutomaticExposureTarget.HasValue);
        var hasExposureDiagnostic = Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.WeatherExposureUnsupported");
        if (hasExposureSettings && !hasExposureDiagnostic)
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "tone_mapping", "Vfx.Preview.WeatherExposureUnsupported")];
        else if (!hasExposureSettings && hasExposureDiagnostic)
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.WeatherExposureUnsupported").ToArray();
        var postDiagnostic = Diagnostics.Any(x => x.Field == "post_processing" && x.ReasonKey == "Vfx.Preview.Unmapped");
        if (weather?.UnsupportedPostProcessing == true && !postDiagnostic)
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "post_processing", "Vfx.Preview.Unmapped")];
        else if (weather?.UnsupportedPostProcessing != true && postDiagnostic)
            Diagnostics = Diagnostics.Where(x => x.Field != "post_processing" || x.ReasonKey != "Vfx.Preview.Unmapped").ToArray();
        _simulation.ProjectionMemoEnabled = _interactiveFrameSeekEnabled || _interactivePrewarmEnabled;
        var frame = _projectedParticles;
        _simulation!.FillProjectedFrame(parameters, frame);
        if (_identifiedEmitters is { } included)
        {
            var visible = 0;
            for (var index = 0; index < frame.Count; index++)
            {
                var particle = frame[index];
                if ((uint)particle.Render.Layer < (uint)included.Length && included[particle.Render.Layer])
                    frame[visible++] = particle;
            }
            if (visible < frame.Count) frame.RemoveRange(visible, frame.Count - visible);
        }
        var needsGroundDepth = parameters.VfxWriteGroundDepth
            || frame.Any(p => p.RequiresGroundDepth || _conversion!.Emitters[p.Render.Layer] is { RequiresFlatTerrain: true, Kind: not 2 });
        var groundDepthUnavailable = false;
        if (needsGroundDepth)
        {
            _flatGroundDepth ??= new(device);
            groundDepthUnavailable = !_flatGroundDepth.Draw(parameters);
            if (groundDepthUnavailable)
                frame.RemoveAll(p => p.RequiresGroundDepth
                    || _conversion!.Emitters[p.Render.Layer].RequiresFlatTerrain && _conversion.Emitters[p.Render.Layer].Kind != 2);
        }
        if (groundDepthUnavailable && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.FlatTerrainDepthUnavailable"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "terrain depth", "Vfx.Preview.FlatTerrainDepthUnavailable")];
        else if (!groundDepthUnavailable && Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.FlatTerrainDepthUnavailable"))
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.FlatTerrainDepthUnavailable").ToArray();
        const string modelTerrainFallback = "Vfx.Preview.ModelTerrainBirthUnsupported";
        var hasModelTerrainFallback = Diagnostics.Any(x => x.Field == "model ground birth" && x.ReasonKey == modelTerrainFallback);
        if (_simulation.ModelTerrainBirthUnsupported && !hasModelTerrainFallback)
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "model ground birth", modelTerrainFallback)];
        else if (!_simulation.ModelTerrainBirthUnsupported && hasModelTerrainFallback)
            Diagnostics = Diagnostics.Where(x => x.Field != "model ground birth" || x.ReasonKey != modelTerrainFallback).ToArray();
        var terrainDiagnostic = Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.TerrainDecalBasisUnsupported");
        if (_simulation.TerrainBasisUnsupported && !terrainDiagnostic)
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "ParticleRelativeSpawnOrientation", "Vfx.Preview.TerrainDecalBasisUnsupported")];
        else if (!_simulation.TerrainBasisUnsupported && terrainDiagnostic)
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.TerrainDecalBasisUnsupported").ToArray();
        ReportCapacity();
        var particleCount = PrepareSubmission(frame);
        const string cullScaleField = "ParticleCullDistances/EmitterScale";
        var cullScaleDiagnostic = Diagnostics.Any(x => x.Field == cullScaleField && x.ReasonKey == "Vfx.Preview.RangeUnsupported");
        if (_simulation.SpriteCullScaleUnsupported && !cullScaleDiagnostic)
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", cullScaleField, "Vfx.Preview.RangeUnsupported")];
        else if (!_simulation.SpriteCullScaleUnsupported && cullScaleDiagnostic)
            Diagnostics = Diagnostics.Where(x => x.Field != cullScaleField || x.ReasonKey != "Vfx.Preview.RangeUnsupported").ToArray();
        var particles = _submissionParticles;
        if (weather != null && Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.WeatherMissing"))
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.WeatherMissing").ToArray();
        var lighting = CreateLighting(parameters);
        if (parameters.VfxGroundHeight is { } groundHeight && float.IsFinite(groundHeight))
        {
            lighting.Parameters.Z = 1; lighting.Parameters.W = groundHeight;
            if (_conversion!.Emitters.Any(x => x.Kind == 2) && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.FlatGroundReceiver"))
                Diagnostics = [.. Diagnostics, new(_sourcePath, "", "scene_view_type", "Vfx.Preview.FlatGroundReceiver")];
        }
        else if (Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.FlatGroundReceiver"))
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.FlatGroundReceiver").ToArray();
        if (scene != null) scene.Retain(_resources!);
        EffekseerNative.Check(scene == null
            ? EffekseerNative.AecnVfxDraw(_handle, Values(parameters.View), Values(parameters.Projection), particles, particleCount, in lighting,
                parameters.VfxSoftDepth?.NativePointer ?? 0)
            : EffekseerNative.AecnVfxDrawInScene(scene.NativeOwner, _handle, Values(parameters.View), Values(parameters.Projection), particles, particleCount, in lighting,
                parameters.VfxSoftDepth?.NativePointer ?? 0));
        if (weather == null && _conversion!.Emitters.Any(x => x.Render.LightingMode != 0)
            && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.WeatherMissing"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "weather", "Vfx.Preview.WeatherMissing")];
        if (_simulation.VelocityScaleUnsupported && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.VelocityScaleUnsupported"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "velocity_stretch_mul", "Vfx.Preview.VelocityScaleUnsupported")];
        if (particleCount > 0 && _conversion!.Emitters.Any(x => x.Render.SoftDistance > 0 || x.Render.DistortionSoftDepth != 0)
            && EffekseerNative.AecnVfxDepthAvailable(_handle) == 0 && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.SceneDepthUnavailable"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "soft_factor", "Vfx.Preview.SceneDepthUnavailable")];
        else if (particleCount > 0 && EffekseerNative.AecnVfxDepthAvailable(_handle) != 0
            && Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.SceneDepthUnavailable"))
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.SceneDepthUnavailable").ToArray();
    }

    private int PrepareSubmission(IReadOnlyList<VfxPreviewParticle> frame)
    {
        var rendered = _renderedParticles;
        rendered.Clear(); rendered.EnsureCapacity(frame.Count * 2);
        var opaque = _opaqueParticles;
        opaque.Clear();
        for (var frameIndex = 0; frameIndex < frame.Count; frameIndex++)
        {
            var projected = frame[frameIndex];
            var particle = projected.Render;
            var coverage = particle;
            if (_conversion!.Emitters[particle.Layer].Render.LightingMode != 0 && particle.NormalRotation == Vector4.Zero)
            {
                var normalTransform = particle.Transform;
                // Velocity-v2 uses the native particle tangent/front basis, opposite the RH camera billboard axes.
                if (_conversion.Emitters[particle.Layer].Alignment == 3)
                    normalTransform = Matrix.CreateRotationY(MathHelper.Pi) * normalTransform;
                if (normalTransform.Decompose(out var scale, out var rotation, out _) && scale.X > 0 && scale.Y > 0 && scale.Z > 0)
                {
                    coverage.NormalRotation = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    coverage.ParticleSize = scale.X;
                }
                else coverage.ParticleSize = -1;
            }
            var rawCoverage = coverage;
            var coverageMultiplier = _conversion!.Emitters[particle.Layer].CoveredColourMultiplier;
            coverage.Colour.X *= coverageMultiplier; coverage.Colour.Y *= coverageMultiplier; coverage.Colour.Z *= coverageMultiplier;
            var layers = _renderLayers[particle.Layer][projected.ModelVariant];
            for (var index = 0; index < layers.Coverage.Length; index++)
            {
                var submitted = index < layers.RawHalf.Length && layers.RawHalf[index] ? rawCoverage : coverage;
                submitted.Layer = layers.Coverage[index];
                if (submitted.Layer >= 0)
                {
                    if (_opaqueLayers.Contains(submitted.Layer)) opaque.Add(submitted); else rendered.Add(submitted);
                }
                if (layers.Emissive[index] < 0) continue;
                var emissive = particle; emissive.Layer = layers.Emissive[index];
                var multiplier = _conversion.Emitters[particle.Layer].AdditiveMultiplier;
                emissive.Colour.X *= multiplier; emissive.Colour.Y *= multiplier; emissive.Colour.Z *= multiplier;
                rendered.Add(emissive);
            }
        }
        // Keep each sorted particle's emission behind subsequent particle coverage.
        var count = opaque.Count + rendered.Count;
        if (_submissionParticles.Length < count)
            Array.Resize(ref _submissionParticles, Math.Max(count, Math.Max(4, _submissionParticles.Length * 2)));
        opaque.CopyTo(_submissionParticles);
        rendered.CopyTo(_submissionParticles, opaque.Count);
        return count;
    }

    public IDisposable? BeginScene(CommonShaderParameters parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _handle == null || ParticleCount == 0 ? null : new SceneScope(_resources!, CreateLighting(parameters));
    }

    private sealed class SceneScope : IDisposable
    {
        private EffekseerHandle? _owner;
        private readonly List<PlaybackResources> _resources = [];
        private bool _disposed;
        internal nint NativeOwner => _owner?.DangerousGetHandle() ?? throw new ObjectDisposedException(nameof(SceneScope));
        public SceneScope(PlaybackResources resources, EffekseerLighting lighting)
        {
            Retain(resources);
            var owner = resources.Handle!;
            var held = false;
            try
            {
                owner.DangerousAddRef(ref held);
                EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(owner, in lighting));
                _owner = owner;
            }
            catch
            {
                if (held) owner.DangerousRelease();
                foreach (var resource in _resources) resource.Release();
                throw;
            }
        }
        internal void Retain(PlaybackResources resources)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_resources.Contains(resources)) return;
            if (!resources.TryRetain()) throw new InvalidOperationException("VFX scene resources have already been released.");
            try { _resources.Add(resources); }
            catch { resources.Release(); throw; }
        }
        public void Dispose()
        {
            _disposed = true;
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            try { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(owner.DangerousGetHandle())); }
            finally
            {
                try { owner.DangerousRelease(); }
                finally
                {
                    foreach (var resource in _resources) resource.Release();
                    _resources.Clear();
                }
            }
        }
    }

    private static EffekseerLighting CreateLighting(CommonShaderParameters parameters)
    {
        var weather = parameters.GameWeather;
        var lighting = weather == null ? default : new EffekseerLighting
        {
            PositiveX = new(weather.PositiveX, 0), NegativeX = new(weather.NegativeX, 0),
            PositiveY = new(weather.PositiveY, 0), NegativeY = new(weather.NegativeY, 0),
            PositiveZ = new(weather.PositiveZ, 0), NegativeZ = new(weather.NegativeZ, 0),
            SunDirection = new(weather.SunDirection, weather.SunScale / 20000), SunColour = new(weather.SunColour, 0),
            Parameters = new(1, weather.Brightness, 0, 0),
        };
        // Fixed native reference for display targets; floating targets retain scene-linear radiance.
        var fixedExposure = weather?.AutomaticExposure == false
            && weather.ToneMappingBrightness is > 0 and <= 100
            && weather.ToneMappingBurn is >= 0 and <= 1;
        lighting.Display = new(1, fixedExposure ? weather!.ToneMappingBrightness!.Value : 1,
            fixedExposure ? weather!.ToneMappingBurn!.Value : .05f, (weather?.UniformContrast ?? 1) - 1);
        // A uniform weather gain needs no game G-buffer classification or MBOIT particle mask.
        var uniformBloom = weather?.BloomThreshold != null && weather.BloomStrength != null
            && weather.BloomStrength == weather.BloomParticleStrength && weather.BloomStrength == weather.BloomEmissiveStrength
            && weather.BloomStrength == weather.BloomWaterStrength;
        lighting.Bloom = new(uniformBloom ? Math.Max(0, weather!.BloomThreshold!.Value) : 4,
            uniformBloom ? weather!.BloomStrength!.Value : .1f, 0, 0);
        return lighting;
    }

    private void ReportCapacity()
    {
        if (_spriteSpinRangeDiagnostic != null && _simulation?.SpriteSpinRangeUnsupported != true)
        {
            Diagnostics = Diagnostics.Where(diagnostic => !ReferenceEquals(diagnostic, _spriteSpinRangeDiagnostic)).ToArray();
            _spriteSpinRangeDiagnostic = null;
        }
        if (_simulation?.ModelSpinRangeUnsupported == true
            && !Diagnostics.Any(x => x.Field == "ParticleSpinDeceleration" && x.ReasonKey == "Vfx.Preview.RangeUnsupported"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "ParticleSpinDeceleration", "Vfx.Preview.RangeUnsupported")];
        if (_simulation?.SpriteSpinRangeUnsupported == true
            && !Diagnostics.Any(x => x.Field == "ParticleSpinDeceleration" && x.ReasonKey == "Vfx.Preview.RangeUnsupported"))
        {
            _spriteSpinRangeDiagnostic = new(_sourcePath, "", "ParticleSpinDeceleration", "Vfx.Preview.RangeUnsupported");
            Diagnostics = [.. Diagnostics, _spriteSpinRangeDiagnostic];
        }
        if ((_simulation?.ModelSpinBasisUnsupported == true || _simulation?.SpriteSpinBasisUnsupported == true)
            && !Diagnostics.Any(x => x.Field == "ParticleSimulationSpace" && x.ReasonKey == "Vfx.Preview.SpinCandidate"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "ParticleSimulationSpace", "Vfx.Preview.SpinCandidate")];
        if (_simulation?.ModelMotionScaleUnsupported == true
            && !Diagnostics.Any(x => x.Field == "EmitterPosition/RootScale/InheritedScale" && x.ReasonKey == "Vfx.Preview.RangeUnsupported"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "EmitterPosition/RootScale/InheritedScale", "Vfx.Preview.RangeUnsupported")];
        if (_simulation?.ForceBasisUnsupported == true && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.ForceBasisUnsupported"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "ForceA", "Vfx.Preview.ForceBasisUnsupported")];
        if (_simulation?.RateLimited == true && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.RateLimited"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "EmissionRateCurve", "Vfx.Preview.RateLimited")];
        if (_simulation?.CapacityReached == true && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.CapacityLimited"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "", "Vfx.Preview.CapacityLimited")];
    }

    private static float[] Values(Matrix matrix) => [
        matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44];

    private sealed record RenderLayers(int[] Coverage, int[] Emissive)
    {
        // Both explicit alpha resolutions submit one unscaled source particle.
        public bool[] RawHalf { get; init; } = [];
    }
    private sealed record HalfCandidate(int Emitter, int Variant, int Part, EffekseerLayer Layer);
    private sealed record BoundsSnapshot(BoundingBox? Value);

    // Only complete, immutable uploads are shared. Timing, simulation and diagnostics remain instance-local.
    private sealed class PlaybackResources(EffekseerHandle? handle, VfxConversion conversion, RenderLayers[][] renderLayers,
        int[] opaqueLayers, BoundingBox? bounds, IReadOnlyList<VfxPreviewDiagnostic> diagnostics, string sourcePath,
        string? resourceIdentity = null, byte?[]? constantAlphas = null)
    {
        private int _references = 1;
        private EffekseerHandle? _handle = handle;
        public EffekseerHandle? Handle => _handle;
        public bool HasExclusiveHandle => Volatile.Read(ref _references) == 1 && _handle is { IsInvalid: false, IsClosed: false };
        public string? ResourceIdentity { get; } = resourceIdentity;
        public byte?[] ConstantAlphas { get; } = constantAlphas ?? [];
        public VfxConversion Conversion { get; } = conversion;
        public RenderLayers[][] RenderLayers { get; } = renderLayers;
        public int[] OpaqueLayers { get; } = opaqueLayers;
        private BoundsSnapshot _bounds = new(bounds);
        public BoundingBox? Bounds => Volatile.Read(ref _bounds).Value;
        public void PublishBounds(BoundingBox? value) => Volatile.Write(ref _bounds, new(value));
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; } = diagnostics;
        public string SourcePath { get; } = sourcePath;
        public bool TryRetain()
        {
            var count = Volatile.Read(ref _references);
            while (count != 0)
            {
                var previous = Interlocked.CompareExchange(ref _references, count + 1, count);
                if (previous == count) return true;
                count = previous;
            }
            return false;
        }
        public void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0) Interlocked.Exchange(ref _handle, null)?.Dispose();
        }
        public bool TryTakeExclusiveHandle(out EffekseerHandle? value)
        {
            value = null;
            if (_handle is not { IsInvalid: false, IsClosed: false }
                || Interlocked.CompareExchange(ref _references, 0, 1) != 1) return false;
            value = Interlocked.Exchange(ref _handle, null);
            return value != null;
        }
    }

    private void ReleaseResources()
    {
        CancelPreparation();
        var resources = _resources;
        _resources = null;
        _handle = null;
        resources?.Release();
    }

    private void CancelPreparation()
    {
        CancelNormalFrameSeek();
        _normalSeekInitializationKnown = false;
        _interactiveBounds?.Dispose();
        _interactiveBounds = null;
        _interactivePrewarm?.Dispose();
        _interactivePrewarm = null;
        _interactiveRestartPending = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelInteractiveReload();
        ReleaseResources();
        _flatGroundDepth?.Dispose();
        _flatGroundDepth = null;
        _conversion = null;
        _simulation = null;
        _identification = null;
        _identifiedEmitters = null;
        _projectedParticles.Clear(); _projectedParticles.Capacity = 0;
        _renderedParticles.Clear(); _renderedParticles.Capacity = 0;
        _opaqueParticles.Clear(); _opaqueParticles.Capacity = 0;
        _submissionParticles = [];
    }
}
