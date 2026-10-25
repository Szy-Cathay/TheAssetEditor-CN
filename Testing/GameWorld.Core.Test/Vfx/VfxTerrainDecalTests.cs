using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxTerrainDecalTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void StaticTerrainDecal_UsesTheExplicitFlatReceiverWithoutEditingSource(bool terrainSpawn)
    {
        var bytes = Source(terrainSpawn);
        var conversion = Convert(bytes); Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var frame = simulation.Frame(Camera(0));
        Assert.That(frame, Has.Length.EqualTo(1));
        Assert.That(frame[0].Transform.Translation, Is.EqualTo(Vector3.Zero));
        Assert.That(Math.Abs(frame[0].Transform.Backward.Y), Is.EqualTo(1).Within(.00001));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(false, 0, 1, 0)]
    [TestCase(true, 30, 1, 0)]
    [TestCase(true, 0, 2, 0)]
    [TestCase(true, 0, 1, 2)]
    public void UncalibratedReceiverOrRoot_DoesNotInventATerrainBasis(bool ground, float yaw, float scale, float height)
    {
        var conversion = Convert(Source(true)); Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.CreateScale(scale) * Matrix.CreateRotationY(MathHelper.ToRadians(yaw)) * Matrix.CreateTranslation(0, height, 0));
        Assert.That(simulation.Frame(Camera(ground ? 0 : null)), Is.Empty);
        simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera(0)), Has.Length.EqualTo(1), "A corrected root must recover without rewriting the document.");
    }

    [TestCase("DEFERRED_DECAL_EMISSIVE", 1)]
    public void MovingTerrainDecals_RemainDiagnosed(string pass, float speed)
    {
        var conversion = Convert(Source(true, pass, speed));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.OrientationUnsupported"), Is.True);
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void TerrainDecal_ProducesPixelsOnlyWithTheConfirmedFlatReceiver()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(Source(true), "vfx/terrain.xml", _ => VfxModelVariantPixelTests.Dds(Color.White)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            preview.Seek(.2f, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var position = new Vector3(0, 4, 6);
            foreach (var ground in new float?[] { null, 0 })
            {
                var parameters = Camera(ground) with { View = Matrix.CreateLookAt(position, Vector3.Zero, Vector3.Up),
                    Projection = Matrix.CreateOrthographic(3, 3, .1f, 100), CameraPosition = position };
                device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(parameters); device.SetRenderTarget(null);
                var pixels = new Color[4096]; target.GetData(pixels);
                Assert.That(pixels.Count(p => p.R > 100), ground.HasValue ? Is.GreaterThan(20) : Is.Zero);
            }
        }
        finally { graphics.Release(); }
    }

    private static CommonShaderParameters Camera(float? ground) => new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: ground);
    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/terrain.xml", _ => [1]));
    private static byte[] Source(bool terrainSpawn, string pass = "DEFERRED_DECAL_EMISSIVE", float speed = 0)
    {
        var doc = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var effect = doc.Root.Element("vfx")!; var emitter = effect.Element("emitters")!.Element("emitter")!;
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleRelativeSpawnOrientation"), new XAttribute("value", "RELATIVE_TO_TERRAIN_NORMAL")),
            new XElement("property", new XAttribute("name", "ParticleRelativeSpawnDirection"), new XAttribute("value", "RELATIVE_TO_TERRAIN_NORMAL")),
            new XElement("property", new XAttribute("name", "ParticlesSpawnOnTerrain"), new XAttribute("value", terrainSpawn ? "true" : "false")),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), FormattableString.Invariant($"x_min,0,0;0,{speed},linear,linear,0,0,0,0,1:x,0;0,0")));
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", -90), new XAttribute("y", 0), new XAttribute("z", 0)));
        effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", pass)))));
        return doc.Write();
    }
}
