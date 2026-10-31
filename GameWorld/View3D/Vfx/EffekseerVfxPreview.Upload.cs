using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Vfx;

public sealed partial class EffekseerVfxPreview
{
    private Exception? _convertedUploadFailure;

    private void ReloadConverted(VfxPreviewSnapshot snapshot, VfxConversion conversion, string? resourceIdentity,
        VfxPreparedResources? preparedResources = null, bool allowSynchronousPreparation = true)
    {
        using var upload = BeginConvertedUpload(snapshot, conversion, resourceIdentity, preparedResources, allowSynchronousPreparation);
        while (ContinueConvertedUpload(upload, snapshot.SourcePath)) { }
    }

    private bool ContinueConvertedUpload(IEnumerator<int> upload, string sourcePath)
    {
        try { return upload.MoveNext(); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            _convertedUploadFailure = exception;
            Logging.Create<EffekseerVfxPreview>().Error(exception, "Effekseer preview load failed for {Path}", sourcePath);
            Diagnostics = [.. Diagnostics, new(sourcePath, "", "", "Vfx.Preview.RuntimeUnavailable")];
            return false;
        }
    }

    private IEnumerator<int> BeginConvertedUpload(VfxPreviewSnapshot snapshot, VfxConversion conversion, string? resourceIdentity,
        VfxPreparedResources? preparedResources = null, bool allowSynchronousPreparation = true)
    {
        _convertedUploadFailure = null;
        CancelPreparation();
        var previous = _resources;
        EffekseerHandle? retainedHandle = null;
        if (resourceIdentity != null && previous?.ResourceIdentity == resourceIdentity
            && previous.TryTakeExclusiveHandle(out retainedHandle))
        {
            // The old owner was consumed, so ReleaseResources must not release it a second time.
            _resources = null;
            _handle = null;
        }
        if (retainedHandle == null && conversion.Emitters.Count > 0 && preparedResources == null && !allowSynchronousPreparation)
            throw new VfxResourcesPreparationRequiredException();
        ReleaseResources();
        _renderLayers = [];
        _opaqueLayers.Clear();
        _spriteSpinRangeDiagnostic = null;
        _sourcePath = snapshot.SourcePath;
        PreviewBounds = null;
        _conversion = conversion;
        UpdateIdentificationMask();
        _simulation = CreateSimulation(_conversion);
        Diagnostics = _conversion.Diagnostics;
        if (_conversion.Emitters.Count == 0)
        {
            if (!Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.MissingResource"))
                _resources = new(null, _conversion, _renderLayers, [], null, Diagnostics, _sourcePath);
            return Enumerable.Empty<int>().GetEnumerator();
        }
        if (retainedHandle != null)
        {
            try
            {
                Diagnostics = [.. Diagnostics, .. previous!.Diagnostics.Where(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.MipmapsUnsupported")
                    .Select(diagnostic => diagnostic with { Source = snapshot.SourcePath })];
                CompleteReload(retainedHandle, previous.ConstantAlphas, resourceIdentity, reused: true);
                return Enumerable.Empty<int>().GetEnumerator();
            }
            catch (InvalidOperationException exception)
            {
                Logging.Create<EffekseerVfxPreview>().Warning(exception, "VFX resource reuse failed; rebuilding {Path}", snapshot.SourcePath);
                CancelPreparation();
                Diagnostics = _conversion.Diagnostics;
                _simulation = CreateSimulation(_conversion);
            }
            catch { CancelPreparation(); retainedHandle.Dispose(); throw; }
            retainedHandle.Dispose();
        }
        if (preparedResources == null && !allowSynchronousPreparation)
            throw new VfxResourcesPreparationRequiredException();
        preparedResources ??= VfxPreparedResources.Prepare(conversion);
        return UploadConvertedSteps(snapshot, preparedResources, resourceIdentity).GetEnumerator();
    }

    // A private candidate keeps the active complete playback available between upload turns.
    private IEnumerable<int> UploadConvertedSteps(VfxPreviewSnapshot snapshot, VfxPreparedResources preparedResources,
        string? resourceIdentity)
    {
        var conversion = _conversion ?? throw new InvalidOperationException("A VFX upload requires a prepared conversion.");
        EffekseerHandle? handle = null;
        var complete = false;
        try
        {
            if (EffekseerNative.AecnVfxAbi(System.Runtime.InteropServices.Marshal.SizeOf<EffekseerLayer>(),
                System.Runtime.InteropServices.Marshal.SizeOf<EffekseerParticle>(), System.Runtime.InteropServices.Marshal.SizeOf<EffekseerVertex>(),
                System.Runtime.InteropServices.Marshal.SizeOf<EffekseerLighting>()) != 1)
                throw new InvalidOperationException("Effekseer ABI mismatch");
            var nativeDevice = (SharpDX.Direct3D11.Device)device.Handle;
            handle = EffekseerNative.AecnVfxCreate(nativeDevice.NativePointer, nativeDevice.ImmediateContext.NativePointer);
            EffekseerNative.Check(handle.IsInvalid ? 0 : 1);
            yield return 0;
            var failedTextures = new HashSet<int>();
            var uploadedTextures = new Dictionary<(VfxTextureUsage Usage, string Hash), (int Index, bool Mipmaps, byte? Alpha)>();
            var constantAlphas = new byte?[conversion.Textures.Count];
            var identities = preparedResources.Textures.Select(texture => texture.Identity).ToArray();
            for (var index = 0; index < conversion.Textures.Count; ++index)
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
                        var image = preparedResources.Textures[index].GetImage();
                        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, image.Pixels, image.Pixels.Length, image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
                        mipmaps = image.MipLevels > 1;
                        constantAlphas[index] = image.ConstantAlpha;
                        uploadedTextures.Add(identity, (index, mipmaps, constantAlphas[index]));
                    }
                    if (mipmaps)
                        Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, "", conversion.TexturePaths[index], "Vfx.Preview.MipmapsUnsupported")];
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    resourceIdentity = null;
                    if (conversion.TextureUsages[index] == VfxTextureUsage.ShadingNormal)
                    {
                        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [0, 128, 0, 128], 4, 1, 1, 0));
                        foreach (var emitter in conversion.Emitters.Where(x => x.Render.NormalIndex == index))
                        {
                            var fallback = emitter.Render; fallback.LightingMode = 0; emitter.Render = fallback;
                            Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, emitter.Name, conversion.TexturePaths[index], "Vfx.Preview.ShadingNormalUnsupported")];
                        }
                    }
                    else
                    {
                        Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, "", conversion.TexturePaths[index], "Vfx.Preview.TextureUnsupported")];
                        // Preserve resource indexes while excluding every emitter that needs this texture.
                        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [0, 0, 0, 0], 4, 1, 1, 0));
                        failedTextures.Add(index);
                    }
                }
                yield return 1;
            }
            if (failedTextures.Count > 0)
            {
                bool UsesFailedTexture(VfxPreviewEmitter emitter) => Enumerable.Range(0, emitter.ModelVariants.Count + 1)
                    .SelectMany(emitter.RenderParts).Any(part => failedTextures.Contains(part.TextureIndex) || failedTextures.Contains(part.GradientIndex)
                        || failedTextures.Contains(part.DistortionMaskIndex)
                        || part.MeshMaterialIndex >= 0 && conversion.MeshMaterials[part.MeshMaterialIndex].TextureIndexes.Any(failedTextures.Contains));
                foreach (var emitter in conversion.Emitters.Where(UsesFailedTexture))
                    Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, emitter.Name, "texture", "Vfx.Preview.LayerTextureUnavailable")];
                conversion.Emitters.RemoveAll(UsesFailedTexture);
                _simulation = CreateSimulation(conversion);
                if (conversion.Emitters.Count == 0) yield break;
            }
            var packedMasks = new Dictionary<((VfxTextureUsage, string), (VfxTextureUsage, string)), (int Index, float[] Rectangles)>();
            var packedRigidMasks = new Dictionary<((VfxTextureUsage, string), (VfxTextureUsage, string), (VfxTextureUsage, string)), (int Index, float[] Rectangles)>();
            var nextPackedIndex = conversion.Textures.Count;
            var failedMaterials = new HashSet<int>();
            for (var index = 0; index < conversion.MeshMaterials.Count; ++index)
            {
                var material = conversion.MeshMaterials[index];
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
                                if (!uploadedTextures.ContainsKey(key.Item1) || !uploadedTextures.ContainsKey(key.Item2))
                                    throw new KeyNotFoundException("A decoded VFX material mask is missing.");
                                var atlas = preparedResources.Masks[index]!.GetAtlas();
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
                                if (!uploadedTextures.ContainsKey(key.Item1) || !uploadedTextures.ContainsKey(key.Item2)
                                    || !uploadedTextures.ContainsKey(key.Item3))
                                    throw new KeyNotFoundException("A decoded VFX material mask is missing.");
                                var preparedMask = preparedResources.Masks[index]!;
                                var atlas = preparedMask.GetAtlas();
                                var srgb = preparedMask.Srgb;
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
                yield return 2;
            }
            if (failedMaterials.Count > 0)
            {
                resourceIdentity = null;
                bool UsesFailedMaterial(VfxPreviewEmitter emitter) => Enumerable.Range(0, emitter.ModelVariants.Count + 1)
                    .SelectMany(emitter.RenderParts).Any(part => failedMaterials.Contains(part.MeshMaterialIndex));
                foreach (var emitter in conversion.Emitters.Where(UsesFailedMaterial))
                    Diagnostics = [.. Diagnostics, new(snapshot.SourcePath, emitter.Name, "material masks", "Vfx.Preview.TextureUnsupported")];
                conversion.Emitters.RemoveAll(UsesFailedMaterial);
                _simulation = CreateSimulation(conversion);
                if (conversion.Emitters.Count == 0) yield break;
            }
            var uploadedModels = new Dictionary<VfxPreviewModel, int>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < conversion.Models.Count; ++index)
            {
                var model = conversion.Models[index];
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
                yield return 3;
                if (_applyingInteractiveReload) EffekseerNative.Check(EffekseerNative.AecnVfxPrepareModel(handle, index));
                yield return 4;
            }
            CompleteReload(handle, constantAlphas, resourceIdentity);
            complete = true;
            yield return 5;
        }
        finally
        {
            if (!complete) { CancelPreparation(); handle?.Dispose(); }
        }
    }
}
