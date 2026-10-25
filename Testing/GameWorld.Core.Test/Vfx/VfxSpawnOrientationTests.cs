#nullable enable
using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSpawnOrientationTests
{
    private static readonly Vector3 Eye = new(8, 5, 10);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(Eye, Vector3.Zero, Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, .1f, 1000) * Matrix.CreateScale(-1, 1, 1),
        Eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], ViewportHeight: 1000, ViewportWidth: 1000);

    [TestCase(null)]
    [TestCase("DISABLED")]
    public void CameraAlignedStretching_DefaultOrDisabledDoesNotInheritEmitterPitch(string? mode)
    {
        var source = Source(new(90, 0, 0));
        if (mode != null) Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", mode));
        var conversion = Convert(source);
        var transform = Frame(conversion, Matrix.Identity).Transform;
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().RelativeOrientation, Is.False);
            Assert.That(Area(transform), Is.EqualTo(1).Within(.00001f), "An authored X90 emitter must not flatten an independent sprite plane.");
        });
    }

    [Test]
    public void ExplicitUpAxis_PreservesAuthoredEmitterRollInTheLegacyPlane()
    {
        var source = Source(new(0, 0, 90));
        Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        var relative = Convert(source);
        var transform = Frame(relative, Matrix.Identity).Transform;
        Emitter(source).Elements().Single(p => (string?)p.Attribute("name") == "ParticleRelativeSpawnOrientation")
            .SetAttributeValue("value", "DISABLED");
        var independent = Frame(Convert(source), Matrix.Identity).Transform;
        Assert.Multiple(() =>
        {
            Assert.That(relative.Emitters.Single().RelativeOrientation, Is.True);
            Assert.That(Area(transform), Is.EqualTo(1).Within(.00001f));
            Assert.That(Vector3.Distance(Vector3.Normalize(transform.Right), Vector3.Normalize(independent.Up)), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(Vector3.Normalize(transform.Up), -Vector3.Normalize(independent.Right)), Is.LessThan(.00001f));
        });
    }

    [TestCase("0", false)]
    [TestCase("1", true)]
    [TestCase("2", false)]
    public void SpawnOrientation_UsesTheSelectedQualityOverride(string quality, bool expected)
    {
        var source = Source(new(0, 0, 90));
        Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", "DISABLED", "0"),
            Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", "1"),
            Property("ParticleRelativeSpawnOrientation", "DISABLED", "2"));
        Assert.That(Convert(source, quality).Emitters.Single().RelativeOrientation, Is.EqualTo(expected));
    }

    [TestCase("RelativeParticleOrientation", "true", true)]
    [TestCase("RelativeParticleOrientation", "false", false)]
    [TestCase("ParticleRelativeOrientation", "1", true)]
    [TestCase("ParticleRelativeOrientation", "0", false)]
    public void ExplicitLegacyBooleanAliases_KeepTheirMeaning(string field, string value, bool expected)
    {
        var source = Source(new(0, 0, 90));
        Emitter(source).Add(Property(field, value));
        Assert.That(Convert(source).Emitters.Single().RelativeOrientation, Is.EqualTo(expected));
    }

    [TestCase("RelativeParticleOrientation")]
    [TestCase("ParticleRelativeOrientation")]
    public void ConflictingModernAndLegacyValues_RejectTheLayer(string legacyField)
    {
        var source = Source(new(90, 0, 0));
        Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", "DISABLED"), Property(legacyField, "true"));
        AssertAmbiguous(Convert(source, requireEmitter: false));
    }

    [Test]
    public void ConflictingLegacyAliases_RejectTheLayer()
    {
        var source = Source(new(90, 0, 0));
        Emitter(source).Add(Property("RelativeParticleOrientation", "true"), Property("ParticleRelativeOrientation", "false"));
        AssertAmbiguous(Convert(source, requireEmitter: false));
    }

    [Test]
    public void EmitterPitch_StillRotatesInitialVelocityWithoutEnteringTheIndependentSpriteOrientation()
    {
        var source = Source(new(90, 0, 0));
        Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", "DISABLED"));
        var conversion = Convert(source);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, Matrix.Identity);
        var particle = ((IEnumerable)Field(simulation, "_particles")).Cast<object>().Single();
        var velocity = (Vector3)Field(particle, "Velocity");
        var orientation = (Vector3)Field(particle, "Orientation");
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(velocity, new(0, 0, 2)), Is.LessThan(.00001f));
            Assert.That(orientation, Is.EqualTo(Vector3.Zero));
            Assert.That(Area(simulation.Frame(Camera).Single().Transform), Is.EqualTo(1).Within(.00001f));
        });
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", false)]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION", true)]
    public void DisabledSprite_OnlyFullLocalSpaceFollowsTheAttachmentRotationAfterBirth(string space, bool followsRotation)
    {
        var source = Source(Vector3.Zero);
        Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", "DISABLED"));
        Emitter(source).Elements().Single(p => (string?)p.Attribute("name") == "ParticleSimulationSpace").SetAttributeValue("value", space);
        var conversion = Convert(source);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.CreateRotationX(MathHelper.PiOver2));
        var atBirth = simulation.Frame(Camera).Single().Transform;
        simulation.Seek(.2f, Matrix.CreateRotationX(MathHelper.ToRadians(120)));
        var later = simulation.Frame(Camera).Single().Transform;
        Assert.Multiple(() =>
        {
            Assert.That(atBirth.Right.Length(), Is.EqualTo(1).Within(.00001f));
            Assert.That(atBirth.Up.Length(), Is.EqualTo(1).Within(.00001f), "Initial absolute attachment rotation must not enter an independent particle orientation.");
            Assert.That(Area(atBirth), Is.EqualTo(1).Within(.00001f));
            Assert.That(later.Right.Length(), Is.EqualTo(1).Within(.00001f));
            Assert.That(later.Up.Length(), Is.EqualTo(followsRotation ? MathF.Cos(MathHelper.ToRadians(30)) : 1).Within(.00001f),
                "Full local rotation uses the change since birth; world and position-only modes retain independent facing.");
        });
    }

    [TestCase("unknown")]
    [TestCase("3")]
    [TestCase("NaN")]
    public void InvalidModernMode_DoesNotSilentlyBecomeIndependent(string mode)
    {
        var source = Source(Vector3.Zero);
        Emitter(source).Add(Property("ParticleRelativeSpawnOrientation", mode));
        var conversion = Convert(source, requireEmitter: false);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleRelativeSpawnOrientation"
                && d.ReasonKey == "Vfx.Preview.OrientationUnsupported"), Is.True);
        });
    }

    private static void AssertAmbiguous(VfxConversion conversion)
    {
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
        });
    }

    private static VfxConversion Convert(VfxDocument document, string quality = "0", bool requireEmitter = true)
    {
        var bytes = document.Write(); var before = bytes.ToArray();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/spawn-orientation-control.xml", _ => [1], quality));
        Assert.That(bytes, Is.EqualTo(before));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(before));
        if (requireEmitter) Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics));
        return conversion;
    }

    private static EffekseerParticle Frame(VfxConversion conversion, Matrix root)
    {
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, root);
        return simulation.Frame(Camera).Single();
    }

    private static float Area(Matrix matrix) => Vector3.Cross(matrix.Right, matrix.Up).Length()
        / (matrix.Right.Length() * matrix.Up.Length());
    private static object Field(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
    private static XElement Emitter(VfxDocument document) => document.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value, string? quality = null)
        => new("property", new XAttribute("name", name), new XAttribute("value", value),
            quality == null ? null : new XAttribute("quality", quality));
    private static string Constant(string axis, float value) => FormattableString.Invariant($"{axis}_min,0,0;0,{value},linear,linear,0,0,0,0,1:{axis},0;0,0");
    private static string VectorCurve(Vector3 value) => string.Join('|', new[] { Constant("x", value.X).Split(':')[0],
        Constant("y", value.Y).Split(':')[0], Constant("z", value.Z).Split(':')[0] }) + ":x,0;0,0|y,0;1,1|z,0;2,2";

    private static VfxDocument Source(Vector3 emitterOrientation) => VfxDocument.Read(Encoding.UTF8.GetBytes($"""
        <vfx revision="2"><emitters><emitter id="orientation" type="continuous_mesh">
        <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{Constant("x", 1)}</property>
        <property name="ParticleLifeTime">{Constant("x", 1)}</property><property name="ParticleScale">{VectorCurve(Vector3.One)}</property>
        <property name="ParticleInitialSpeed">{Constant("x", 2)}</property><property name="EmitterOrientation">{VectorCurve(emitterOrientation)}</property>
        <property name="ParticleSimulationSpace" value="WORLD_SPACE"/><property name="SpawnArc" x="0" y="0"/>
        </emitter></emitters><behaviour_chains><behaviour_chain id="orientation"><quality value="0">
        <modifier fragment="vs_translation_velocity_v2"><property name="orientation_mode" value="Camera-aligned with stretching factor"/></modifier>
        <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/control.dds"/></modifier>
        </quality></behaviour_chain></behaviour_chains></vfx>
        """));
}