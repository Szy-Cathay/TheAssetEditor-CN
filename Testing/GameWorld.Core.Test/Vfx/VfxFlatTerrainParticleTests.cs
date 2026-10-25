using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxFlatTerrainParticleTests
{
    [TestCase(false, 0, 4)]
    [TestCase(false, .6f, 4.6f)]
    [TestCase(true, -.6f, -.6f)]
    [TestCase(true, 0, 0)]
    [TestCase(true, .6f, .6f)]
    public void StaticSprite_UsesNativeGroundPlusEmitterOffset(bool spawn, float offset, float expectedY)
    {
        var bytes = Source(spawn, offset);
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.CreateTranslation(2, 4, 3));
        var particle = simulation.Frame(Camera(0)).Single();
        Assert.That(particle.Transform.Translation, Is.EqualTo(new Vector3(2, expectedY, 3)).Using(Vector3Comparer));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void StaticModel_KeepsItsGeometryAndUsesTheSameFlatBirthHeight()
    {
        var doc = VfxDocument.Read(VfxModelVariantTests.Source(1));
        TerrainProperties(doc, true, -.6f);
        var bytes = doc.Write(); var conversion = VfxModelVariantTests.Convert(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.CreateTranslation(0, 4, 0));
        Assert.That(simulation.Frame(Camera(0)).Single().Transform.Translation.Y, Is.EqualTo(-.6f).Within(.00001));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(null, 0, 1)]
    [TestCase(0f, 30, 1)]
    [TestCase(0f, 0, -1)]
    public void MissingGroundOrRotatedMirroredRoot_RemainsDiagnosedAndCanRecover(float? ground, float yaw, float scale)
    {
        var conversion = Convert(Source(true, .6f)); Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.CreateScale(scale) * Matrix.CreateRotationY(MathHelper.ToRadians(yaw)));
        Assert.That(simulation.Frame(Camera(ground)), Is.Empty);
        Assert.That(simulation.TerrainBasisUnsupported, Is.True);
        simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera(0)), Has.Length.EqualTo(1));
        Assert.That(simulation.TerrainBasisUnsupported, Is.False);
    }

    [TestCase(false, .5f)]
    [TestCase(false, 2f)]
    [TestCase(false, .7f)]
    [TestCase(true, .5f)]
    [TestCase(true, 2f)]
    [TestCase(true, .7f)]
    public void PositiveUniformReferenceScale_KeepsGroundBirthOffsetUnscaled(bool model, float scale)
    {
        var child = VfxDocument.Read(model ? VfxModelVariantTests.Source(1) : Source(true, 4));
        if (model) TerrainProperties(child, true, 4);
        var childBytes = child.Write(); var parent = VfxDocument.CreateComposition();
        var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.SetAttributeValue("scale_min", scale); edit.Selection.SetAttributeValue("scale_max", scale);
        edit.Selection.SetAttributeValue("pos_x", 2); edit.Selection.SetAttributeValue("pos_z", 3);
        var bytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/parent.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? childBytes : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.CreateTranslation(0, 4, 0));
        var frame = simulation.Frame(Camera(0));
        Assert.That(frame, Has.Length.EqualTo(1), "A positive uniform scale preserves the confirmed flat terrain basis.");
        var particle = frame.Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.Transform.Translation, Is.EqualTo(new Vector3(2, 4, 3)).Using(Vector3Comparer), "Native birth height is ground plus the original emitter offset, independent of reference scale.");
            Assert.That(particle.Transform.Right.Length(), Is.EqualTo(scale * (model ? 1 : 1.6f)).Within(.00001));
            Assert.That(particle.Transform.Up.Length(), Is.EqualTo(scale * (model ? 1 : 1.6f)).Within(.00001));
            Assert.That(simulation.TerrainBasisUnsupported, Is.False);
            Assert.That(child.Write(), Is.EqualTo(childBytes));
            Assert.That(parent.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void UncalibratedScaledTerrainWithoutGroundBirth_RemainsDiagnosed()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(false, .6f)));
        simulation.Seek(.2f, Matrix.CreateScale(2));
        Assert.That(simulation.Frame(Camera(0)), Is.Empty);
        Assert.That(simulation.TerrainBasisUnsupported, Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void NonuniformShearedOrMirroredTerrainRoot_RemainsDiagnosed(int kind)
    {
        var shear = Matrix.Identity; shear.M12 = .1f;
        var matrix = kind == 0 ? Matrix.CreateScale(1, 2, 1) : kind == 1 ? shear : Matrix.CreateScale(-1, 1, 1);
        var simulation = new TotalWarVfxSimulation(Convert(Source(true, 4))); simulation.Seek(.2f, matrix);
        Assert.That(simulation.Frame(Camera(0)), Is.Empty);
        Assert.That(simulation.TerrainBasisUnsupported, Is.True);
    }

    [TestCase("ParticleInitialSpeed")]
    [TestCase("ForceA")]
    [TestCase("EmitterOrientation")]
    [TestCase("ParticlesFollowTerrain")]
    public void UncalibratedTerrainMotion_RemainsDiagnosed(string field)
    {
        var doc = VfxDocument.Read(Source(true, 0)); var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", field), field == "ParticlesFollowTerrain"
            ? new XAttribute("value", "true") : field is "ForceA" or "EmitterOrientation" ? VectorCurve(1) : VfxLibraryVisibilityTests.Constant("x", 1)));
        var conversion = Convert(doc.Write());
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.OrientationUnsupported"), Is.True);
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void GroundOcclusion_MatchesNativeBelowHalfAndAbovePlaneControls()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var counts = new List<int>();
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24, 4, RenderTargetUsage.DiscardContents);
            var eye = new Vector3(0, 8, -8);
            var camera = Camera(0) with { View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Projection = Matrix.CreateOrthographic(6, 6, .1f, 100) * Matrix.CreateScale(-1, 1, 1), CameraPosition = eye };
            foreach (var offset in new[] { -.6f, 0, .6f })
            {
                using var preview = new EffekseerVfxPreview(device);
                preview.Reload(new(Source(true, offset), "vfx/terrain-particle.xml", _ => VfxModelVariantPixelTests.Dds(Color.White)));
                Assert.That(preview.EmitterCount, Is.EqualTo(1)); preview.Seek(.2f, Matrix.CreateTranslation(0, 4, 0));
                device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Color[128 * 128]; target.GetData(pixels); counts.Add(pixels.Count(p => p.R > 100));
            }
            Assert.That(counts[0], Is.Zero, "The complete quad is below the confirmed receiver.");
            Assert.That(counts[2], Is.GreaterThan(500));
            Assert.That(counts[1] / (double)counts[2], Is.InRange(.4, .6), "A ground-centered camera-facing plane is half occluded.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GroundDepthGeometry_CoversOnlyTheVisibleFrustumSection(bool reflected)
    {
        var eye = new Vector3(4, 8, -10); var view = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(1, 1.5f, .1f, 500) * Matrix.CreateScale(reflected ? -1 : 1, 1, 1);
        var vertices = VfxFlatGroundDepth.Triangles(view * projection, 0);
        Assert.That(vertices.Length, Is.GreaterThanOrEqualTo(6));
        foreach (var vertex in vertices)
        {
            Assert.That(vertex.Position.Y, Is.Zero);
            var clip = Vector4.Transform(new Vector4(vertex.Position, 1), view * projection);
            Assert.That(Math.Abs(clip.X / clip.W), Is.LessThanOrEqualTo(1.001f));
            Assert.That(Math.Abs(clip.Y / clip.W), Is.LessThanOrEqualTo(1.001f));
            Assert.That(clip.Z / clip.W, Is.InRange(-.001f, 1.001f));
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void MissingDepth_SkipsTerrainGeometryAndRecoversOnAValidTarget()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(Source(true, .6f), "vfx/terrain-particle.xml", _ => VfxModelVariantPixelTests.Dds(Color.White)));
            preview.Seek(.2f, Matrix.Identity); var eye = new Vector3(0, 8, -8);
            var camera = Camera(0) with { View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Projection = Matrix.CreateOrthographic(6, 6, .1f, 100), CameraPosition = eye };
            foreach (var depth in new[] { DepthFormat.None, DepthFormat.Depth24 })
            {
                using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, depth);
                device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Color[4096]; target.GetData(pixels);
                Assert.That(pixels.Count(p => p.R > 100), depth == DepthFormat.None ? Is.Zero : Is.GreaterThan(100));
                Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.FlatTerrainDepthUnavailable"), Is.EqualTo(depth == DepthFormat.None));
            }
        }
        finally { graphics.Release(); }
    }

    private static readonly IEqualityComparer<Vector3> Vector3Comparer = EqualityComparer<Vector3>.Create(
        (left, right) => Vector3.DistanceSquared(left, right) < .00000001f);
    private static CommonShaderParameters Camera(float? ground) => new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: ground);
    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/terrain-particle.xml", _ => [1]));
    private static byte[] Source(bool spawn, float offset)
    {
        var doc = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); TerrainProperties(doc, spawn, offset);
        doc.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleInitialScale"),
            string.Join('|', new[] { "x", "y", "z" }.Select(c => VfxLibraryVisibilityTests.Constant(c, 1.6f).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2"));
        return doc.Write();
    }
    private static void TerrainProperties(VfxDocument doc, bool spawn, float offset)
    {
        doc.Root.Descendants("emitter").Single().Add(
            new XElement("property", new XAttribute("name", "ParticleRelativeSpawnOrientation"), new XAttribute("value", "RELATIVE_TO_TERRAIN_NORMAL")),
            new XElement("property", new XAttribute("name", "ParticleRelativeSpawnDirection"), new XAttribute("value", "RELATIVE_TO_TERRAIN_NORMAL")),
            new XElement("property", new XAttribute("name", "ParticlesSpawnOnTerrain"), new XAttribute("value", spawn ? "true" : "false")),
            new XElement("property", new XAttribute("name", "EmitterPosition"), VectorCurve(offset)));
    }
    private static string VectorCurve(float y) => string.Join('|', new[] { "x", "y", "z" }
        .Select(c => VfxLibraryVisibilityTests.Constant(c, c == "y" ? y : 0).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
}
