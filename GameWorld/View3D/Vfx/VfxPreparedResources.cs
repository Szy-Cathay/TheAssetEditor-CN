using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace GameWorld.Core.Vfx;

internal sealed class VfxResourcesPreparationRequiredException : Exception { }

internal sealed record VfxDecodedTexture(byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels, byte? ConstantAlpha);

internal sealed record VfxPreparedTexture((VfxTextureUsage Usage, string Hash) Identity, VfxDecodedTexture? Image, Exception? Failure)
{
    internal VfxDecodedTexture GetImage()
    {
        if (Failure != null) ExceptionDispatchInfo.Capture(Failure).Throw();
        return Image!;
    }
}

internal sealed record VfxPreparedMaskAtlas(VfxMaterialMaskAtlas? Atlas, bool Srgb, Exception? Failure)
{
    internal VfxMaterialMaskAtlas GetAtlas()
    {
        if (Failure != null) ExceptionDispatchInfo.Capture(Failure).Throw();
        return Atlas!;
    }
}

// Payload slots and failures stay aligned with the original conversion. GPU fallback remains with its owner.
internal sealed partial class VfxPreparedResources(VfxPreparedTexture[] textures, VfxPreparedMaskAtlas?[] masks)
{
    internal VfxPreparedTexture[] Textures { get; } = textures;
    internal VfxPreparedMaskAtlas?[] Masks { get; } = masks;
    internal int PreparationThreadId { get; } = Environment.CurrentManagedThreadId;
    internal long TexturePixelBytes => Textures.Select(texture => texture.Image).OfType<VfxDecodedTexture>()
        .Distinct<VfxDecodedTexture>(ReferenceEqualityComparer.Instance).Sum(image => image.Pixels.LongLength);
    internal long MaskAtlasPixelBytes => Masks.OfType<VfxPreparedMaskAtlas>().Select(mask => mask.Atlas)
        .OfType<VfxMaterialMaskAtlas>().Distinct<VfxMaterialMaskAtlas>(ReferenceEqualityComparer.Instance).Sum(atlas => atlas.Pixels.LongLength);

    internal static VfxPreparedResources Prepare(VfxConversion conversion, CancellationToken cancellationToken = default,
        Func<byte[], VfxTextureUsage, (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels)>? decode = null)
    {
        decode ??= DecodeTexture;
        var textures = new VfxPreparedTexture[conversion.Textures.Count];
        var decoded = new Dictionary<(VfxTextureUsage Usage, string Hash), VfxPreparedTexture>();
        for (var index = 0; index < textures.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = conversion.Textures[index];
            var key = (conversion.TextureUsages[index], Convert.ToHexStringLower(SHA256.HashData(bytes)));
            if (!decoded.TryGetValue(key, out var prepared))
            {
                try
                {
                    var image = decode(bytes, key.Item1);
                    cancellationToken.ThrowIfCancellationRequested();
                    prepared = new(key, new(image.Pixels, image.Width, image.Height, image.Srgb, image.MipLevels,
                        ConstantAlpha(image.Pixels)), null);
                }
                catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
                {
                    prepared = new(key, null, exception);
                }
                decoded.Add(key, prepared);
            }
            textures[index] = prepared;
        }

        var maskImages = new Dictionary<(VfxTextureUsage Usage, string Hash), (byte[] Pixels, int Width, int Height)>();
        (byte[] Pixels, int Width, int Height) Mask(int index)
        {
            var texture = textures[index];
            if (maskImages.TryGetValue(texture.Identity, out var cached)) return cached;
            // The original upload path only retained masks belonging to successfully decoded textures.
            if (texture.Failure != null) throw new KeyNotFoundException("A decoded VFX material mask is missing.");
            var image = texture.Image!;
            var result = (image.Pixels[..checked(image.Width * image.Height * 4)], image.Width, image.Height);
            maskImages.Add(texture.Identity, result);
            return result;
        }
        var masks = new VfxPreparedMaskAtlas?[conversion.MeshMaterials.Count];
        var pairs = new Dictionary<((VfxTextureUsage, string), (VfxTextureUsage, string)), VfxPreparedMaskAtlas>();
        var triples = new Dictionary<((VfxTextureUsage, string), (VfxTextureUsage, string), (VfxTextureUsage, string)), VfxPreparedMaskAtlas>();
        for (var index = 0; index < masks.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var material = conversion.MeshMaterials[index];
            var slots = material.TextureIndexes;
            if (slots.Length == 7 && material.Parameters.Length == 36)
            {
                var key = (textures[slots[3]].Identity, textures[slots[4]].Identity);
                if (!pairs.TryGetValue(key, out var prepared))
                {
                    try
                    {
                        var first = Mask(slots[3]); var second = Mask(slots[4]);
                        prepared = new(VfxMaterialMaskAtlas.Pack(first.Pixels, first.Width, first.Height,
                            second.Pixels, second.Width, second.Height), false, null);
                    }
                    catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
                    {
                        prepared = new(null, false, exception);
                    }
                    pairs.Add(key, prepared);
                }
                masks[index] = prepared;
            }
            else if (slots.Length == 10 && material.Parameters.Length == 60)
            {
                var key = (textures[slots[1]].Identity, textures[slots[2]].Identity, textures[slots[4]].Identity);
                if (!triples.TryGetValue(key, out var prepared))
                {
                    try
                    {
                        var first = Mask(slots[1]); var second = Mask(slots[2]); var third = Mask(slots[4]);
                        var srgb = textures[slots[1]].Image!.Srgb;
                        if (textures[slots[2]].Image!.Srgb != srgb || textures[slots[4]].Image!.Srgb != srgb)
                            throw new NotSupportedException("VFX packed masks require matching sampling colour spaces");
                        prepared = new(VfxMaterialMaskAtlas.PackThree(first, second, third), srgb, null);
                    }
                    catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
                    {
                        prepared = new(null, false, exception);
                    }
                    triples.Add(key, prepared);
                }
                masks[index] = prepared;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(textures, masks);
    }
}
