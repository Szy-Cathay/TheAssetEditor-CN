using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameWorld.Core.Vfx;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct EffekseerLayer
{
    internal const int PreciseCoverageMode = 5, PreciseEmissionMode = 6;
    public int Kind, Blend, ZTest, TextureIndex, NormalIndex, ModelIndex;
    public float SoftDistance, Distortion;
    public Microsoft.Xna.Framework.Vector2 Pivot;
    public int AtlasColumns, AtlasRows, AtlasLoop, AtlasBlend;
    public int MaterialMode, UvRotation;
    public int LightingMode;
    public float Emissive, SmallParticles, LargeParticles, Directionality, AverageSize;
    public int MeshMaterialIndex;
    public int GradientIndex, GradientAlphaMapping;
    public float GradientScale;
    public int CameraFacing, OpaqueModel;
    public float EmissionShapeWeight;
    public int DistortionMaskIndex;
    public float DistortionSoftScaleStrength;
    public int DistortionSoftDepth;
    public int NativeSoftDepth;
    public static EffekseerLayer Create() => new()
    {
        ZTest = 1, Blend = 1, ModelIndex = -1, NormalIndex = -1, MeshMaterialIndex = -1, GradientIndex = -1, DistortionMaskIndex = -1,
        GradientScale = 1, DistortionSoftScaleStrength = 1, Pivot = new(0.5f),
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct EffekseerParticle
{
    public int Layer;
    public Microsoft.Xna.Framework.Matrix Transform;
    public Microsoft.Xna.Framework.Vector4 Colour, UV;
    public float Time, Flipbook;
    // Lit coverage uses a quaternion; velocity distortion carries T.xy/B.xy or N.xyz/1.
    public Microsoft.Xna.Framework.Vector4 NormalRotation;
    public float ParticleSize;
    public float MaterialTime;
    public float AppearancePhase;
    public Microsoft.Xna.Framework.Vector2 AlphaCrush;
    public float SoftParticleScale = 1;
    public EffekseerParticle() { }
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct EffekseerLighting
{
    public Microsoft.Xna.Framework.Vector4 PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ;
    public Microsoft.Xna.Framework.Vector4 SunDirection, SunColour, Parameters;
    // Display.w stores uniform contrast minus one, preserving the default identity.
    public Microsoft.Xna.Framework.Vector4 Display;
    public Microsoft.Xna.Framework.Vector4 Bloom;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct EffekseerVertex
{
    public Microsoft.Xna.Framework.Vector3 Position, Normal, Tangent, Binormal;
    public Microsoft.Xna.Framework.Vector2 UV;
    public Microsoft.Xna.Framework.Vector4 Colour = Microsoft.Xna.Framework.Vector4.One;
    public EffekseerVertex() { }
}

internal sealed class EffekseerHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public EffekseerHandle() : base(true) { }
    protected override bool ReleaseHandle() { EffekseerNative.AecnVfxDestroy(handle); return true; }
}

internal static class EffekseerNative
{
    private const string Library = "AssetEditor.CN.Effekseer.dll";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxAbi(int layerBytes, int particleBytes, int vertexBytes, int lightingBytes);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern nint AecnVfxLastError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern EffekseerHandle AecnVfxCreate(nint device, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void AecnVfxDestroy(nint preview);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxDepthAvailable(EffekseerHandle preview);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxDrawCalls(EffekseerHandle preview);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxTextureRgba(EffekseerHandle preview, byte[] pixels, int length, int width, int height, int srgb);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxTextureRgbaMipChain(EffekseerHandle preview, byte[] pixels, int length, int width, int height, int srgb, int mipLevels);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxTextureAlias(EffekseerHandle preview, int sourceIndex);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxModel(EffekseerHandle preview, EffekseerVertex[] vertices, int vertexCount, int[] indices, int indexCount);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxModelFrames(EffekseerHandle preview, EffekseerVertex[] vertices, int vertexLength, int verticesPerFrame, int frameCount, int[] indices, int indexCount, float cycles);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxModelAlias(EffekseerHandle preview, int sourceIndex);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxMeshMaterial(EffekseerHandle preview, int[] textures, int textureCount, float[] parameters, int parameterCount);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxConfigure(EffekseerHandle preview, [In] EffekseerLayer[] layers, int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int AecnVfxDraw(EffekseerHandle preview, float[] view, float[] projection, [In] EffekseerParticle[] particles, int count, in EffekseerLighting lighting = default, nint softDepth = 0);

    internal static void Check(int result)
    {
        if (result == 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(AecnVfxLastError()));
    }
}
