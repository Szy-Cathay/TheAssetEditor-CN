#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxOrdinaryBillboardPoseTests
{
    private const string RelativeUp = "RELATIVE_TO_VFX_UP_AXIS";
    private static readonly Vector3 Eye = new(8, 5, 10);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(Eye, Vector3.Zero, Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, .1f, 1000),
        Eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MultiAxisAuthoredPose_UsesNativeXThenYThenZBeforeRestriction(int restriction)
    {
        var authored = new Vector3(31, -47, 23);
        var actual = Frame(Source(restriction, authored), Matrix.Identity);
        AssertBasis(actual, Map(NativeAuthored(authored), restriction));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ExplicitRelativePose_ComposesAuthoredEmitterAndNodeWithoutEulerAddition(int restriction)
    {
        var authored = new Vector3(32, -27, 19);
        var emitter = new Vector3(-18, 41, 24);
        var node = Matrix.CreateRotationX(.43f) * Matrix.CreateRotationY(-.58f) * Matrix.CreateRotationZ(.21f);
        var actual = Frame(Source(restriction, authored, emitter, RelativeUp), node);
        AssertBasis(actual, Map(NativeAuthored(authored) * ExistingEmitter(emitter) * node, restriction));
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", false)]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION", true)]
    public void MissingRelativeMode_PreservesIndependentBirthAndOnlyFullLocalRotationDelta(string space, bool followsRotation)
    {
        var authored = new Vector3(29, -33, 17);
        var birthNode = Matrix.CreateRotationX(.47f) * Matrix.CreateRotationY(-.31f);
        var currentNode = Matrix.CreateRotationY(.71f) * Matrix.CreateRotationZ(.26f);
        var simulation = Simulation(Source(3, authored, new(65, 12, -9), space: space));
        simulation.Seek(0, birthNode);
        AssertBasis(simulation.Frame(Camera).Single().Transform, NativeAuthored(authored));
        simulation.Seek(.25f, currentNode);
        var expected = NativeAuthored(authored);
        if (followsRotation) expected *= Matrix.Transpose(birthNode) * currentNode;
        AssertBasis(simulation.Frame(Camera).Single().Transform, expected);
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", false)]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION", true)]
    public void RelativeNodeRotation_IsBakedOnceAtBirthAndRetainsTheCorrectLaterSpace(string space, bool followsRotation)
    {
        var authored = new Vector3(13, 39, -28);
        var emitter = new Vector3(-11, 27, 16);
        var birthNode = Matrix.CreateRotationX(.61f) * Matrix.CreateRotationY(-.44f);
        var currentNode = Matrix.CreateRotationZ(.37f) * Matrix.CreateRotationY(.24f);
        var simulation = Simulation(Source(3, authored, emitter, RelativeUp, space));
        simulation.Seek(0, birthNode);
        AssertBasis(simulation.Frame(Camera).Single().Transform, NativeAuthored(authored) * ExistingEmitter(emitter) * birthNode);
        simulation.Seek(.25f, currentNode);
        var expected = NativeAuthored(authored) * ExistingEmitter(emitter) * (followsRotation ? currentNode : birthNode);
        AssertBasis(simulation.Frame(Camera).Single().Transform, expected);
    }

    [TestCase("DISABLED")]
    [TestCase(RelativeUp)]
    public void HorizontalRestriction_StaysInWorldXZUnderPitchedNode(string relativeMode)
    {
        var node = Matrix.CreateRotationX(.62f) * Matrix.CreateRotationZ(-.19f);
        var actual = Frame(Source(2, new(24, -19, 13), new(12, 15, -8), relativeMode), node);
        var normal = Vector3.Normalize(Vector3.Cross(actual.Right, actual.Up));
        Assert.Multiple(() =>
        {
            Assert.That(actual.Right.Y, Is.Zero.Within(.00001f));
            Assert.That(actual.Up.Y, Is.Zero.Within(.00001f));
            Assert.That(MathF.Abs(normal.Y), Is.EqualTo(1).Within(.00001f));
        });
    }

    [TestCase(55, 0, 0)]
    [TestCase(0, 55, 0)]
    public void CameraRestriction_ProjectsAuthoredPitchAndYawInsteadOfDiscardingThem(float x, float y, float z)
    {
        var authored = new Vector3(x, y, z);
        var actual = Frame(Source(0, authored), Matrix.Identity);
        AssertBasis(actual, Map(NativeAuthored(authored), 0));
        Assert.That(Vector3.Cross(actual.Right, actual.Up).Length(), Is.EqualTo(MathF.Cos(MathHelper.ToRadians(55))).Within(.00001f));
    }

    [Test]
    public void VerticalRestriction_UsesFullQuaternionProjectionOnCameraRightAndWorldUp()
    {
        var authored = new Vector3(37, 29, -26);
        var rotation = NativeAuthored(authored);
        var actual = Frame(Source(1, authored), Matrix.Identity);
        AssertBasis(actual, Map(rotation, 1));
        Assert.That(actual.Up.Y, Is.EqualTo(rotation.M22).Within(.00001f));
    }

    [TestCase(1, 35)]
    [TestCase(2, 35)]
    [TestCase(3, 90)]
    public void SingleLocalZSpin_RotatesBeforeBirthTiltAndPreservesItsPlane(int restriction, float tilt)
    {
        var authored = new Vector3(tilt, 0, 0);
        var source = Source(restriction, authored, spin: new(0, 0, 90));
        var actual = Frame(source, Matrix.Identity, .5f);
        var expected = ContinuousLocalSpin(new(0, 0, 90), .5f) * NativeAuthored(authored);
        AssertBasis(actual, Map(expected, restriction));
        if (restriction == 3)
            Assert.That(Vector3.Normalize(Vector3.Cross(actual.Right, actual.Up)).Y, Is.EqualTo(-1).Within(.00001f));
    }

    [Test]
    public void MultiAxisLocalSpin_MatchesTheConstantOmegaContinuousCandidateWithoutEulerAngleAddition()
    {
        // This is the continuous candidate, not the native per-step normalized Euler integrator or Z-only damping.
        var authored = new Vector3(28, -36, 21);
        var emitter = new Vector3(-17, 26, 12);
        var spin = new Vector3(43, 71, 29);
        var node = Matrix.CreateRotationY(.41f) * Matrix.CreateRotationX(-.23f);
        var source = Source(3, authored, emitter, RelativeUp, spin: spin);
        var actual = Frame(source, node, .5f);
        var expected = ContinuousLocalSpin(spin, .5f) * NativeAuthored(authored) * ExistingEmitter(emitter) * node;
        AssertBasis(actual, expected);
    }

    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    public void OrdinaryDistortion_PreservesFullShadingBasisSeparatelyFromProjectedGeometry(int restriction, bool normalTexture)
    {
        var authored = new Vector3(38, -27, 19);
        var source = DistortionSource(restriction, authored, normalTexture);
        var simulation = Simulation(source); simulation.Seek(0, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        var rotation = NativeAuthored(authored);
        var shading = ShadingBasis(rotation, restriction);
        var expected = normalTexture ? new Vector4(shading.Right.X, shading.Right.Y, shading.Up.X, shading.Up.Y)
            : new(shading.Backward, 1);
        Assert.Multiple(() =>
        {
            Assert.That(Vector4.Distance(particle.NormalRotation, expected), Is.LessThan(.00001f));
            Assert.That(particle.NormalRotation, Is.Not.EqualTo(Vector4.Zero));
        });
        AssertBasis(particle.Transform, Map(rotation, restriction));
    }

    [Test]
    public void OrdinaryDistortion_LeavesProjectionMirrorCompensationToTheRenderer()
    {
        var simulation = Simulation(DistortionSource(0, new(38, -27, 19), true)); simulation.Seek(0, Matrix.Identity);
        var original = simulation.Frame(Camera).Single();
        var reflected = simulation.Frame(Camera with { Projection = Camera.Projection * Matrix.CreateScale(-1, 1, 1) }).Single();
        Assert.That(reflected.NormalRotation, Is.EqualTo(original.NormalRotation), "The bridge already compensates the mirrored local-X tangent.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LitCameraBillboard_KeepsFullQuaternionAndUnprojectedParticleSize(bool reflected)
    {
        var authored = new Vector3(38, -27, 19);
        var source = LightingSource(authored, false);
        var simulation = Simulation(source); simulation.Seek(0, Matrix.CreateScale(1.4f));
        var parameters = Camera with { Projection = reflected ? Camera.Projection * Matrix.CreateScale(-1, 1, 1) : Camera.Projection };
        var particle = simulation.Frame(parameters).Single();
        var q = particle.NormalRotation;
        var actual = Matrix.CreateFromQuaternion(new(q.X, q.Y, q.Z, q.W));
        var expected = ShadingBasis(NativeAuthored(authored), 0);
        AssertBasis(actual, expected);
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(.00001f));
            Assert.That(particle.ParticleSize, Is.EqualTo(3.5f).Within(.00001f));
            Assert.That(MathF.Abs(particle.Transform.Right.Length() - particle.ParticleSize), Is.GreaterThan(.1f),
                "This pitched/yawed control must distinguish source size from the shorter projected edge.");
        });
    }

    [Test]
    public void LitVelocityBillboard_PreservesTheCalibratedY180CorrectionOnItsFullShadingBasis()
    {
        var authored = new Vector3(31, -23, 17);
        var source = LightingSource(authored, true);
        var simulation = Simulation(source); simulation.Seek(0, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        var viewDirection = Vector3.Normalize(Eye);
        var side = Vector3.Normalize(Vector3.Cross(viewDirection, Vector3.Up));
        var up = Vector3.Normalize(Vector3.Cross(side, viewDirection));
        var velocityBasis = Matrix.Identity;
        velocityBasis.Right = side; velocityBasis.Up = up; velocityBasis.Backward = Vector3.Cross(side, up);
        var expected = Matrix.CreateRotationY(MathHelper.Pi) * NativeAuthored(authored) * velocityBasis;
        var q = particle.NormalRotation;
        var actual = Matrix.CreateFromQuaternion(new(q.X, q.Y, q.Z, q.W));
        AssertBasis(actual, expected);
        Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(.00001f));
        Assert.That(particle.ParticleSize, Is.EqualTo(2.5f).Within(.00001f));
    }

    [Test]
    public void ZeroVelocityLighting_KeepsTheExistingPreviewNormalFallback()
    {
        var source = LightingSource(new(31, -23, 17), true);
        source.Root.Descendants("emitter").Single().Elements("property")
            .Single(node => (string?)node.Attribute("name") == "ParticleInitialSpeed").Value = Constant("x", 0);
        var simulation = Simulation(source); simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().NormalRotation, Is.EqualTo(Vector4.Zero),
            "Zero speed retains the prior preview fallback; this control does not establish new native zero-speed support.");
    }

    [Test]
    public void ConversionAndPoseSampling_PreserveOriginalUtf8BomWhitespaceAndUnknownSourceBytes()
    {
        var xml = Encoding.UTF8.GetString(Source(0, new(23, 41, -19)).Write())
            .Replace("<emitters>", "<!-- original pose control -->\r\n  <emitters>", StringComparison.Ordinal)
            .Replace("</emitter>", "<property name=\"UnknownPoseData\" value=\"keep\" /></emitter>", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(xml)).ToArray();
        var before = bytes.ToArray();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/ordinary-pose-bytes.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.5f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(before));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(before));
        });
    }

    private static Matrix NativeAuthored(Vector3 degrees)
    {
        // Scalar formula from current native shared birth 0x6be398-0x6be475: qZ * qY * qX.
        var half = degrees * (MathHelper.Pi / 360);
        var sx = MathF.Sin(half.X); var cx = MathF.Cos(half.X);
        var sy = MathF.Sin(half.Y); var cy = MathF.Cos(half.Y);
        var sz = MathF.Sin(half.Z); var cz = MathF.Cos(half.Z);
        return Matrix.CreateFromQuaternion(new(sx * cy * cz - cx * sy * sz, cx * sy * cz + sx * cy * sz,
            cx * cy * sz - sx * sy * cz, cx * cy * cz + sx * sy * sz));
    }

    private static Matrix ExistingEmitter(Vector3 degrees) => Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(degrees.Y),
        MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Z));

    private static Matrix ContinuousLocalSpin(Vector3 degreesPerSecond, float seconds)
    {
        var omega = degreesPerSecond * (MathHelper.Pi / 180);
        var magnitude = omega.Length();
        var halfAngle = magnitude * seconds / 2;
        return magnitude == 0 ? Matrix.Identity
            : Matrix.CreateFromQuaternion(new(omega / magnitude * MathF.Sin(halfAngle), MathF.Cos(halfAngle)));
    }

    private static Matrix Map(Matrix rotation, int restriction)
    {
        if (restriction == 3) return rotation;
        var inverseView = Matrix.Invert(Camera.View); var result = Matrix.Identity;
        var side = restriction == 2 ? Vector3.Right : inverseView.Right;
        var up = restriction == 2 ? Vector3.Backward : restriction == 1 ? Vector3.Up : inverseView.Up;
        result.Right = rotation.M11 * side + rotation.M12 * up;
        result.Up = rotation.M21 * side + rotation.M22 * up;
        return result;
    }

    private static Matrix ShadingBasis(Matrix rotation, int restriction)
    {
        var inverseView = Matrix.Invert(Camera.View); inverseView.Translation = Vector3.Zero;
        return rotation * (restriction is 0 or 1 ? inverseView : restriction == 2 ? Matrix.CreateRotationX(MathHelper.PiOver2) : Matrix.Identity);
    }

    private static VfxDocument DistortionSource(int restriction, Vector3 authored, bool normalTexture)
    {
        var source = Source(restriction, authored);
        if (normalTexture) source.Root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "ps_texture")
            .Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", "vfx/normal.dds")));
        source.Root.Descendants("vfx").Single().Add(new XElement("effect_render_stacks",
            new XElement("effect_render_stack", new XAttribute("id", "pose"),
                new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DISTORTION")))));
        return source;
    }

    private static VfxDocument LightingSource(Vector3 authored, bool velocity)
    {
        var source = Source(0, authored);
        var emitter = source.Root.Descendants("emitter").Single();
        emitter.Elements("property").Single(node => (string?)node.Attribute("name") == "ParticleScale").Value = VectorCurve(new(2.5f, 1.6f, 1));
        var quality = source.Root.Descendants("quality").Single();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "ps_lighting"),
            new XElement("property", new XAttribute("name", "emissive"), new XAttribute("type", "float"), new XAttribute("value", ".2"))));
        if (velocity)
        {
            quality.Elements("modifier").Single(node => (string?)node.Attribute("fragment") == "vs_translation")
                .ReplaceWith(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
                    new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-aligned with stretching factor"))));
            emitter.Elements("property").Single(node => (string?)node.Attribute("name") == "ParticleInitialSpeed").Value = Constant("x", 2);
            emitter.Add(new XElement("property", new XAttribute("name", "SpawnArc"), new XAttribute("x", 0), new XAttribute("y", 0)));
        }
        return source;
    }

    private static void AssertBasis(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(.00001f), "Particle local X must follow the audited native restriction.");
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(.00001f), "Particle local Y must follow the audited native restriction.");
        });
    }

    private static TotalWarVfxSimulation Simulation(VfxDocument source)
    {
        var bytes = source.Write(); var before = bytes.ToArray();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/ordinary-pose-control.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics));
        Assert.That(bytes, Is.EqualTo(before));
        return new(conversion);
    }

    private static Matrix Frame(VfxDocument source, Matrix node, float seconds = 0)
    {
        var simulation = Simulation(source); simulation.Seek(seconds, node);
        return simulation.Frame(Camera).Single().Transform;
    }

    private static string Constant(string axis, float value) => FormattableString.Invariant($"{axis}_min,0,0;0,{value},linear,linear,0,0,0,0,1:{axis},0;0,0");
    private static string VectorCurve(Vector3 value) => string.Join('|', new[] { Constant("x", value.X).Split(':')[0],
        Constant("y", value.Y).Split(':')[0], Constant("z", value.Z).Split(':')[0] }) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static string VectorProperty(string name, Vector3 value)
        => FormattableString.Invariant($"<property name=\"{name}\" x=\"{value.X}\" y=\"{value.Y}\" z=\"{value.Z}\"/>");

    private static VfxDocument Source(int restriction, Vector3 authored, Vector3 emitter = default, string? relative = null,
        string space = "WORLD_SPACE", Vector3 spin = default) => VfxDocument.Read(Encoding.UTF8.GetBytes($"""
        <vfx revision="2"><emitters><emitter id="pose" type="continuous_mesh">
        <property name="InfiniteLife" value="true"/><property name="MaximumParticles" value="1"/>
        <property name="EmissionRateCurve">{Constant("x", 1)}</property><property name="ParticleLifeTime">{Constant("x", 2)}</property>
        <property name="ParticleScale">{VectorCurve(Vector3.One)}</property><property name="ParticleInitialSpeed">{Constant("x", 0)}</property>
        <property name="EmitterOrientation">{VectorCurve(emitter)}</property><property name="ParticleSimulationSpace" value="{space}"/>
        {(relative == null ? "" : $"<property name=\"ParticleRelativeSpawnOrientation\" value=\"{relative}\"/>")}
        {VectorProperty("ParticleOrientationMin", authored)}{VectorProperty("ParticleOrientationMax", authored)}
        {VectorProperty("ParticleSpin_Min", spin)}{VectorProperty("ParticleSpin_Max", spin)}
        <property name="ParticleSpinDirectionX" value="CCW"/><property name="ParticleSpinDirectionY" value="CCW"/><property name="ParticleSpinDirectionZ" value="CCW"/>
        </emitter></emitters><behaviour_chains><behaviour_chain id="pose"><quality value="0">
        <modifier fragment="vs_translation"><property name="restriction" value="{restriction}"/></modifier>
        <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/control.dds"/></modifier>
        </quality></behaviour_chain></behaviour_chains></vfx>
        """));
}
