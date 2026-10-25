#nullable enable
using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxGrowingVinesNativeTests
{
    [Test]
    public void NativeTrunk_ClipsAtTheParticleAlphaBoundaryWithoutRejectingTheWholeEffect()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var empty = Render(0);
            var half = Render(.3f);
            var full = Render(.6f);
            Assert.Multiple(() =>
            {
                Assert.That(empty.Count(IsGreen), Is.Zero);
                Assert.That(full.Count(IsGreen), Is.GreaterThan(100));
                // The fixture is a right triangle; clipping at half its height leaves one quarter of its area.
                Assert.That(half.Count(IsGreen), Is.InRange(full.Count(IsGreen) * .23, full.Count(IsGreen) * .27));
                Assert.That(half.Where(IsGreen).Select(c => c.G).Distinct(), Is.EqualTo(full.Where(IsGreen).Select(c => c.G).Distinct()),
                    "Surviving geometry must keep its colour rather than fade as a transparent rectangle.");
            });

            Color[] Render(float alpha)
            {
                var material = VfxGrowingVinesMaterialTests.Material();
                VfxGemstoneMaterialTests.Set(material, "from_trunk_to_leaves", "0");
                VfxGemstoneMaterialTests.Set(material, "base_colour_tint", "0,1,0");
                VfxGemstoneMaterialTests.Set(material, "emissiveness", "0");
                var source = VfxOpaqueModelMaterialTests.Source(Vector3.One, alpha);
                byte[]? Resolve(string path) => path.Replace('\\', '/') switch
                {
                    "vfx/character.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
                    _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
                };
                var device = graphics.GraphicsDevice;
                using var preview = new EffekseerVfxPreview(device);
                preview.Reload(new(source, "vfx/growing.xml", Resolve));
                Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False,
                    "The native bridge must accept the growing material's opaque depth contract.");
                preview.Seek(.2f, Matrix.Identity);
                var position = new Vector3(.5f, .5f, 10);
                var camera = new CommonShaderParameters(Matrix.CreateLookAt(position, new(.5f, .5f, 0), Vector3.Up),
                    Matrix.CreateOrthographic(2, 2, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
                device.SetRenderTarget(target);
                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(20, 23, 28), 1, 0);
                preview.Draw(camera);
                device.SetRenderTarget(null);
                var pixels = new Color[128 * 128]; target.GetData(pixels); return pixels;
            }
        }
        finally { graphics.Release(); }
    }

    [TestCase(.000033900989f, 0, 0)]
    [TestCase(.0008f, 1, 0)]
    [TestCase(.101f, 103, 128)]
    [TestCase(.5f, 512, 255)]
    public void GrowingEmission_UsesNativeTenBitStorageAndDiffuseMetalness(float emission, int storedEmission, byte metalness)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var unlit = RenderEmission(graphics.GraphicsDevice, 0, metalness, .2f);
            var lit = RenderEmission(graphics.GraphicsDevice, emission, metalness, .2f);
            // Captured GrowingVines G-buffer uses R10G10B10A2; deferred lighting scales its
            // emission by 255 and by the non-metallic diffuse colour (Terry draw 643 -> 949).
            var nativeGain = storedEmission / 1023f * 255 * (1 - metalness / 255f);
            foreach (var index in Enumerable.Range(0, unlit.Length).Where(i => unlit[i].X > .01f))
            {
                Assert.That(lit[index].X - unlit[index].X, Is.EqualTo(unlit[index].X * nativeGain).Within(.00005f));
                Assert.That(lit[index].Y - unlit[index].Y, Is.EqualTo(unlit[index].Y * nativeGain).Within(.00005f));
                Assert.That(lit[index].Z - unlit[index].Z, Is.EqualTo(unlit[index].Z * nativeGain).Within(.00005f));
            }
            Assert.That(unlit.Any(pixel => pixel.X > .01f), Is.True, "The controlled model must actually be drawn.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void GrowingEmission_UsesMaterialTimeForTheAuthoredPanningMask()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var first = RenderEmission(graphics.GraphicsDevice, .1f, 0, .2f, true);
            var moved = RenderEmission(graphics.GraphicsDevice, .1f, 0, .7f, true);
            var loop = RenderEmission(graphics.GraphicsDevice, .1f, 0, 2.2f, true);
            var pixels = Enumerable.Range(0, first.Length).Where(i => first[i].X > .01f).ToArray();
            Assert.That(pixels.Max(i => Math.Abs(first[i].X - moved[i].X)), Is.GreaterThan(.1f));
            Assert.That(pixels.Max(i => Math.Abs(first[i].X - loop[i].X)), Is.LessThan(.001f),
                "A full authored UV period must return to the same light pattern independently of particle respawn.");
        }
        finally { graphics.Release(); }
    }

    private static Vector4[] RenderEmission(GraphicsDevice device, float emission, byte metalness, float time, bool movingMask = false)
    {
        var material = VfxGrowingVinesMaterialTests.Material();
        VfxGemstoneMaterialTests.Set(material, "from_trunk_to_leaves", "0");
        VfxGemstoneMaterialTests.Set(material, "base_colour_tint", "1,1,1");
        VfxGemstoneMaterialTests.Set(material, "emissiveness", emission.ToString(System.Globalization.CultureInfo.InvariantCulture));
        VfxGemstoneMaterialTests.Set(material, "fresnel_power", "0");
        VfxGemstoneMaterialTests.Set(material, "emissive_panning_mask_direction", ".5,0");
        var mask = VfxModelVariantPixelTests.Dds(Color.White);
        if (movingMask)
            for (var index = 128; index < mask.Length; index += 8) mask[index] = mask[index + 1] = mask[index + 2] = 0;
        byte[]? Resolve(string path) => path.Replace('\\', '/') switch
        {
            "vfx/character.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
            "diffuse.dds" => VfxModelVariantPixelTests.Dds(new Color(32, 64, 96, 255)),
            "material_map.dds" => VfxModelVariantPixelTests.Dds(new Color(metalness, (byte)0, (byte)0, (byte)255)),
            "normal.dds" => VfxModelVariantPixelTests.Dds(new Color(0, 128, 0, 128)),
            "emissive_panning_mask_r.dds" => mask,
            _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
        };
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/growing.xml", Resolve));
        preview.Seek(time, Matrix.Identity);
        var eye = new Vector3(.5f, .5f, 10);
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, new(.5f, .5f, 0), Vector3.Up),
            Matrix.CreateOrthographic(2, 2, .1f, 100), eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
        preview.Draw(camera); device.SetRenderTarget(null);
        var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
    }

    private static bool IsGreen(Color colour) => colour.G > 100 && colour.R < 15 && colour.B < 15;
}
