#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelBirthOrientationTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void NativePerAxisInterpolationIsRestrictedToModelBirth(int kind)
    {
        var minimum = new Vector3(-10, 20, 60); var maximum = new Vector3(90, -80, 120);
        var sampled = new Vector3(15, -5, 75);
        var result = VfxModelBirthOrientation.Apply(kind, sampled, minimum, maximum, new(.1f, .7f, .9f));
        var expected = kind == 1 ? new Vector3(15, 20 + (-80 - 20) * .7f, 60 + (120 - 60) * .9f) : sampled;
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    public void DegenerateNativeRangesRetainEveryPreviouslySampledBit(int mask)
    {
        var minimum = new Vector3(-0f, 20, 60);
        var maximum = minimum + new Vector3((mask & 1) != 0 ? 120 : 0, (mask & 2) != 0 ? 360 : 0, (mask & 4) != 0 ? 60 : 0);
        var sampled = Vector3.Lerp(minimum, maximum, .27f);
        var result = VfxModelBirthOrientation.Apply(1, sampled, minimum, maximum, new(.1f, .7f, .9f));
        for (var axis = 0; axis < 3; axis++)
            Assert.That(BitConverter.SingleToInt32Bits(Component(result, axis)), Is.EqualTo(BitConverter.SingleToInt32Bits(Component(sampled, axis))));
    }

    [TestCase(3)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    public void ModelBirthSamplesChangingAxesIndependentlyWithoutChangingOtherRandomFields(int mask)
    {
        var minimum = new Vector3(11, -23, 61);
        var maximum = minimum + new Vector3((mask & 1) != 0 ? 120 : 0, (mask & 2) != 0 ? 360 : 0, (mask & 4) != 0 ? 60 : 0);
        var conversion = VfxModelVariantTests.Convert(Source(minimum, maximum));
        var fixedConversion = VfxModelVariantTests.Convert(Source(minimum, minimum));
        var actual = new TotalWarVfxSimulation(conversion); actual.Seek(.1f, Matrix.Identity);
        var control = new TotalWarVfxSimulation(fixedConversion); control.Seek(.1f, Matrix.Identity);
        var particles = Particles(actual); var controls = Particles(control);
        Assert.That(particles, Has.Length.EqualTo(controls.Length));
        Assert.That(particles.Length, Is.GreaterThan(1));
        for (var index = 0; index < particles.Length; index++)
        {
            var particle = particles[index]; var reference = controls[index];
            var orientation = Value<Vector3>(particle, "Orientation");
            var reserved = Value<Vector3>(particle, "RandomScale");
            var firstAxis = Enumerable.Range(0, 3).First(axis => Component(maximum - minimum, axis) != 0);
            var first = (Component(orientation, firstAxis) - Component(minimum, firstAxis)) / Component(maximum - minimum, firstAxis);
            var samples = new[] { first, reserved.Y, reserved.Z }; var sampleIndex = 0;
            var expected = minimum;
            for (var axis = 0; axis < 3; axis++)
            {
                var delta = Component(maximum - minimum, axis);
                if (delta != 0) Set(ref expected, axis, Component(minimum, axis) + delta * samples[sampleIndex++]);
            }
            Assert.That(Vector3.Distance(orientation, expected), Is.LessThan(.00001f), $"particle {index}");
            foreach (var field in new[] { "Birth", "Life", "Position", "Velocity", "InitialScale", "InitialAlpha", "RandomScale", "RandomColour", "RandomAlpha", "ModelVariant", "Cell", "Row", "Loops" })
                Assert.That(Value<object>(particle, field), Is.EqualTo(Value<object>(reference, field)), field);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    public void FixedAndSingleAxisBirthsRetainTheirExistingExactResult(int mask)
    {
        var minimum = new Vector3(11, -23, 61);
        var maximum = minimum + new Vector3((mask & 1) != 0 ? 120 : 0, (mask & 2) != 0 ? 360 : 0, (mask & 4) != 0 ? 60 : 0);
        var conversion = VfxModelVariantTests.Convert(Source(minimum, maximum));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.Identity);
        foreach (var particle in Particles(simulation))
        {
            var orientation = Value<Vector3>(particle, "Orientation");
            if (mask == 0) Assert.That(orientation, Is.EqualTo(minimum));
            for (var axis = 0; axis < 3; axis++)
                if ((mask & (1 << axis)) == 0) Assert.That(Component(orientation, axis), Is.EqualTo(Component(minimum, axis)));
        }
    }

    [Test]
    public void CompleteOfficialWeaponRootUsesIndependentAxesAndRetainsOriginalParameters()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Official game data was not explicitly provided.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(parts => parts.Length > 1)
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx_models.pack", "vfx_models_dds.pack", "data.pack", "vfx.pack", "commontextures.pack" })
        {
            var path = Path.Combine(data!, name); Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var file in new PackFileContainerLoader(null!).Load(path)!.FileList) files[file.Key.Replace('/', '\\')] = file.Value;
        }
        var captured = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? Read(string path)
        {
            var key = path.Replace('/', '\\');
            if (!files.TryGetValue(key, out var file)) return null;
            var bytes = file.DataSource.ReadData(); captured[key] = bytes; return bytes;
        }
        const string root = @"vfx\wh3_dlc24_cth_jade_dragon_human_weapon_trail.xml";
        var original = Read(root)!; var conversion = TotalWarVfxConverter.Convert(new(original, root, Read));
        var target = conversion.Emitters.Single(emitter => emitter.Name == "gems6");
        Assert.That(target.InitialScale.Vector(0, Vector3.Zero), Is.EqualTo(new Vector3(.018f, .022f, .018f)));
        Assert.That(target.Life.Range("x", 0), Is.EqualTo(new VfxRange(50, 50)));
        Assert.That(target.MaximumParticles, Is.EqualTo(30)); Assert.That(target.SimulationSpace, Is.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.Identity);
        var particles = Particles(simulation).Where(particle => ReferenceEquals(Value<object>(Value<object>(particle, "Emitter"), "Definition"), target)).ToArray();
        Assert.That(particles, Has.Length.EqualTo(30));
        foreach (var particle in particles)
        {
            var orientation = Value<Vector3>(particle, "Orientation");
            var random = Value<Vector3>(particle, "RandomScale");
            Assert.That(orientation.X, Is.Zero);
            Assert.That(orientation.Z, Is.EqualTo(60 + (120 - 60) * random.Y));
            Assert.That(Value<float>(particle, "Life"), Is.EqualTo(10));
        }
        Assert.That(particles.Any(particle => MathF.Abs(Value<Vector3>(particle, "Orientation").Y / 360 - Value<Vector3>(particle, "RandomScale").Y) > .1f), Is.True);
        foreach (var entry in captured) Assert.That(files[entry.Key].DataSource.ReadData(), Is.EqualTo(entry.Value));
        Assert.That(VfxDocument.Read(original).Write(), Is.EqualTo(original));
    }

    private static byte[] Source(Vector3 minimum, Vector3 maximum)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(2)); var emitter = document.Root.Descendants("emitter").Single();
        void Vector(string name, Vector3 value) => emitter.Add(new XElement("property", new XAttribute("name", name),
            new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z)));
        Vector("ParticleOrientationMin", minimum); Vector("ParticleOrientationMax", maximum);
        emitter.Elements().Where(item => (string?)item.Attribute("name") is "EmissionRateCurve" or "MaximumParticles").Remove();
        emitter.Add(new XElement("property", new XAttribute("name", "EmissionRateCurve"), VfxLibraryVisibilityTests.Constant("x", 500)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 30)),
            new XElement("property", new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", "LOCAL_SPACE_POSITION_ROTATION")));
        return document.Write();
    }

    private static object[] Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Value<object>(simulation, "_particles")).Cast<object>().ToArray();
    private static T Value<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static float Component(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    private static void Set(ref Vector3 value, int axis, float number) { if (axis == 0) value.X = number; else if (axis == 1) value.Y = number; else value.Z = number; }
}
