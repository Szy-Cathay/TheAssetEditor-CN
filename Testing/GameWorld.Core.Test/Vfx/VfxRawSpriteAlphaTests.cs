using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxRawSpriteAlphaTests
{
    [TestCase(3, 1)]
    [TestCase(1, 3)]
    [TestCase(.5f, 1)]
    public void PremultipliedSprite_PreservesAuthoredAlphaUntilTextureCoverage(float initial, float curve)
    {
        var source = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="alpha" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="MaximumParticles" value="1"/>
            <property name="ParticleLifeTime">{Constant("x", 5)}</property>
            <property name="ParticleInitialAlpha">{Constant("x", initial)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="alpha"><quality value="0">
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/alpha.tga"/>
            <property name="pre_multiplied_alpha" value="true"/></modifier>
            <modifier fragment="vs_vertex_alpha"><property name="curve_alpha">{Constant("alpha", curve)}</property></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 128, 128, 128, 128];
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/alpha.xml", _ => texture));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        Assert.That(simulation.Frame(camera).Single().Colour.W, Is.EqualTo(initial * curve).Within(.0001),
            "The shipped process_texture_colour saturates texture alpha times particle alpha, rather than particle alpha first.");
    }

    [TestCase(1)]
    [TestCase(3)]
    public void NativeCoverage_SharpensOpacityWithoutMultiplyingPremultipliedRgbAgain(float alpha)
    {
        var pixels = Render([alpha]);
        AssertColour(pixels[32 * 64 + 32], alpha);
    }

    [Test]
    public void NativeCoverage_KeepsDifferentRawAlphaValuesInTheSameDrawBatch()
    {
        var pixels = Render([2, 3]);
        AssertColour(pixels[32 * 64 + 16], 2);
        AssertColour(pixels[32 * 64 + 48], 3);
    }

    [Test]
    public void NativeCoverage_PreservesFaintParticlesBesideVeryLargeAlphaInTheSameLayer()
    {
        var pixels = Render([.25f, 1000]);
        AssertColour(pixels[32 * 64 + 16], .25f);
        AssertColour(pixels[32 * 64 + 48], 1000);
    }

    private static void AssertColour(Vector4 actual, float rawAlpha)
    {
        var linear = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
        var expected = new Vector3(linear * Math.Min(rawAlpha, 1))
            + new Vector3(.2f, .4f, .6f) * (1 - Math.Clamp(linear * rawAlpha, 0, 1));
        Assert.That(Vector3.Distance(new(actual.X, actual.Y, actual.Z), expected), Is.LessThan(.008f),
            "Preserve min(texture RGB, texture alpha) times saturated vertex alpha while sharpening the coverage mask.");
    }

    private static Vector4[] Render(float[] alphas)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Requires opt-in DX11 pixels.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [128, 128, 128, 128], 4, 1, 1, 1));
            var layer = EffekseerLayer.Create(); layer.MaterialMode = 1; layer.Blend = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var particles = alphas.Select((alpha, index) => new EffekseerParticle
            {
                Transform = Matrix.CreateScale(.75f) * Matrix.CreateTranslation(alphas.Length == 1 ? 0 : index * 2 - 1, 0, 0),
                Colour = new(1, 1, 1, alpha), UV = new(0, 0, 1, 1),
            }).ToArray();
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(new Color(.2f, .4f, .6f)); device.DepthStencilState = DepthStencilState.Default;
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,
                Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)), Values(Matrix.CreateOrthographic(4, 4, .1f, 50)), particles, particles.Length));
            device.SetRenderTarget(null);
            var pixels = new Vector4[4096]; target.GetData(pixels); return pixels;
        }
        finally { graphics.Release(); }
    }

    private static string Constant(string axis, float value) => $"{axis},0,1;0,{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:{axis},0;0,0";
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
