#nullable enable
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable, Apartment(System.Threading.ApartmentState.STA)]
public class VfxModelDisplacementBindingPixelTests
{
    private const int Size = 96;
    private static readonly Vector3 Normal = new(.6f, 0, .8f);

    [SetUp]
    public void RequireExplicitOffscreenOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering is opt-in; no editor window or desktop input.");
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DedicatedVsTextureAndRepeatSampler_MatchIndependentlyBakedNativeGeometry(bool tidecall, bool wrappedUv)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var uv = new Vector2(wrappedUv ? 1.625f : .625f, .625f);
            // Two mip-zero blue texels, with bilinear repeat; the base colour's blue is deliberately 61.
            var x = (double)uv.X * 2 - .5; var left = (int)Math.Floor(x); var fraction = x - left;
            var blue = ((left % 2 == 0 ? 31 : 197) * (1 - fraction) + ((left + 1) % 2 == 0 ? 31 : 197) * fraction) / 255;
            var amplitude = (float)(blue * .8 * (1 - uv.Y));
            var offset = tidecall ? Normal * amplitude : new Vector3(amplitude * Normal.X);
            var actual = Render(graphics.GraphicsDevice, tidecall, uv, true, Vector3.Zero);
            var baked = Render(graphics.GraphicsDevice, tidecall, uv, false, offset);
            var still = Render(graphics.GraphicsDevice, tidecall, uv, false, Vector3.Zero);
            Assert.That(actual.Zip(baked).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThanOrEqualTo(.000035f));
            Assert.That(actual.Zip(still).Count(p => Vector4.Distance(p.First, p.Second) > .001f), Is.GreaterThan(20),
                "The renderer must retain the authored displacement texture and repeat sampler after binding its PS material.");
        }
        finally { graphics.Release(); }
    }

    private static Vector4[] Render(GraphicsDevice device, bool tidecall, Vector2 uv, bool displace, Vector3 bakedOffset)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        var count = tidecall ? 12 : 6;
        for (var i = 0; i < count; ++i)
        {
            byte[] bytes = i == count - 1 ? new byte[] { 0,0,31,255,0,0,197,255 } : tidecall ? [0,0,61,255] : [255,0,61,255];
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, bytes, bytes.Length, bytes.Length / 4, 1, 0));
        }
        var parameters = new float[tidecall ? 104 : 64];
        if (tidecall)
        {
            parameters[19] = displace ? .8f : 0;
            parameters[20] = parameters[21] = 1;
            parameters[45] = 1;
            // All PS logical slots refer to a zero-red texture, so dither keeps opaque black geometry.
        }
        else
        {
            parameters[0] = parameters[4] = parameters[8] = 1;
            parameters[15] = parameters[16] = parameters[18] = 1;
            parameters[21] = parameters[22] = 1;
            parameters[23] = parameters[24] = parameters[25] = parameters[26] = 1;
            parameters[27] = 1;
            parameters[29] = parameters[30] = parameters[31] = parameters[32] = 1;
            for (var i = 39; i <= 44; ++i) parameters[i] = 1;
            parameters[46] = 1;
            parameters[50] = .8f;
            parameters[53] = parameters[54] = 1;
            parameters[55] = displace ? 1 : 0;
            parameters[63] = 1;
        }
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, Enumerable.Range(0, count).ToArray(), count, parameters, parameters.Length));
        var vertices = new[] { new Vector3(-.45f,-.45f,0), new(.45f,-.45f,0), new(-.45f,.45f,0), new(.45f,.45f,0) }
            .Select(p => new EffekseerVertex { Position = p + bakedOffset, Normal = Normal, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = uv, Colour = Vector4.One }).ToArray();
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0,1,2,2,1,3], 6));
        var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0; layer.TextureIndex = 0;
        layer.OpaqueModel = tidecall ? 1 : 0;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
        using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(50,80,120), 1, 0);
        var view = Matrix.CreateLookAt(new(0,0,10), Vector3.Zero, Vector3.Up); var projection = Matrix.CreateOrthographic(4,4,.1f,100);
        var particle = new EffekseerParticle { Transform = Matrix.Identity, Colour = Vector4.One, UV = new(0,0,1,1), ParticleSize = 1 };
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1));
        device.SetRenderTarget(null); var result = new Vector4[Size * Size]; target.GetData(result); return result;
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
