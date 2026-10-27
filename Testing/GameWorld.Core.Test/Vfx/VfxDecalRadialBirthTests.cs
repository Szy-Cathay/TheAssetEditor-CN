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

public class VfxDecalRadialBirthTests
{
    [TestCase(false, "WORLD_SPACE")]
    [TestCase(true, "WORLD_SPACE")]
    [TestCase(false, "LOCAL_SPACE_POSITION_ONLY")]
    [TestCase(true, "LOCAL_SPACE_POSITION_ONLY")]
    public void ProjectedContinuousMesh_UsesRadialBirthDirectionAndSamplesSpeedOnlyAtBirth(bool dynamicSpeed, string space)
    {
        var bytes = Source(dynamicSpeed, space);
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters.Single().Kind, Is.EqualTo(2));
        Assert.That(conversion.Emitters.Single().RadialSpawnDirection, Is.True);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.25f, Matrix.Identity);
        var births = Births(simulation);
        Assert.That(births.Length, Is.GreaterThan(20));
        foreach (var birth in births)
        {
            Assert.That(birth.Velocity.Y, Is.EqualTo(0).Within(.00001f));
            Assert.That(Vector3.Distance(Vector3.Normalize(birth.Velocity), Vector3.Normalize(birth.Position)), Is.LessThan(.00001f));
            var expectedSpeed = dynamicSpeed ? 24 - 14 * birth.Time / .4f : 24;
            Assert.That(birth.Velocity.Length(), Is.EqualTo(expectedSpeed).Within(.0001),
                "Native 6c2080 samples the birth-speed scalar, then multiplies each direction component before writer argument 6.");
            Assert.That(birth.Life, Is.EqualTo(2));
        }
        var savedFirst = births[0];
        simulation.Seek(.35f, Matrix.Identity);
        Assert.That(Births(simulation).Single(birth => birth.Time == savedFirst.Time), Is.EqualTo(savedFirst),
            "An older particle must retain its initial velocity when the emitter speed curve advances.");

        var numeric = VfxDocument.Read(bytes);
        Property(numeric, "SpreadMethod").SetAttributeValue("value", "0");
        var control = new TotalWarVfxSimulation(Convert(numeric.Write()));
        control.Seek(.25f, Matrix.Identity);
        Assert.That(births.Select(BirthFields), Is.EqualTo(Births(control).Select(BirthFields)),
            "Direction selection cannot consume different random samples or alter birth, lifetime, alpha, scale or orientation.");
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("numeric-zero")]
    [TestCase("emissive-pass")]
    [TestCase("full-local")]
    [TestCase("ordinary-dynamic")]
    [TestCase("spacing")]
    [TestCase("inherited-speed")]
    [TestCase("rotated-emitter")]
    public void UnclosedBirthCombinations_DoNotEnterTheNewDecalPath(string variant)
    {
        var document = VfxDocument.Read(Source(true, "LOCAL_SPACE_POSITION_ONLY"));
        switch (variant)
        {
            case "numeric-zero": Property(document, "SpreadMethod").SetAttributeValue("value", "0"); break;
            case "emissive-pass": document.Root.Descendants("property").Single(node => (string?)node.Attribute("name") == "scene_view_type")
                    .SetAttributeValue("value", "DEFERRED_DECAL_EMISSIVE"); break;
            case "full-local": Property(document, "ParticleSimulationSpace").SetAttributeValue("value", "LOCAL_SPACE_POSITION_ROTATION"); break;
            case "ordinary-dynamic": document.Root.Element("vfx")!.Element("effect_render_stacks")!.Remove();
                Property(document, "ParticleSimulationSpace").SetAttributeValue("value", "WORLD_SPACE"); break;
            case "spacing": Add(document, "SpawnSpacing", "1"); break;
            case "inherited-speed": Add(document, "SpawnInheritedSpeed", "1"); break;
            case "rotated-emitter": Property(document, "EmitterOrientation").Value = VectorCurve(new(0, 15, 0)); break;
        }
        var bytes = document.Write();
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters.Single().RadialSpawnDirection, Is.False);
        Assert.That(document.Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void CompleteOfficialArc90_KeepsItsAuthoredLifetimeAlphaAndFootprintInsteadOfLeavingTheReceiver()
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
            foreach (var entry in loader.Load(path)!.FileList) files[entry.Key.Replace('/', '\\')] = entry.Value;
        }
        const string rootPath = @"vfx\wh_main_ability_melee_slash_arc90.xml";
        const string libraryPath = @"vfx\wh_main_lib_melee.xml";
        var source = files[rootPath].DataSource.ReadData();
        var library = files[libraryPath].DataSource.ReadData();
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo("087b6329484eb6ad30f35208115ba39c1cecff4a17db243baeea266ded5884a6"));
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(library)), Is.EqualTo("1929ba9fde0b377bbf69deee319646917cbe0c898c604254c69cdffa739a19ee"));
        var libraryOriginal = VfxDocument.Read(library);
        var originalEmitter = libraryOriginal.Root.Element("vfx")!.Element("emitters")!.Elements("emitter")
            .Single(node => (string?)node.Attribute("id") == "Arc_white");
        var originalLife = new VfxPreviewCurve(originalEmitter.Elements("property").Single(node => (string?)node.Attribute("name") == "ParticleLifeTime").Value,
            "ParticleLifeTime", 1);
        var originalAlpha = new VfxPreviewCurve(libraryOriginal.Root.Descendants("behaviour_chain")
            .Single(node => (string?)node.Attribute("id") == "Arc_white").Elements("quality").Single(node => (string?)node.Attribute("value") == "0")
            .Elements("modifier").Single(node => (string?)node.Attribute("fragment") == "vs_vertex_alpha").Elements("property")
            .Single(node => (string?)node.Attribute("name") == "curve_alpha").Value, "curve_alpha", 1);
        var conversion = TotalWarVfxConverter.Convert(new(source, rootPath,
            path => files.GetValueOrDefault(path.Replace('/', '\\'))?.DataSource.ReadData()));
        var decal = conversion.Emitters.Single(emitter => emitter.Name == "Arc_white");
        Assert.That(decal.Kind, Is.EqualTo(2));
        Assert.That(decal.RadialSpawnDirection, Is.True);
        Assert.That(decal.Speed.IsConstant, Is.False);
        Assert.That(decal.SpawnOnTerrain, Is.False, "This repair must not force a terrain clamp through the unrelated receiver-normal path.");
        foreach (var phase in new[] { 0d, .1, .2, .5, .9, 1 })
            foreach (var random in new[] { 0f, .5f, 1 })
            {
                Assert.That(decal.Life.Sample("x", phase, random), Is.EqualTo(originalLife.Sample("x", phase, random)));
                Assert.That(decal.AlphaAt(phase, random), Is.EqualTo(originalAlpha.Sample("alpha", phase, random)));
            }
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.Identity);
        var births = Births(simulation).Where(birth => birth.Name == "Arc_white").ToArray();
        Assert.That(births.Length, Is.GreaterThan(0));
        foreach (var birth in births)
        {
            Assert.That(birth.Velocity.Y, Is.EqualTo(0).Within(.00001f));
            Assert.That(Vector3.Distance(Vector3.Normalize(birth.Velocity), Vector3.Normalize(birth.Position)), Is.LessThan(.00001f));
            Assert.That(birth.Life, Is.InRange(.4f, .5f));
        }
        Assert.That(decal.SpawnAreaArc, Is.EqualTo(new VfxRange(-5, 5)));
        Assert.That(decal.SpawnAreaAngle, Is.EqualTo(new VfxRange(0, 0)));
        Assert.That(conversion.Diagnostics.Any(diagnostic => diagnostic.Field == "SpawnArc" && diagnostic.ReasonKey == "Vfx.Preview.SpawnCandidate"), Is.True,
            "Native angular bounds are mapped, while the existing candidate diagnostic retains the visual/random-sequence boundary.");
        Assert.That(files[rootPath].DataSource.ReadData(), Is.EqualTo(source));
        Assert.That(files[libraryPath].DataSource.ReadData(), Is.EqualTo(library));
    }

    private static byte[] Source(bool dynamicSpeed, string space)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        Property(document, "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 128);
        Property(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        Property(document, "InfiniteLife").SetAttributeValue("value", "false");
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", ".4"), new XAttribute("max", ".4")),
            new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(Vector3.Zero)),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), dynamicSpeed
                ? "x_min,0,0;0,24,linear,linear,0,0,0,0,1;1,10,linear,linear,0,0,0,0,1:x,0;0,0" : VfxLibraryVisibilityTests.Constant("x", 24)),
            new XElement("property", new XAttribute("name", "SpawnAreaCurve"),
                "inner_radius_min,0,0;0,.5,linear,linear,0,0,0,0,1|outer_radius_min,0,0;0,.5,linear,linear,0,0,0,0,1:inner_radius,0;0,0|outer_radius,0;1,1"));
        Add(document, "SpreadMethod", "false"); Add(document, "ParticleSimulationSpace", space);
        document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DEFERRED_DECAL")))));
        return document.Write();
    }

    private static XElement Property(VfxDocument document, string name) => document.Root.Descendants("emitter").Single().Elements()
        .Single(node => (string?)node.Attribute("name") == name);
    private static void Add(VfxDocument document, string name, string value) => document.Root.Descendants("emitter").Single()
        .Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    private static string VectorCurve(Vector3 value) => string.Join('|', new[] { ("x", value.X), ("y", value.Y), ("z", value.Z) }
        .Select(pair => VfxLibraryVisibilityTests.Constant(pair.Item1, pair.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/decal-radial-control.xml", _ => [1]));
    private static object BirthFields(Birth birth) => (birth.Name, birth.Position, birth.Life, birth.Time, birth.RandomAlpha, birth.InitialScale, birth.Orientation);
    private static Birth[] Births(TotalWarVfxSimulation simulation) => ((IEnumerable)typeof(TotalWarVfxSimulation)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!).Cast<object>().Select(particle =>
        {
            object Field(string name) => particle.GetType().GetField(name)!.GetValue(particle)!;
            var state = Field("Emitter");
            var definition = (VfxPreviewEmitter)state.GetType().GetField("Definition")!.GetValue(state)!;
            return new Birth(definition.Name, (Vector3)Field("MotionOrigin"), (Vector3)Field("MotionInitialVelocity"),
                (float)Field("Life"), (double)Field("Birth"), (float)Field("RandomAlpha"), (Vector3)Field("InitialScale"), (Vector3)Field("Orientation"));
        }).ToArray();
    private sealed record Birth(string Name, Vector3 Position, Vector3 Velocity, float Life, double Time, float RandomAlpha, Vector3 InitialScale, Vector3 Orientation);
}
