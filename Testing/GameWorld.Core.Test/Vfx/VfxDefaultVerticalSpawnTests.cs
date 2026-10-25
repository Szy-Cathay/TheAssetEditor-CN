#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxDefaultVerticalSpawnTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.002f)]
    [TestCase(.01f)]
    [TestCase(.1f)]
    public void DefaultVerticalBirth_KeepsTheNativeBasePlaneAcrossParticles(float speed)
    {
        var source = Source(speed);
        var conversion = Convert(source);
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Alignment, Is.EqualTo(4));
            Assert.That(emitter.DefaultVerticalSpawn, Is.True);
            Assert.That(emitter.Orientation.IsZero, Is.True);
            Assert.That(emitter.OrientationMin.X, Is.EqualTo(180));
        });
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var frame = simulation.Frame(Camera);
        Assert.That(frame, Has.Length.GreaterThan(100));
        var bases = frame.Select(particle => (Tangent: Vector3.Normalize(particle.Transform.Right),
            Normal: Vector3.Normalize(particle.Transform.Backward))).ToArray();
        var maxNormalError = bases.Max(basis => Vector3.Distance(basis.Normal, -Vector3.UnitX));
        var maxTangentX = bases.Max(basis => Math.Abs(basis.Tangent.X));
        var maxBinormalX = bases.Max(basis => Math.Abs(Vector3.Cross(basis.Normal, basis.Tangent).X));
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new { Speed = speed, ParticleCount = frame.Length,
            SourceSha256 = System.Convert.ToHexStringLower(SHA256.HashData(source)), MaxNormalError = maxNormalError,
            MaxTangentX = maxTangentX, MaxBinormalX = maxBinormalX,
            FirstNormals = bases.Take(5).Select(basis => new[] { basis.Normal.X, basis.Normal.Y, basis.Normal.Z }),
            Contract = "Actual official Gyro main raw velocity has a fixed negativeX pole residue. Native base velocity plane is -X; full particle X180 flips the separate shading normal to +X in event1627 PostVS. Geometry keeps its 2x2 orientation rule. No exact residue magnitude or fixed spin-dependent T/B.Y is assumed. CPU fixture only, no texture decode or GPU." }));
        Assert.Multiple(() =>
        {
            Assert.That(maxNormalError, Is.LessThan(.00001f), "A missing spawn sector must not amplify a random pole residue into a different plane.");
            Assert.That(maxTangentX, Is.LessThan(.00001f));
            Assert.That(maxBinormalX, Is.LessThan(.00001f));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExplicitLegacySpread_KeepsItsAuthoredRandomSector(bool restrictArc)
    {
        var source = Source(.01f, restrictArc);
        var conversion = Convert(source);
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Alignment, Is.EqualTo(4));
            Assert.That(emitter.DefaultVerticalSpawn, Is.False);
            Assert.That(emitter.LegacySpreadSpawn, Is.True);
        });
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var positions = simulation.Frame(Camera).Select(particle => particle.Transform.Translation)
            .Where(position => position.Length() > .0005f).ToArray();
        Assert.That(positions, Has.Length.GreaterThan(100));
        Assert.Multiple(() =>
        {
            Assert.That(positions.All(position => position.Y >= -.000001f
                && position.Y / position.Length() <= MathF.Sin(MathHelper.ToRadians(3)) + .000001f), Is.True);
            Assert.That(positions.Any(position => position.X < -.001f), Is.True);
            Assert.That(positions.Any(position => position.X > .001f), Is.True);
            if (restrictArc)
                Assert.That(positions.All(position => position.Z < 0 && Math.Abs(position.X) <= -position.Z + .000001f), Is.True);
            else
            {
                Assert.That(positions.Any(position => position.Z < -.001f), Is.True);
                Assert.That(positions.Any(position => position.Z > .001f), Is.True);
            }
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DefaultVerticalDistortion_TransmitsTheFullWorldShadingBasisSeparateFromGeometry(bool normalTexture)
    {
        var source = Source(.01f, distortion: true, normalTexture: normalTexture);
        var conversion = Convert(source);
        var emitter = conversion.Emitters.Single();
        Assert.That(emitter.Render.Distortion, Is.EqualTo(1));
        Assert.That(emitter.DefaultVerticalSpawn, Is.True);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var frame = simulation.Frame(Camera);
        Assert.That(frame, Has.Length.GreaterThan(100));
        // A zero payload means the old Transform fallback, not an identity shading basis.
        var zeroPayloads = frame.Count(particle => particle.NormalRotation == Vector4.Zero);
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new { ParticleCount = frame.Length, ZeroNormalPayloads = zeroPayloads,
            Contract = "NormalRotation carries full world shading basis directly: normal texture uses(T.x,T.y,B.x,B.y), no normal uses(N.xyz,1). Zero preserves v5 fallback. Actual main PostVS X180 gives N approximately+X and T.X/B.X approximately0. Geometry retains its native 2x2 orientation projection; no normalization of possibly non-orthogonal native basis." }));
        Assert.That(zeroPayloads, Is.Zero, "A missing shading basis must not be normalized into an apparent successful identity.");
        var bases = frame.Select(particle =>
        {
            return (Particle: particle, Payload: particle.NormalRotation,
                Tangent: Vector3.Normalize(particle.Transform.Right), Up: Vector3.Normalize(particle.Transform.Up),
                GeometryNormal: Vector3.Normalize(particle.Transform.Backward));
        }).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(bases.Max(item => Vector3.Distance(item.GeometryNormal, -Vector3.UnitX)), Is.LessThan(.00001f), "Do not change the geometry third axis to repair the shading basis.");
            if (normalTexture)
            {
                Assert.That(emitter.Render.TextureIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(bases.Max(item => Math.Abs(item.Payload.X)), Is.LessThan(.00001f));
                Assert.That(bases.Max(item => Math.Abs(item.Payload.Z)), Is.LessThan(.00001f));
                Assert.That(bases.Max(item => Math.Abs(item.Payload.Y - item.Tangent.Y)), Is.LessThan(.00001f));
                Assert.That(bases.Max(item => Math.Abs(item.Payload.W - item.Up.Y)), Is.LessThan(.00001f));
            }
            else
            {
                Assert.That(emitter.Render.TextureIndex, Is.EqualTo(-1));
                Assert.That(bases.Max(item => Vector3.Distance(new(item.Payload.X, item.Payload.Y, item.Payload.Z), Vector3.UnitX)), Is.LessThan(.00001f));
                Assert.That(bases.All(item => item.Payload.W == 1), Is.True);
            }
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    private static VfxConversion Convert(byte[] source)
        => TotalWarVfxConverter.Convert(new(source, "vfx/cpu-default-pole.xml", _ => [1]));

    private static byte[] Source(float speed, bool? restrictArc = null, bool distortion = false, bool normalTexture = true)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0, rate: 128));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"),
                new XAttribute("value", speed.ToString(CultureInfo.InvariantCulture))),
            new XElement("property", new XAttribute("name", "ParticleOrientationMin"), new XAttribute("x", 180), new XAttribute("y", 0), new XAttribute("z", 0)),
            new XElement("property", new XAttribute("name", "ParticleOrientationMax"), new XAttribute("x", 180), new XAttribute("y", 0), new XAttribute("z", 0)),
            new XElement("property", new XAttribute("name", "ParticleSpinMin"), new XAttribute("x", 0), new XAttribute("y", 0), new XAttribute("z", 90)),
            new XElement("property", new XAttribute("name", "ParticleSpinMax"), new XAttribute("x", 0), new XAttribute("y", 0), new XAttribute("z", 90)),
            new XElement("property", new XAttribute("name", "ParticleSpinDirectionZ"), new XAttribute("value", "CW")));
        var translation = document.Root.Descendants("modifier").Single(modifier => (string?)modifier.Attribute("fragment") == "vs_translation");
        translation.SetAttributeValue("fragment", "vs_translation_velocity_v2");
        translation.Add(new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Horizontally-oriented")),
            new XElement("property", new XAttribute("name", "velocity_stretch_mul"), new XAttribute("value", 0)));
        if (restrictArc is { } limited)
        {
            emitter.Add(new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 0), new XAttribute("max", 3)));
            if (limited)
                emitter.Add(new XElement("ranged_property", new XAttribute("name", "SpreadArc"), new XAttribute("min", 45), new XAttribute("max", 135)));
        }
        if (distortion)
        {
            var texture = document.Root.Descendants("modifier").Single(modifier => (string?)modifier.Attribute("fragment") == "ps_texture");
            if (normalTexture)
            {
                texture.Elements("texture_property").Single().SetAttributeValue("path", "");
                texture.Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", "vfx/cpu-normal.dds")));
            }
            document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "material0"),
                new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DISTORTION")))));
        }
        return document.Write();
    }
}
