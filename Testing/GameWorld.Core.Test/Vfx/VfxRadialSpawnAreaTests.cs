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

public class VfxRadialSpawnAreaTests
{
    // Actual original DLL 6ca8d0 outputs, scaled to the controlled .5 birth radius.
    [TestCase(0, 0, .9999998807907104f, 0, 0)]
    [TestCase(0, -5, .9961946606636047f, 0, .08715572953224182f)]
    [TestCase(0, 5, .9961946606636047f, 0, -.08715572953224182f)]
    [TestCase(0, 90, -8.74227694680485e-8f, 0, -.9999998807907104f)]
    [TestCase(0, -90, 0, 0, .9999998807907104f)]
    [TestCase(90, 0, -8.74227694680485e-8f, .9999999403953552f, 0)]
    [TestCase(-90, 0, 0, -.9999999403953552f, 0)]
    [TestCase(30, 45, .6123723983764648f, .5f, -.6123723983764648f)]
    public void ExplicitBirthAngles_MatchActualNativeHelperAxesAndSigns(float angle, float arc, float x, float y, float z)
    {
        var bytes = Source(angle, angle, arc, arc);
        var simulation = new TotalWarVfxSimulation(Convert(bytes));
        simulation.Seek(.2f, Matrix.Identity);
        var births = Births(simulation);
        Assert.That(births, Has.Length.GreaterThan(10));
        var native = new Vector3(x, y, z) * .5f;
        foreach (var birth in births)
        {
            Assert.That(Vector3.Distance(birth.Position, native), Is.LessThan(.000001f),
                "SpawnAngle/SpawnArc determine the native birth position before SpreadMethod selects velocity.");
            Assert.That(Vector3.Distance(Vector3.Normalize(birth.Velocity), Vector3.Normalize(native)), Is.LessThan(.000001f));
        }
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void NarrowArc_ConcentratesBirthsAroundPositiveXWithoutChangingCountLifeAlphaOrLaterRandomFields()
    {
        var bytes = Source(0, 0, -5, 5);
        var sourceWithoutArc = VfxDocument.Read(bytes);
        Property(sourceWithoutArc, "SpawnArc").Remove();
        var simulation = new TotalWarVfxSimulation(Convert(bytes));
        var oldControl = new TotalWarVfxSimulation(Convert(sourceWithoutArc.Write()));
        simulation.Seek(.2f, Matrix.Identity); oldControl.Seek(.2f, Matrix.Identity);
        var births = Births(simulation); var controls = Births(oldControl);
        Assert.That(births.Length, Is.EqualTo(controls.Length).And.GreaterThan(10));
        Assert.That(births.Select(RandomFields), Is.EqualTo(controls.Select(RandomFields)),
            "Birth-position correction must preserve the existing number of random calls and unrelated particle fields.");
        foreach (var birth in births)
        {
            Assert.That(birth.Position.Y, Is.EqualTo(0));
            Assert.That(birth.Position.X, Is.GreaterThanOrEqualTo(.5f * MathF.Cos(MathHelper.ToRadians(5)) - .000001f));
            Assert.That(Math.Abs(MathHelper.ToDegrees(MathF.Atan2(-birth.Position.Z, birth.Position.X))), Is.LessThanOrEqualTo(5.00001f));
        }
        Assert.That(births.Any(birth => birth.Position.Z < 0) && births.Any(birth => birth.Position.Z > 0), Is.True);
        Assert.That(controls.Any(birth => birth.Position.X < 0), Is.True,
            "The no-SpawnArc compatibility control retains its existing full-circle candidate.");
    }

    [Test]
    public void NumericZero_DoesNotAcquireTheLiteralFalseBirthAreaRule()
    {
        var document = VfxDocument.Read(Source(0, 0, -5, 5));
        Property(document, "SpreadMethod").SetAttributeValue("value", "0");
        var withArc = new TotalWarVfxSimulation(Convert(document.Write()));
        Property(document, "SpawnArc").Remove();
        var withoutArc = new TotalWarVfxSimulation(Convert(document.Write()));
        withArc.Seek(.2f, Matrix.Identity); withoutArc.Seek(.2f, Matrix.Identity);
        Assert.That(Births(withArc), Is.EqualTo(Births(withoutArc)));
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY")]
    public void DistortionZeroAngles_KeepTheNativePointBirthAndExistingPositiveXVelocity(string space)
    {
        var document = VfxDocument.Read(Source(0, 0, 0, 0));
        Property(document, "SpawnAngle").Remove();
        document.Root.Descendants("emitter").Single().Add(
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", "0"), new XAttribute("max", "0")),
            new XElement("ranged_property", new XAttribute("name", "SpreadArc"), new XAttribute("min", "0"), new XAttribute("max", "0")),
            new XElement("property", new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", space)));
        document.Root.Descendants("effect_render_stack").Single().Elements("property").Single().SetAttributeValue("value", "DISTORTION");
        var bytes = document.Write(); var conversion = Convert(bytes); var definition = conversion.Emitters.Single();
        Assert.That(definition.LegacySpreadSpawn, Is.True);
        Assert.That(definition.RadialSpawnDirection, Is.False, "A birth-area correction must not replace the existing distortion velocity algorithm.");
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        foreach (var birth in Births(simulation))
        {
            Assert.That(Vector3.Distance(birth.Position, new Vector3(.5f, 0, 0)), Is.LessThan(.000001f));
            Assert.That(birth.Velocity, Is.EqualTo(new Vector3(20, 0, 0)));
        }
        Assert.That(document.Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void ExplicitAngularArea_UsesNativeLinearRadiusInsteadOfAnAreaWeightedDisk()
    {
        var document = VfxDocument.Read(Source(0, 0, 0, 0));
        Property(document, "SpawnAreaCurve").Value = Radius(.2f, 1);
        Property(document, "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 1000);
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        simulation.Seek(.5f, Matrix.Identity);
        var births = Births(simulation);
        Assert.That(births, Has.Length.GreaterThan(300));
        Assert.That(births.All(birth => birth.Position.X is >= .2f and <= 1 && Math.Abs(birth.Position.Z) < .000001f), Is.True);
        Assert.That(births.Average(birth => birth.Position.Length()), Is.InRange(.57, .63),
            "Native 6c0548..0550 interpolates radius linearly; the old sqrt-weighted disk has a mean near .689.");
    }

    [Test]
    public void CompleteOfficialArc90_WhiteBirthsStayInsideTheAuthoredTenDegreeForwardSector()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original game data is required for the unchanged complete official root.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" })
        {
            var path = Path.Combine(data!, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var entry in new PackFileContainerLoader(null!).Load(path)!.FileList) files[entry.Key.Replace('/', '\\')] = entry.Value;
        }
        const string rootPath = @"vfx\wh_main_ability_melee_slash_arc90.xml";
        const string libraryPath = @"vfx\wh_main_lib_melee.xml";
        var source = files[rootPath].DataSource.ReadData(); var library = files[libraryPath].DataSource.ReadData();
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo("087b6329484eb6ad30f35208115ba39c1cecff4a17db243baeea266ded5884a6"));
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(library)), Is.EqualTo("1929ba9fde0b377bbf69deee319646917cbe0c898c604254c69cdffa739a19ee"));
        var conversion = TotalWarVfxConverter.Convert(new(source, rootPath, path => files.GetValueOrDefault(path.Replace('/', '\\'))?.DataSource.ReadData()));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var allBirths = Births(simulation);
        var births = allBirths.Where(birth => birth.Name == "Arc_white").ToArray();
        Assert.That(births, Has.Length.EqualTo(6));
        foreach (var birth in births)
        {
            Assert.That(birth.Position.Y, Is.EqualTo(0));
            Assert.That(birth.Position.X, Is.GreaterThanOrEqualTo(.5f * MathF.Cos(MathHelper.ToRadians(5)) - .000001f));
            Assert.That(Math.Abs(MathHelper.ToDegrees(MathF.Atan2(-birth.Position.Z, birth.Position.X))), Is.LessThanOrEqualTo(5.00001f));
            Assert.That(birth.Life, Is.InRange(.4f, .5f));
        }
        var distortion = allBirths.Where(birth => birth.Name == "Arc_distortion").ToArray();
        Assert.That(distortion, Has.Length.EqualTo(1));
        // The original emitter adds its authored Y=.1 offset to the angular birth point.
        Assert.That(Vector3.Distance(distortion[0].Position, new Vector3(.5f, .1f, 0)), Is.LessThan(.000001f));
        Assert.That(distortion[0].Velocity, Is.EqualTo(new Vector3(50, 0, 0)));
        Assert.That(distortion[0].Life, Is.EqualTo(.8f));
        Assert.That(conversion.Emitters.Single(emitter => emitter.Name == "Arc_distortion").RadialSpawnDirection, Is.False);
        Assert.That(files[rootPath].DataSource.ReadData(), Is.EqualTo(source)); Assert.That(files[libraryPath].DataSource.ReadData(), Is.EqualTo(library));
    }

    private static byte[] Source(float angleMin, float angleMax, float arcMin, float arcMax)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        Property(document, "InfiniteLife").SetAttributeValue("value", "false");
        Property(document, "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 128);
        Property(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 3);
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", "1"), new XAttribute("max", "1")),
            new XElement("property", new XAttribute("name", "SpreadMethod"), new XAttribute("value", "false")),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", 20)),
            new XElement("property", new XAttribute("name", "SpawnAreaCurve"), Radius(.5f, .5f)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", angleMin), new XAttribute("y", angleMax)),
            new XElement("property", new XAttribute("name", "SpawnArc"), new XAttribute("x", arcMin), new XAttribute("y", arcMax)));
        document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DEFERRED_DECAL")))));
        return document.Write();
    }
    private static string Radius(float inner, float outer) => VfxLibraryVisibilityTests.Constant("inner_radius", inner).Split(':')[0] + "|"
        + VfxLibraryVisibilityTests.Constant("outer_radius", outer).Split(':')[0] + ":inner_radius,0;0,0|outer_radius,0;1,1";
    private static XElement Property(VfxDocument document, string name) => document.Root.Descendants("emitter").Single().Elements().Single(node => (string?)node.Attribute("name") == name);
    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/radial-area-diagnostic.xml", _ => [1]));
    private static object RandomFields(Birth birth) => (birth.Name, birth.Time, birth.Life, birth.RandomAlpha, birth.InitialScale, birth.Orientation);
    private static Birth[] Births(TotalWarVfxSimulation simulation) => ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(simulation)!).Cast<object>().Select(particle =>
        {
            object Field(string name) => particle.GetType().GetField(name)!.GetValue(particle)!;
            var state = Field("Emitter"); var definition = (VfxPreviewEmitter)state.GetType().GetField("Definition")!.GetValue(state)!;
            return new Birth(definition.Name, (Vector3)Field("MotionOrigin"), (Vector3)Field("MotionInitialVelocity"), (double)Field("Birth"), (float)Field("Life"),
                (float)Field("RandomAlpha"), (Vector3)Field("InitialScale"), (Vector3)Field("Orientation"));
        }).ToArray();
    private sealed record Birth(string Name, Vector3 Position, Vector3 Velocity, double Time, float Life, float RandomAlpha, Vector3 InitialScale, Vector3 Orientation);
}
