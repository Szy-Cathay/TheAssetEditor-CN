#nullable enable
using System.Text.Json;
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
public class VfxRigidOrientationTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 4, -10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    public static IEnumerable<TestCaseData> NativeControls()
    {
        using var stream = typeof(VfxRigidOrientationTests).Assembly.GetManifestResourceStream("Test.GameWorld.Core.Vfx.Fixtures.NativeRigidOrientation.json")!;
        var fixture = JsonSerializer.Deserialize<Fixture>(stream)!;
        return fixture.Cases.Select(control => new TestCaseData(control).SetName($"RigidNativeControl_{control.Index}"));
    }

    [TestCaseSource(nameof(NativeControls))]
    public void FixedRigidAngles_FollowTheOrderInferredFromBothNativeGeometries(Control control)
    {
        var source = Source(new(control.Angles[0], control.Angles[1], control.Angles[2]));
        var conversion = VfxModelVariantTests.Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var transform = simulation.Frame(Camera).Single().Transform;
        Near(transform.Right, Vector(control.Right)); Near(transform.Up, Vector(control.Up)); Near(transform.Backward, Vector(control.Backward));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void SpatialSprite_NativeX90ThenZ90KeepsLocalUpOnPositiveWorldZ()
    {
        var source = VfxDocument.Read(Source(new(90, 0, 90)));
        source.Root.Descendants("emitter").Single().SetAttributeValue("type", "continuous_mesh");
        source.Root.Descendants("property_rigid_data").Remove();
        source.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_translation"),
            new XElement("property", new XAttribute("name", "restriction"), new XAttribute("value", 3))));
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "vfx/spatial.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform.Up, Vector3.Backward);
    }

    [TestCase(1)]
    [TestCase(3)]
    public void InternalLegacyModelVelocityPlane_PreservesItsPreviousEulerAndRootRotation(int alignment)
    {
        var authored = new Vector3(23, -37, 19);
        var source = VfxDocument.Read(Source(authored));
        var emitter = source.Root.Descendants("emitter").Single();
        foreach (var name in new[] { "ParticleScale", "ParticleInitialSpeed", "EmitterOrientation", "ParticleRelativeSpawnOrientation", "SpawnArc", "MaximumParticles" })
            emitter.Elements().Where(node => (string?)node.Attribute("name") == name).Remove();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleScale"), VfxParticleEventTests.Curve(["x", "y", "z"], 1, 1, 1)),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), new XAttribute("value", 2)),
            new XElement("property", new XAttribute("name", "EmitterOrientation"), VfxParticleEventTests.Curve(["x", "y", "z"], 0, 0, 0)),
            new XElement("property", new XAttribute("name", "ParticleRelativeSpawnOrientation"), new XAttribute("value", "RELATIVE_TO_VFX_UP_AXIS")),
            new XElement("property", new XAttribute("name", "SpawnArc"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        var conversion = VfxModelVariantTests.Convert(source.Write());
        var definition = conversion.Emitters.Single();
        Assert.That(definition.Kind, Is.EqualTo(1));
        Assert.That(definition.Alignment, Is.EqualTo(2), "The converter still forces model alignment2; this control only guards the internal legacy simulation path.");
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.Alignment))!.SetValue(definition, alignment);
        var node = Matrix.CreateRotationY(.31f) * Matrix.CreateRotationZ(-.24f);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, node);
        var actual = simulation.Frame(Camera).Single().Transform;
        var look = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, node));
        var viewDirection = Vector3.Normalize(Camera.CameraPosition - actual.Translation);
        var side = Vector3.Normalize(Vector3.Cross(viewDirection, look));
        var up = alignment == 3 ? Vector3.Normalize(Vector3.Cross(side, viewDirection)) : look;
        var velocityBasis = Matrix.Identity;
        velocityBasis.Right = side; velocityBasis.Up = up; velocityBasis.Backward = Vector3.Cross(side, up);
        var oldRotation = Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(authored.Y), MathHelper.ToRadians(authored.X), MathHelper.ToRadians(authored.Z)) * node;
        var plane = Matrix.Identity;
        plane.M11 = oldRotation.M11; plane.M12 = oldRotation.M12; plane.M21 = oldRotation.M21; plane.M22 = oldRotation.M22;
        var expected = plane * velocityBasis;
        Near(actual.Right, expected.Right); Near(actual.Up, expected.Up);
    }

    [Test]
    public void EventChild_InheritsTheSameRigidOrientationAsItsParentGeometry()
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        SetAngles(parent, new(90, 0, 90));
        var child = VfxDocument.Read(VfxParticleEventTests.Source(false));
        child.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "EmitterPosition"),
            VfxParticleEventTests.Curve(["x", "y", "z"], 0, 1, 0)));
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", _ => child.Write()));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.CreateTranslation(2, 3, 4));
        Near(simulation.Frame(Camera).Single().Transform.Translation, new(2, 3, 5));
    }

    [Test]
    public void ReferenceRotation_RemainsSeparateFromTheRigidParticlesRotation()
    {
        var child = VfxDocument.Read(Source(new(90, 0, 90))); var composition = VfxDocument.CreateComposition();
        var edit = composition.AddReference("rigid", child); edit.Apply();
        edit.Selection.SetAttributeValue("rot_y", 30); edit.Selection.SetAttributeValue("rot_z", 70);
        var conversion = TotalWarVfxConverter.Convert(new(composition.Write(), "vfx/root.xml", path =>
            path.EndsWith("rigid.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var reference = Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(30), 0, MathHelper.ToRadians(70));
        Near(simulation.Frame(Camera).Single().Transform.Up, Vector3.TransformNormal(Vector3.Backward, reference));
    }

    [Test]
    public void RigidOrientation_ReachesTheGpuInsteadOfCollapsingTheFaceIntoAnEdge()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(Source(new(90, 0, 90)), "vfx/rigid.xml", path => path.EndsWith(".rigid_model_v2", StringComparison.OrdinalIgnoreCase)
                ? VfxModelVariantTests.Model(0) : VfxModelVariantPixelTests.Dds(Color.White)));
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            preview.Seek(.2f, Matrix.Identity); device.SetRenderTarget(target); device.Clear(Color.Black);
            var eye = new Vector3(10, .5f, .5f);
            preview.Draw(Camera with { CameraPosition = eye, View = Matrix.CreateLookAt(eye, new(0, .5f, .5f), Vector3.Up), Projection = Matrix.CreateOrthographic(2, 2, .1f, 100) });
            device.SetRenderTarget(null); var pixels = new Color[4096]; target.GetData(pixels);
            Assert.That(pixels.Count(p => p.R > 100 && p.G > 100), Is.GreaterThan(300), "The original incorrect order turns this face edge-on.");
        }
        finally { graphics.Release(); }
    }

    private static byte[] Source(Vector3 angles)
    {
        var doc = VfxDocument.Read(VfxModelVariantTests.Source(1)); SetAngles(doc, angles); return doc.Write();
    }

    private static void SetAngles(VfxDocument document, Vector3 angles)
    {
        var emitter = document.Root.Descendants("emitter").Single();
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
        {
            emitter.Elements().Where(p => (string?)p.Attribute("name") == name).Remove();
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", angles.X), new XAttribute("y", angles.Y), new XAttribute("z", angles.Z)));
        }
    }

    private static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00001f), $"{actual} != {expected}");
    public sealed class Control
    {
        public int Index { get; set; }
        public float[] Angles { get; set; } = [];
        public float[] Right { get; set; } = [];
        public float[] Up { get; set; } = [];
        public float[] Backward { get; set; } = [];
    }
    private sealed class Fixture { public Control[] Cases { get; set; } = []; }
}
