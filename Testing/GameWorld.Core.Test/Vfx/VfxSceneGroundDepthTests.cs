using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSceneGroundDepthTests
{
    private const int Size = 128;

    [TestCase(false, -.9f, true)]
    [TestCase(true, -.9f, true)]
    [TestCase(false, .9f, true)]
    [TestCase(false, -.9f, false)]
    public void ExplicitSceneReceiver_OccludesBeforeAnyInstanceWhileKeepingOptOut(
        bool terrainFirst, float ordinaryHeight, bool writeGroundDepth)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var ordinary = Preview(device, false, Color.Red);
            using var terrain = Preview(device, true, Color.Blue);
            ordinary.Seek(.2f, Matrix.CreateTranslation(0, ordinaryHeight, 0));
            terrain.Seek(.2f, Matrix.CreateTranslation(2, 0, 0));
            var eye = new Vector3(0, 8, -8);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(6, 6, .1f, 100) * Matrix.CreateScale(-1, 1, 1),
                eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

            // DIAG: fixed opaque colours isolate depth; this is not an original VFX visual comparison.
            var withoutReceiver = Render(device, [ordinary], camera);
            Assert.That(withoutReceiver.Count(pixel => pixel.R > 100), Is.GreaterThan(100),
                "The same ordinary particle must be visible before a scene receiver is requested.");
            var ordered = terrainFirst ? new[] { terrain, ordinary } : new[] { ordinary, terrain };
            var pixels = Render(device, ordered, camera with { VfxWriteGroundDepth = writeGroundDepth });
            var visibleOrdinary = pixels.Count(pixel => pixel.R > 100);
            if (ordinaryHeight < 0 && writeGroundDepth)
                Assert.That(visibleOrdinary, Is.Zero,
                    "A supplied scene floor must already occlude the underground particle when either instance draws first.");
            else
                Assert.That(visibleOrdinary, Is.GreaterThan(100),
                    "An above-ground particle stays visible; opting out preserves the earlier no-prewrite behaviour.");
            Assert.That(ordinary.ParticleCount, Is.EqualTo(1));
            Assert.That(terrain.ParticleCount, Is.EqualTo(1));
        }
        finally { graphics.Release(); }
    }

    private static EffekseerVfxPreview Preview(GraphicsDevice device, bool terrain, Color colour)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        if (terrain)
            document.Root.Descendants("emitter").Single().Add(
                Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL"),
                Property("ParticleRelativeSpawnDirection", "RELATIVE_TO_TERRAIN_NORMAL"),
                Property("ParticlesSpawnOnTerrain", "true"));
        var bytes = document.Write();
        var texture = VfxModelVariantPixelTests.Dds(colour);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/scene-ground-diagnostic.xml", _ => texture));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Kind, Is.Zero);
        Assert.That(conversion.Emitters[0].RequiresFlatTerrain, Is.EqualTo(terrain));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(bytes, "vfx/scene-ground-diagnostic.xml", _ => texture));
        Assert.That(preview.EmitterCount, Is.EqualTo(1));
        return preview;
    }

    private static Color[] Render(GraphicsDevice device, IReadOnlyList<EffekseerVfxPreview> previews,
        CommonShaderParameters camera)
    {
        using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        try
        {
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            device.DepthStencilState = DepthStencilState.Default;
            foreach (var preview in previews) preview.Draw(camera);
        }
        finally { device.SetRenderTarget(null); }
        var pixels = new Color[Size * Size];
        target.GetData(pixels);
        return pixels;
    }

    private static XElement Property(string name, string value) => new("property",
        new XAttribute("name", name), new XAttribute("value", value));
}
