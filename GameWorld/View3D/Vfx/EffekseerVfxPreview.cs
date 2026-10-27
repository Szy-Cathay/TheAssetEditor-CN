using System.Security.Cryptography;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Vfx;

public sealed class EffekseerVfxPreview(GraphicsDevice device) : IVfxPreview
{
    private EffekseerHandle? _handle;
    private PlaybackResources? _resources;
    private VfxConversion? _conversion;
    private TotalWarVfxSimulation? _simulation;
    private bool _interactivePrewarmEnabled;
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
    public int EmitterCount => _handle == null ? 0 : _conversion?.Emitters.Count(e => !e.IsEventTemplate) ?? 0;
    public int TotalEmitterCount => _conversion?.TotalEmitterCount ?? 0;
    public int ParticleCount => _simulation?.ParticleCount ?? 0;
    public int DisabledEmitterCount => _conversion?.DisabledEmitterCount ?? 0;
    public float CycleDuration => _simulation?.CycleDuration ?? 8;
    public bool HasInfiniteEmitters => _conversion?.Emitters.Any(emitter => !emitter.IsEventTemplate && emitter.Infinite) == true;
    public bool HasMotionTrailEmitters => _simulation?.HasMotionTrailEmitters == true;
    public void EnableInteractivePrewarm() => _interactivePrewarmEnabled = true;
    public bool IsPreparing => _interactivePrewarm != null && (_simulation == null || _interactiveRestartPending);
    public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? attachmentAtTime)
    {
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
        if (_interactivePrewarm == null) return;
        var key = PrewarmKey(attachment, attachmentAtTime);
        if (!_interactivePrewarm.Request(key))
        {
            _interactivePrewarm.Dispose(); _interactivePrewarm = null; _interactiveRestartPending = false;
            _simulation ??= new(_conversion!) { EmissionEndTimeSeconds = _emissionEndTimeSeconds };
            return;
        }
        if (IsPreparing && _interactivePrewarm.TryTake(key, out var prepared))
        {
            _simulation = prepared!.Simulation;
            PreviewBounds = prepared.Bounds;
            _resources?.PublishBounds(prepared.Bounds);
            _interactiveRestartPending = false;
        }
    }

    private VfxInteractivePrewarmKey PrewarmKey(Matrix attachment, Func<float, Matrix>? history)
        => new(history?.Invoke(0) ?? attachment, attachment, attachment.Translation.Y, _emissionEndTimeSeconds);

    private bool PrepareInteractiveSeek(float seconds, Matrix attachment, Func<float, Matrix>? history)
    {
        if (_interactivePrewarm == null) return true;
        if (!_interactiveRestartPending && _simulation != null && seconds + .00001f >= _simulation.Time) return true;
        _interactiveRestartPending = true;
        PrepareInteractiveFrame(attachment, history);
        return !IsPreparing;
    }
    public float? EmissionEndTimeSeconds
    {
        get => _emissionEndTimeSeconds;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value is { } end && !float.IsFinite(end)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_interactivePrewarm != null && _emissionEndTimeSeconds != value) _interactiveRestartPending = true;
            _emissionEndTimeSeconds = value;
            if (_simulation != null) _simulation.EmissionEndTimeSeconds = value;
        }
    }
    public bool IsDrained => _simulation?.IsDrained ?? true;
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
        ReleaseResources();
        _renderLayers = [];
        _opaqueLayers.Clear();
        _spriteSpinRangeDiagnostic = null;
        _sourcePath = snapshot.SourcePath;
        PreviewBounds = null;
        _conversion = TotalWarVfxConverter.Convert(snapshot);
        _simulation = new(_conversion) { EmissionEndTimeSeconds = _emissionEndTimeSeconds };
        Diagnostics = _conversion.Diagnostics;
        if (_conversion.Emitters.Count == 0)
        {
            if (!Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.MissingResource"))
                _resources = new(null, _conversion, _renderLayers, [], null, Diagnostics, _sourcePath);
            return;
        }
        try
        {
            if (EffekseerNative.AecnVfxAbi(System.Runtime.InteropServices.Marshal.SizeOf<EffekseerLayer>(),
                System.Runtime.InteropServices.Marshal.SizeOf<EffekseerParticle>(), System.Runtime.InteropServices.Marshal.SizeOf<EffekseerVertex>(),
                System.Runtime.InteropServices.Marshal.SizeOf<EffekseerLighting>()) != 1)
                throw new InvalidOperationException("Effekseer ABI mismatch");
            var nativeDevice = (SharpDX.Direct3D11.Device)device.Handle;
            var handle = EffekseerNative.AecnVfxCreate(nativeDevice.NativePointer, nativeDevice.ImmediateContext.NativePointer);
            try
            {
                EffekseerNative.Check(handle.IsInvalid ? 0 : 1);
                var failedTextures = new HashSet<int>();
                var uploadedTextures = new Dictionary<(VfxTextureUsage Usage, string Hash), (int Index, bool Mipmaps, byte? Alpha)>();
                var constantAlphas = new byte?[_conversion.Textures.Count];
                var identities = _conversion.Textures.Select((bytes, index) =>
                    (_conversion.TextureUsages[index], Convert.ToHexStringLower(SHA256.HashData(bytes)))).ToArray();
                var maskIdentities = _conversion.MeshMaterials.SelectMany(m =>
                    m.TextureIndexes.Length == 7 && m.Parameters.Length == 36 ? m.TextureIndexes.Skip(3).Take(2)
                    : m.TextureIndexes.Length == 10 && m.Parameters.Length == 60 ? new[] { m.TextureIndexes[1], m.TextureIndexes[2], m.TextureIndexes[4] } : [])
                    .Select(index => identities[index]).ToHashSet();
                var maskImages = new Dictionary<(VfxTextureUsage Usage, string Hash), (byte[] Pixels, int Width, int Height)>();
                var srgbMasks = new HashSet<(VfxTextureUsage Usage, string Hash)>();
                for (var index = 0; index < _conversion.Textures.Count; ++index)
                {
                    try
                    {
                        var identity = identities[index];
                        bool mipmaps;
                        if (uploadedTextures.TryGetValue(identity, out var uploaded))
                        {
                            EffekseerNative.Check(EffekseerNative.AecnVfxTextureAlias(handle, uploaded.Index));
                            mipmaps = uploaded.Mipmaps;
                            constantAlphas[index] = uploaded.Alpha;
                        }
                        else
                        {
                            var image = DecodeTexture(_conversion.Textures[index], _conversion.TextureUsages[index]);
                            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, image.Pixels, image.Pixels.Length, image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
                            mipmaps = image.MipLevels > 1;
                            constantAlphas[index] = ConstantAlpha(image.Pixels);
                            uploadedTextures.Add(identity, (index, mipmaps, constantAlphas[index]));
                            if (maskIdentities.Contains(identity))
                            {
                                maskImages.Add(identity, (image.Pixels[..(image.Width * image.Height * 4)], image.Width, image.Height));
                                if (image.Srgb) srgbMasks.Add(identity);
                            }
                        }
                        if (mipmaps)
                            Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, "", _conversion.TexturePaths[index], "Vfx.Preview.MipmapsUnsupported")];
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        if (_conversion.TextureUsages[index] == VfxTextureUsage.ShadingNormal)
                        {
                            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [0, 128, 0, 128], 4, 1, 1, 0));
                            foreach (var emitter in _conversion.Emitters.Where(x => x.Render.NormalIndex == index))
                            {
                                var fallback = emitter.Render; fallback.LightingMode = 0; emitter.Render = fallback;
                                Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, emitter.Name, _conversion.TexturePaths[index], "Vfx.Preview.ShadingNormalUnsupported")];
                            }
                            continue;
                        }
                        Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, "", _conversion.TexturePaths[index], "Vfx.Preview.TextureUnsupported")];
                        // Preserve resource indexes while excluding every emitter that needs this texture.
                        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [0, 0, 0, 0], 4, 1, 1, 0));
                        failedTextures.Add(index);
                    }
                }
                if (failedTextures.Count > 0)
                {
                    bool UsesFailedTexture(VfxPreviewEmitter emitter) => Enumerable.Range(0, emitter.ModelVariants.Count + 1)
                        .SelectMany(emitter.RenderParts).Any(part => failedTextures.Contains(part.TextureIndex) || failedTextures.Contains(part.GradientIndex)
                            || failedTextures.Contains(part.DistortionMaskIndex)
                            || part.MeshMaterialIndex >= 0 && _conversion.MeshMaterials[part.MeshMaterialIndex].TextureIndexes.Any(failedTextures.Contains));
                    foreach (var emitter in _conversion.Emitters.Where(UsesFailedTexture))
                        Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, emitter.Name, "texture", "Vfx.Preview.LayerTextureUnavailable")];
                    _conversion.Emitters.RemoveAll(UsesFailedTexture);
                    _simulation = new(_conversion) { EmissionEndTimeSeconds = _emissionEndTimeSeconds };
                    if (_conversion.Emitters.Count == 0) { handle.Dispose(); return; }
                }
                var packedMasks = new Dictionary<((VfxTextureUsage, string), (VfxTextureUsage, string)), (int Index, float[] Rectangles)>();
                var packedRigidMasks = new Dictionary<((VfxTextureUsage, string), (VfxTextureUsage, string), (VfxTextureUsage, string)), (int Index, float[] Rectangles)>();
                var nextPackedIndex = _conversion.Textures.Count;
                var failedMaterials = new HashSet<int>();
                for (var index = 0; index < _conversion.MeshMaterials.Count; ++index)
                {
                    var material = _conversion.MeshMaterials[index];
                    var textures = material.TextureIndexes; var parameters = material.Parameters;
                    if (textures.Length == 7 && parameters.Length == 36)
                    {
                        var packedIndex = textures[0]; parameters = (float[])parameters.Clone();
                        if (!textures.Any(failedTextures.Contains))
                        {
                            try
                            {
                                var key = (identities[textures[3]], identities[textures[4]]);
                                if (!packedMasks.TryGetValue(key, out var packed))
                                {
                                    var first = maskImages[key.Item1]; var second = maskImages[key.Item2];
                                    var atlas = VfxMaterialMaskAtlas.Pack(first.Pixels, first.Width, first.Height, second.Pixels, second.Width, second.Height);
                                    EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, atlas.Pixels, atlas.Pixels.Length, atlas.Width, atlas.Height, 0));
                                    packed = (nextPackedIndex++, atlas.Rectangles); packedMasks.Add(key, packed);
                                }
                                packedIndex = packed.Index; packed.Rectangles.CopyTo(parameters, 28);
                            }
                            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
                            {
                                failedMaterials.Add(index);
                            }
                        }
                        textures = [textures[0], textures[1], textures[2], packedIndex, textures[5], textures[6]];
                    }
                    else if (textures.Length == 12 && parameters.Length == VfxTidecallMaterial.ParameterCount)
                    {
                        parameters = (float[])parameters.Clone();
                        var palette = new List<int>();
                        var aliases = new Dictionary<(VfxTextureUsage, string), int>();
                        for (var logical = 0; logical < textures.Length; ++logical)
                        {
                            // Keep the original mip chains; extra PS resources and VS displacement use separate bindings.
                            if (logical == 2) continue;
                            var identity = identities[textures[logical]];
                            if (!aliases.TryGetValue(identity, out var alias))
                            {
                                alias = palette.Count;
                                aliases.Add(identity, alias); palette.Add(textures[logical]);
                            }
                            parameters[VfxTidecallMaterial.SourceParameterCount + logical] = alias;
                        }
                        if (palette.Count > 11)
                        {
                            failedMaterials.Add(index);
                            palette.Clear(); palette.Add(textures[0]);
                            Array.Clear(parameters, VfxTidecallMaterial.SourceParameterCount, 12);
                        }
                        while (palette.Count < 11) palette.Add(palette[0]);
                        palette.Add(textures[2]);
                        textures = palette.ToArray();
                    }
                    else if (textures.Length == 10 && parameters.Length == 60)
                    {
                        var packedIndex = textures[0]; parameters = (float[])parameters.Clone();
                        Array.Clear(parameters, 46, 12);
                        for (var rectangle = 46; rectangle < 58; rectangle += 4)
                            parameters[rectangle + 2] = parameters[rectangle + 3] = 1;
                        if (!textures.Any(failedTextures.Contains))
                        {
                            try
                            {
                                var key = (identities[textures[1]], identities[textures[2]], identities[textures[4]]);
                                if (!packedRigidMasks.TryGetValue(key, out var packed))
                                {
                                    var srgb = srgbMasks.Contains(key.Item1);
                                    if (srgbMasks.Contains(key.Item2) != srgb || srgbMasks.Contains(key.Item3) != srgb)
                                        throw new NotSupportedException("VFX packed masks require matching sampling colour spaces");
                                    var atlas = VfxMaterialMaskAtlas.PackThree(maskImages[key.Item1], maskImages[key.Item2], maskImages[key.Item3]);
                                    EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, atlas.Pixels, atlas.Pixels.Length, atlas.Width, atlas.Height, srgb ? 1 : 0));
                                    packed = (nextPackedIndex++, atlas.Rectangles); packedRigidMasks.Add(key, packed);
                                }
                                packedIndex = packed.Index; packed.Rectangles.CopyTo(parameters, 46);
                            }
                            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
                            {
                                failedMaterials.Add(index);
                            }
                        }
                        textures = [textures[0], textures[3], textures[7], textures[6], textures[5], packedIndex];
                    }
                    EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle, textures, textures.Length, parameters, parameters.Length));
                }
                if (failedMaterials.Count > 0)
                {
                    bool UsesFailedMaterial(VfxPreviewEmitter emitter) => Enumerable.Range(0, emitter.ModelVariants.Count + 1)
                        .SelectMany(emitter.RenderParts).Any(part => failedMaterials.Contains(part.MeshMaterialIndex));
                    foreach (var emitter in _conversion.Emitters.Where(UsesFailedMaterial))
                        Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, emitter.Name, "material masks", "Vfx.Preview.TextureUnsupported")];
                    _conversion.Emitters.RemoveAll(UsesFailedMaterial);
                    _simulation = new(_conversion) { EmissionEndTimeSeconds = _emissionEndTimeSeconds };
                    if (_conversion.Emitters.Count == 0) { handle.Dispose(); return; }
                }
                var uploadedModels = new Dictionary<VfxPreviewModel, int>(ReferenceEqualityComparer.Instance);
                for (var index = 0; index < _conversion.Models.Count; ++index)
                {
                    var model = _conversion.Models[index];
                    if (uploadedModels.TryGetValue(model, out var sourceIndex))
                        EffekseerNative.Check(EffekseerNative.AecnVfxModelAlias(handle, sourceIndex));
                    else
                    {
                        if (model.AnimationVertices is { } frames)
                            EffekseerNative.Check(EffekseerNative.AecnVfxModelFrames(handle, frames, frames.Length, model.Vertices.Length,
                                model.AnimationFrameCount, model.Indices, model.Indices.Length, model.AnimationCycles));
                        else EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, model.Vertices, model.Vertices.Length, model.Indices, model.Indices.Length));
                        uploadedModels.Add(model, index);
                    }
                }
                var configuration = BuildRenderLayers(constantAlphas);
                if (configuration.Layers.Length > 0)
                    EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, configuration.Layers, configuration.Layers.Length));
                if ((configuration.HalfLayers.Length > 0 || configuration.FullLayers.Length > 0)
                    && System.Runtime.InteropServices.Marshal.SizeOf<EffekseerHalfLayer>() != EffekseerHalfLayer.Bytes)
                    throw new InvalidOperationException("Unexpected raw-alpha layer configuration size.");
                if (configuration.HalfLayers.Length > 0)
                {
                    EffekseerNative.Check(EffekseerNative.AecnVfxConfigureHalf(handle, configuration.HalfLayers,
                        configuration.HalfLayers.Length, EffekseerHalfLayer.Bytes));
                }
                if (configuration.FullLayers.Length > 0)
                    EffekseerNative.Check(EffekseerNative.AecnVfxConfigureFull(handle, configuration.FullLayers,
                        configuration.FullLayers.Length, EffekseerHalfLayer.Bytes));
                if (_interactivePrewarmEnabled && VfxInteractivePrewarm.CanPrepare(_conversion))
                {
                    _interactivePrewarm = new(_conversion);
                    _simulation = null;
                }
                else if (_interactivePrewarmEnabled) _interactiveBounds = new(_conversion);
                else PreviewBounds = VfxPreviewBounds.Estimate(_conversion);
                _resources = new(handle, _conversion, _renderLayers, _opaqueLayers.ToArray(), PreviewBounds, Diagnostics, _sourcePath);
                _handle = handle;
            }
            catch { handle.Dispose(); throw; }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            Logging.Create<EffekseerVfxPreview>().Error(exception, "Effekseer preview load failed for {Path}", snapshot.SourcePath);
            Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, "", "", "Vfx.Preview.RuntimeUnavailable")];
        }
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

    private static byte? ConstantAlpha(byte[] pixels)
    {
        var alpha = pixels[3];
        if (alpha is not (0 or 255)) return null;
        for (var index = 7; index < pixels.Length; index += 4)
            if (pixels[index] != alpha) return null;
        return alpha;
    }

    private static (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) DecodeTexture(byte[] gameBytes, VfxTextureUsage usage)
    {
        if (usage == VfxTextureUsage.SolidWhite)
        {
            if (gameBytes.Length != 4 || gameBytes.Any(value => value != 255)) throw new InvalidOperationException("Invalid untextured VFX identity");
            return (gameBytes, 1, 1, false, 1);
        }
        if (usage == VfxTextureUsage.GradientValue)
        {
            if (gameBytes.Length != VfxPreviewGradient.Width * 4) throw new InvalidOperationException("Invalid VFX gradient lookup");
            return (gameBytes, VfxPreviewGradient.Width, 1, false, 1);
        }
        using var image = ImageLoader.LoadImageFromBytes(gameBytes, out var information);
        if (image.Format != Pfim.ImageFormat.Rgba32 || image.Width is < 1 or > 4096 || image.Height is < 1 or > 4096
            || image.Stride < image.Width * 4 || (long)image.Stride * image.Height > image.DataLen)
            throw new NotSupportedException("Unsupported decoded VFX image layout");
        var layouts = new List<(int Width, int Height, int Stride, int Offset)> { (image.Width, image.Height, image.Stride, 0) };
        var width = image.Width; var height = image.Height;
        foreach (var mip in image.MipMaps)
        {
            if (width == 1 && height == 1) throw new NotSupportedException("Invalid decoded VFX mip count");
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            if (mip.Width != width || mip.Height != height || mip.Stride < width * 4 || mip.DataOffset < 0
                || (long)mip.Stride * height > mip.DataLen || (long)mip.DataOffset + mip.DataLen > image.Data.Length)
                throw new NotSupportedException("Unsupported decoded VFX mip layout");
            layouts.Add((width, height, mip.Stride, mip.DataOffset));
        }
        var pixels = new byte[layouts.Sum(layout => checked(layout.Width * layout.Height * 4))];
        var target = 0;
        // Pfim keeps block padding in the smallest mips. Upload only each level's actual texels.
        foreach (var layout in layouts)
            for (var row = 0; row < layout.Height; ++row)
                for (var column = 0; column < layout.Width; ++column)
                {
                    var source = layout.Offset + row * layout.Stride + column * 4;
                    pixels[target] = image.Data[source + 2]; pixels[target + 1] = image.Data[source + 1];
                    pixels[target + 2] = image.Data[source]; pixels[target + 3] = usage == VfxTextureUsage.OpaqueDiffuse ? (byte)255 : image.Data[source + 3];
                    target += 4;
                }
        // ps_texture samples diffuse colour as sRGB, including legacy DDS files without a colour-space tag.
        // Numeric material slots stay linear for legacy DDS, but explicit sRGB formats retain their native sampling.
        var srgb = usage is VfxTextureUsage.Diffuse or VfxTextureUsage.OpaqueDiffuse
            || usage == VfxTextureUsage.MaterialValue && information.Header10_DxgiFormat is
                Pfim.DxgiFormat.R8G8B8A8_UNORM_SRGB or Pfim.DxgiFormat.B8G8R8A8_UNORM_SRGB
                or Pfim.DxgiFormat.B8G8R8X8_UNORM_SRGB or Pfim.DxgiFormat.BC1_UNORM_SRGB
                or Pfim.DxgiFormat.BC2_UNORM_SRGB or Pfim.DxgiFormat.BC3_UNORM_SRGB or Pfim.DxgiFormat.BC7_UNORM_SRGB;
        if (usage == VfxTextureUsage.DistortionNormal)
        {
            if (information.IsDdsImage && information.Header_PixelFormat.FourCC == Pfim.CompressionAlgorithm.D3DFMT_DXT5)
                TotalWarVfxMaterial.ConvertDxt5Normal(pixels);
            else
                throw new NotSupportedException("Unconfirmed game distortion normal encoding");
            srgb = false;
        }
        if (usage == VfxTextureUsage.ShadingNormal && (!information.IsDdsImage
            || information.Header_PixelFormat.FourCC is not (Pfim.CompressionAlgorithm.D3DFMT_DXT5 or 0)))
            throw new NotSupportedException("Unconfirmed game shading normal encoding");
        return (pixels, image.Width, image.Height, srgb, layouts.Count);
    }

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
        if (!PrepareInteractiveSeek(seconds, attachment, null)) return;
        _simulation?.Seek(seconds, attachment);
        ReportCapacity();
    }
    public void Seek(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PrepareInteractiveSeek(seconds, attachment, attachmentAtTime)) return;
        _simulation?.Seek(seconds, attachment, attachmentAtTime);
        ReportCapacity();
    }

    public void Seek(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime, bool useCurrentAttachmentForFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        _simulation?.SeekContinuousTrail(seconds, attachment, attachmentAtTime, nextAttachmentSample);
        ReportCapacity();
    }

    public void Draw(CommonShaderParameters parameters) => DrawCore(parameters, null);
    public void Draw(CommonShaderParameters parameters, IDisposable? scene) => DrawCore(parameters, scene as SceneScope);
    private void DrawCore(CommonShaderParameters parameters, SceneScope? scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == null || _simulation == null) return;
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
        var frame = _projectedParticles;
        _simulation!.FillProjectedFrame(parameters, frame);
        var needsGroundDepth = parameters.VfxWriteGroundDepth
            || frame.Any(p => _conversion!.Emitters[p.Render.Layer] is { RequiresFlatTerrain: true, Kind: not 2 });
        var groundDepthUnavailable = false;
        if (needsGroundDepth)
        {
            _flatGroundDepth ??= new(device);
            groundDepthUnavailable = !_flatGroundDepth.Draw(parameters);
            if (groundDepthUnavailable)
                frame.RemoveAll(p => _conversion!.Emitters[p.Render.Layer].RequiresFlatTerrain && _conversion.Emitters[p.Render.Layer].Kind != 2);
        }
        if (groundDepthUnavailable && !Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.FlatTerrainDepthUnavailable"))
            Diagnostics = [.. Diagnostics, new(_sourcePath, "", "terrain depth", "Vfx.Preview.FlatTerrainDepthUnavailable")];
        else if (!groundDepthUnavailable && Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.FlatTerrainDepthUnavailable"))
            Diagnostics = Diagnostics.Where(x => x.ReasonKey != "Vfx.Preview.FlatTerrainDepthUnavailable").ToArray();
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
        return _handle == null || ParticleCount == 0 ? null : new SceneScope(_handle, CreateLighting(parameters));
    }

    private sealed class SceneScope : IDisposable
    {
        private EffekseerHandle? _owner;
        internal nint NativeOwner => _owner?.DangerousGetHandle() ?? throw new ObjectDisposedException(nameof(SceneScope));
        public SceneScope(EffekseerHandle owner, EffekseerLighting lighting)
        {
            var held = false;
            owner.DangerousAddRef(ref held);
            try
            {
                EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(owner, in lighting));
                _owner = owner;
            }
            catch
            {
                if (held) owner.DangerousRelease();
                throw;
            }
        }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            try { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(owner.DangerousGetHandle())); }
            finally { owner.DangerousRelease(); }
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
        int[] opaqueLayers, BoundingBox? bounds, IReadOnlyList<VfxPreviewDiagnostic> diagnostics, string sourcePath)
    {
        private int _references = 1;
        public EffekseerHandle? Handle { get; } = handle;
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
            if (Interlocked.Decrement(ref _references) == 0) Handle?.Dispose();
        }
    }

    private void ReleaseResources()
    {
        _interactiveBounds?.Dispose();
        _interactiveBounds = null;
        _interactivePrewarm?.Dispose();
        _interactivePrewarm = null;
        _interactiveRestartPending = false;
        var resources = _resources;
        _resources = null;
        _handle = null;
        resources?.Release();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseResources();
        _flatGroundDepth?.Dispose();
        _flatGroundDepth = null;
        _conversion = null;
        _simulation = null;
        _projectedParticles.Clear(); _projectedParticles.Capacity = 0;
        _renderedParticles.Clear(); _renderedParticles.Capacity = 0;
        _opaqueParticles.Clear(); _opaqueParticles.Capacity = 0;
        _submissionParticles = [];
    }
}
