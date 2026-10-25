#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxTranslationOrientationTests
{
    [TestCase(1, 6)]
    [TestCase(3, 2)]
    public void OrdinaryTranslation_MapsTheAuthoredOrientationWithoutEditingSource(int restriction, int alignment)
    {
        var source = Source(restriction, Vector3.Zero);
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters[0].Alignment, Is.EqualTo(alignment));
            Assert.That(conversion.Emitters[0].Render.Kind, Is.Zero);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(15, 0)]
    [TestCase(55, 0)]
    [TestCase(40, 45)]
    public void VerticalTranslation_RetainsWorldUpWhenTheCameraPitchAndYawChange(float pitch, float yaw)
    {
        var frame = Frame(1, Vector3.Zero, pitch, yaw);
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Normalize(frame.Up), Is.EqualTo(Vector3.Up));
            Assert.That(frame.Right.Y, Is.Zero.Within(.00001f));
            Assert.That(Vector3.Dot(frame.Right, frame.Up), Is.Zero.Within(.00001f));
            Assert.That(frame.Translation, Is.EqualTo(Vector3.Zero));
        });
    }

    [TestCase(0, 0, 0)]
    [TestCase(30, 50, 0)]
    [TestCase(0, 0, 30)]
    [TestCase(90, 180, 0)]
    public void SpatialTranslation_RetainsItsAuthoredWorldPlaneWhenTheCameraMoves(float x, float y, float z)
    {
        var orientation = new Vector3(x, y, z);
        var front = Frame(3, orientation, 15, 0);
        var high = Frame(3, orientation, 55, 0);
        var side = Frame(3, orientation, 40, 45);
        Assert.Multiple(() =>
        {
            Assert.That(high, Is.EqualTo(front));
            Assert.That(side, Is.EqualTo(front));
            Assert.That(front.Right.Length(), Is.GreaterThan(.9f));
            Assert.That(front.Up.Length(), Is.GreaterThan(.9f));
            Assert.That(Vector3.Dot(front.Right, front.Up), Is.Zero.Within(.00001f));
        });
    }

    [TestCase(1)]
    [TestCase(3)]
    public void DistortionTranslation_UsesTheSameGeometryWithoutChangingItsTextureRole(int restriction)
    {
        var source = Source(restriction, new(30, 50, 0), true);
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Render.Distortion, Is.EqualTo(1));
        var render = conversion.Emitters[0].Render;
        Assert.That(conversion.TextureUsages[render.TextureIndex], Is.EqualTo(VfxTextureUsage.DistortionNormal));
        Assert.That(conversion.TextureUsages[render.DistortionMaskIndex], Is.EqualTo(VfxTextureUsage.Diffuse));
    }

    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "vfx/facing.xml", _ => [1]));
    private static Matrix Frame(int restriction, Vector3 orientation, float pitch, float yaw)
    {
        var conversion = Convert(Source(restriction, orientation));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var p = MathHelper.ToRadians(pitch); var y = MathHelper.ToRadians(yaw);
        var camera = new Vector3(-MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p)) * 12;
        var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up), Matrix.Identity,
            camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        return simulation.Frame(parameters).Single().Transform;
    }

    internal static byte[] Source(int restriction, Vector3 orientation, bool distortion = false)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(new(.3f), false, 0));
        var effect = document.Root.Element("vfx")!;
        var translation = effect.Descendants("modifier").Single(x => (string?)x.Attribute("fragment") == "vs_translation");
        translation.Add(new XElement("property", new XAttribute("name", "restriction"), new XAttribute("type", "int"), new XAttribute("value", restriction)));
        var emitter = effect.Element("emitters")!.Element("emitter")!;
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", orientation.X),
                new XAttribute("y", orientation.Y), new XAttribute("z", orientation.Z)));
        if (distortion)
        {
            effect.Descendants("modifier").Single(x => (string?)x.Attribute("fragment") == "ps_texture")
                .Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("type", "file"), new XAttribute("path", "vfx/normal.dds")));
            effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "material0"),
                new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DISTORTION")))));
        }
        return document.Write();
    }
}
