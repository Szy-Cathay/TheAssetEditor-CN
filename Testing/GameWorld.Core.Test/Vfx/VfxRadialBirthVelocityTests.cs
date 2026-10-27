#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxRadialBirthVelocityTests
{
    [TestCase(0f, -.5f, 0f)]
    [TestCase(3f, -.5f, -2f)]
    public void ExplicitFalse_UsesTheBirthOffsetForRadialVelocityWithoutShiftingOtherRandomFields(float x, float y, float z)
    {
        var origin = new Vector3(x, y, z);
        var source = RadialSource(false, 1, origin);
        var simulation = new TotalWarVfxSimulation(Convert(source));
        simulation.Seek(.15f, Matrix.Identity);
        var births = Births(simulation);
        AssertRadial(births, origin);
        var previousPath = new TotalWarVfxSimulation(Convert(RadialSource(null, 1, origin)));
        previousPath.Seek(.15f, Matrix.Identity);
        Assert.That(births.Select(p => (p.Position, p.Life, p.BirthTime, p.RandomAlpha, p.InitialScale, p.Orientation)),
            Is.EqualTo(Births(previousPath).Select(p => (p.Position, p.Life, p.BirthTime, p.RandomAlpha, p.InitialScale, p.Orientation))),
            "Changing direction must not consume different random samples for later births or unrelated fields.");
        var incremental = new TotalWarVfxSimulation(Convert(source));
        incremental.Advance(.05f, Matrix.Identity);
        incremental.Advance(.1f, Matrix.Identity);
        Assert.That(Births(incremental), Is.EqualTo(births));
        simulation.Seek(.025f, Matrix.Identity);
        simulation.Seek(.15f, Matrix.Identity);
        Assert.That(Births(simulation), Is.EqualTo(births));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void ExplicitFalse_ZeroRadiusKeepsTheNativeZeroDirectionInsteadOfInventingUp()
    {
        var source = RadialSource(false, 0, new(3, -.5f, -2));
        var simulation = new TotalWarVfxSimulation(Convert(source));
        simulation.Seek(.15f, Matrix.Identity);
        var births = Births(simulation);
        Assert.That(births.Length, Is.GreaterThan(20));
        foreach (var birth in births)
        {
            Assert.That(birth.Position, Is.EqualTo(new Vector3(3, -.5f, -2)));
            Assert.That(birth.Velocity, Is.EqualTo(Vector3.Zero),
                "Native 6c0bd5..0c4f leaves a zero difference unchanged; it does not restore the initially written Up.");
        }
    }

    [TestCase(true)]
    [TestCase(null)]
    public void TrueOrMissingSpreadMethod_LeavesTheExistingAnglePathUnchanged(bool? spreadMethod)
    {
        var simulation = new TotalWarVfxSimulation(Convert(RadialSource(spreadMethod, 1, Vector3.Zero)));
        simulation.Seek(.15f, Matrix.Identity);
        var births = Births(simulation);
        Assert.That(births.Length, Is.GreaterThan(20));
        foreach (var birth in births)
        {
            Assert.That(birth.Velocity.Z, Is.Zero);
            Assert.That(birth.Velocity.X, Is.GreaterThan(0));
            Assert.That(birth.Velocity.Y, Is.GreaterThan(0));
            Assert.That(birth.Velocity.Length(), Is.EqualTo(10).Within(.00001f));
        }
        // Compatibility control only: this does not certify unclosed native default/angle semantics.
    }

    [TestCase(0f)]
    [TestCase(1f)]
    public void NumericZeroAndLiteralFalse_KeepTheirDistinctCalibratedBirthDirections(float radius)
    {
        var literalFalse = RadialSource(false, radius, Vector3.Zero);
        var numericDocument = VfxDocument.Read(literalFalse);
        numericDocument.Root.Descendants("emitter").Single().Elements("property")
            .Single(node => (string?)node.Attribute("name") == "SpreadMethod").SetAttributeValue("value", "0");
        var numericZero = numericDocument.Write();
        var falseConversion = Convert(literalFalse);
        var numericConversion = Convert(numericZero);
        Assert.Multiple(() =>
        {
            Assert.That(falseConversion.Emitters.Single().RadialSpawnDirection, Is.True);
            Assert.That(numericConversion.Emitters.Single().RadialSpawnDirection, Is.False,
                "The native literal-false contract must not override the numeric-zero angular trajectory controls.");
        });
        var falseSimulation = new TotalWarVfxSimulation(falseConversion);
        var numericSimulation = new TotalWarVfxSimulation(numericConversion);
        var angleControl = new TotalWarVfxSimulation(Convert(RadialSource(null, radius, Vector3.Zero)));
        falseSimulation.Seek(.15f, Matrix.Identity);
        numericSimulation.Seek(.15f, Matrix.Identity);
        angleControl.Seek(.15f, Matrix.Identity);
        var falseBirths = Births(falseSimulation);
        var numericBirths = Births(numericSimulation);
        Assert.That(numericBirths, Is.EqualTo(Births(angleControl)),
            "Changing only the literal to numeric zero must retain the existing angular path and random samples.");
        Assert.That(numericBirths.Select(p => (p.Position, p.Life, p.BirthTime, p.RandomAlpha, p.InitialScale, p.Orientation)),
            Is.EqualTo(falseBirths.Select(p => (p.Position, p.Life, p.BirthTime, p.RandomAlpha, p.InitialScale, p.Orientation))));
        foreach (var birth in numericBirths)
        {
            Assert.That(birth.Velocity.Y, Is.GreaterThan(0));
            Assert.That(birth.Velocity.Length(), Is.EqualTo(10).Within(.00001f));
        }
        if (radius == 0)
            Assert.That(falseBirths.All(birth => birth.Velocity == Vector3.Zero), Is.True,
                "Literal false with no birth offset retains native zero velocity.");
        else AssertRadial(falseBirths, Vector3.Zero);
        Assert.That(VfxDocument.Read(numericZero).Write(), Is.EqualTo(numericZero));
        Assert.That(VfxDocument.Read(literalFalse).Write(), Is.EqualTo(literalFalse));
    }

    [Test]
    public void CompleteOfficialRockImpact_DustRingBirthVelocityFollowsItsAuthoredFootprint()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original game data is required for the complete official CPU control.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1 && parts[0].EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        var loader = new PackFileContainerLoader(null!);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" })
        {
            var path = Path.Combine(data!, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            var pack = loader.Load(path)!;
            foreach (var entry in pack.FileList) files[entry.Key.Replace('/', '\\')] = entry.Value;
        }
        const string rootPath = @"vfx\wh_main_impact_vlarge_rock.xml";
        const string libraryPath = @"vfx\wh_main_lib_terrain_impact.xml";
        var source = files[rootPath].DataSource.ReadData();
        var library = files[libraryPath].DataSource.ReadData();
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo("275489431e4c681eef7f4acec99b4c530e9e41211887828bb7509ec1d19f6d81"));
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(library)), Is.EqualTo("4dca91a2ad3fca5fdec5de489f46355e377fbd47183acdcdf8c34b26b54a126d"));
        var conversion = TotalWarVfxConverter.Convert(new(source, rootPath,
            path => files.GetValueOrDefault(path.Replace('/', '\\'))?.DataSource.ReadData()));
        var dust = conversion.Emitters.Single(emitter => emitter.Name == "dust_ring");
        Assert.That(dust.Kind, Is.Zero);
        Assert.That(dust.ConstantForceDamping, Is.Null,
            "Read birth velocity before the official gravity and dynamic drag alter it; MotionOrigin must remain the birth record.");
        var simulation = new TotalWarVfxSimulation(conversion);
        // The unchanged burst starts at .1.. .15 seconds and emits for .1 seconds.
        simulation.Seek(.4f, Matrix.Identity);
        AssertRadial(Births(simulation).Where(particle => particle.Name == "dust_ring").ToArray(), new(0, -.5f, 0));
        Assert.That(files[rootPath].DataSource.ReadData(), Is.EqualTo(source));
        Assert.That(files[libraryPath].DataSource.ReadData(), Is.EqualTo(library));
    }

    private static void AssertRadial(Birth[] births, Vector3 emitterPosition)
    {
        Assert.That(births.Length, Is.GreaterThan(20));
        foreach (var birth in births)
        {
            var difference = birth.Position - emitterPosition;
            Assert.That(difference.LengthSquared(), Is.GreaterThan(.000001f));
            Assert.That(birth.Velocity.LengthSquared(), Is.GreaterThan(.000001f));
            Assert.That(Math.Abs(birth.Velocity.Y), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(Vector3.Normalize(birth.Velocity), Vector3.Normalize(difference)), Is.LessThan(.00001f),
                "Actual 6c1ec0 passes radial direction times initial speed through writer argument6, independently of orientation normal argument7.");
        }
        Assert.That(births.Any(p => p.Velocity.X < 0) && births.Any(p => p.Velocity.X > 0)
            && births.Any(p => p.Velocity.Z < 0) && births.Any(p => p.Velocity.Z > 0), Is.True,
            "The complete birth ring must retain all four horizontal directions.");
    }

    private static VfxConversion Convert(byte[] bytes)
    {
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/radial-birth-diagnostic.xml", _ => [1]));
        Assert.That(result.Emitters, Has.Count.EqualTo(1));
        return result;
    }

    private static byte[] RadialSource(bool? spreadMethod, float radius, Vector3 position)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Single(node => (string?)node.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 400);
        emitter.Elements().Single(node => (string?)node.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        emitter.Add(new XElement("property", new XAttribute("name", "EmitterPosition"), VectorCurve(position)),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", 10)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 20), new XAttribute("y", 85)),
            new XElement("property", new XAttribute("name", "SpawnAreaCurve"),
                VfxLibraryVisibilityTests.Constant("inner_radius", radius == 0 ? 0 : .3f).Split(':')[0] + "|"
                + VfxLibraryVisibilityTests.Constant("outer_radius", radius).Split(':')[0] + ":inner_radius,0;0,0|outer_radius,0;1,1"));
        if (spreadMethod.HasValue)
            emitter.Add(new XElement("property", new XAttribute("name", "SpreadMethod"), new XAttribute("value", spreadMethod.Value)));
        return document.Write();
    }

    private static string VectorCurve(Vector3 value) => string.Join('|', new[] { ("x", value.X), ("y", value.Y), ("z", value.Z) }
        .Select(pair => VfxLibraryVisibilityTests.Constant(pair.Item1, pair.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";

    private static Birth[] Births(TotalWarVfxSimulation simulation) => ((IEnumerable)typeof(TotalWarVfxSimulation)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!).Cast<object>()
        .Select(particle =>
        {
            object Field(string name) => particle.GetType().GetField(name)!.GetValue(particle)!;
            var emitter = Field("Emitter");
            var definition = (VfxPreviewEmitter)emitter.GetType().GetField("Definition")!.GetValue(emitter)!;
            return new Birth(definition.Name, (Vector3)Field("MotionOrigin"), (Vector3)Field("MotionInitialVelocity"),
                (float)Field("Life"), (double)Field("Birth"), (float)Field("RandomAlpha"),
                (Vector3)Field("InitialScale"), (Vector3)Field("Orientation"));
        }).ToArray();

    private sealed record Birth(string Name, Vector3 Position, Vector3 Velocity, float Life, double BirthTime,
        float RandomAlpha, Vector3 InitialScale, Vector3 Orientation);
}
