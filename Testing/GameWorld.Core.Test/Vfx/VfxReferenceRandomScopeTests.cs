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

public class VfxReferenceRandomScopeTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Test]
    public void SameReferenceOccurrence_SiblingsShareScaleAndStartSamples()
    {
        var leaf = TwoLayers();
        var composition = Composition("leaf", leaf);
        Configure(composition.Root.Descendants("vfx").Single(element => element.Attribute("vfx_ref") != null), true);
        var conversion = Convert(composition, new() { [@"vfx\leaf.xml"] = leaf.Write() });
        var states = States(new TotalWarVfxSimulation(conversion));
        Assert.That(states, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(Scale(states[1]), Is.EqualTo(Scale(states[0])), "One VFX reference instance owns its scale, not each selected emitter.");
            Assert.That(Number(states[1], "Start"), Is.EqualTo(Number(states[0], "Start")));
        });
    }

    [Test]
    public void SameReferenceOccurrence_SharesRandomChannelsButKeepsEachEmitterOverrideRange()
    {
        var leaf = TwoLayers(); var composition = Composition("leaf", leaf);
        var reference = composition.Root.Descendants("vfx").Single(element => element.Attribute("vfx_ref") != null);
        Configure(reference, false);
        ConfigureOverrides(reference);
        var states = States(new TotalWarVfxSimulation(Convert(composition, new() { [@"vfx\leaf.xml"] = leaf.Write() })));
        Assert.That(states, Has.Length.EqualTo(2));
        var first = (Vector4)Read(states[0], "Tint"); var second = (Vector4)Read(states[1], "Tint");
        Assert.Multiple(() =>
        {
            Assert.That((second.X - 4) / 4, Is.EqualTo(first.X - 1).Within(.000001), "Share the unary, not the already evaluated tint.");
            Assert.That((second.W - .6f) / .2f, Is.EqualTo((first.W - .2f) / .2f).Within(.000001));
            Assert.That((Number(states[1], "RateScale") - 3) / 2, Is.EqualTo(Number(states[0], "RateScale") - 1).Within(.000001));
            Assert.That((Number(states[1], "LifeBias") - .4f) / .4f, Is.EqualTo((Number(states[0], "LifeBias") - .1f) / .2f).Within(.000001));
            Assert.That((Number(states[1], "Duration") - 1 - .5f) / .5f, Is.EqualTo((Number(states[0], "Duration") - 1 - .2f) / .2f).Within(.000001));
            Assert.That(first.X, Is.InRange(1, 2)); Assert.That(second.X, Is.InRange(4, 8));
        });
    }

    [Test]
    public void EqualIndependentReferenceOccurrences_DoNotMergeTheirSamples()
    {
        var leaf = TwoLayers(); var root = VfxDocument.CreateComposition();
        root.AddReference("leaf", leaf).Apply(); root.AddReference("leaf", leaf).Apply();
        foreach (var reference in root.Root.Descendants("vfx").Where(element => element.Attribute("vfx_ref") != null)) Configure(reference, true);
        var states = States(new TotalWarVfxSimulation(Convert(root, new() { [@"vfx\leaf.xml"] = leaf.Write() })));
        Assert.That(states, Has.Length.EqualTo(4));
        Assert.That(Scale(states[0]), Is.Not.EqualTo(Scale(states[2])), "Structurally equal XML nodes are two independent instances.");
        Assert.That(Number(states[0], "Start"), Is.Not.EqualTo(Number(states[2], "Start")));
    }

    [Test]
    public void NestedReferenceOccurrences_ShareWithinEachBranchAndRemainIndependentBetweenBranches()
    {
        var leaf = TwoLayers(); var inner = Composition("leaf", leaf);
        Configure(inner.Root.Descendants("vfx").Single(element => element.Attribute("vfx_ref") != null), true);
        var root = VfxDocument.CreateComposition(); root.AddReference("inner", inner).Apply(); root.AddReference("inner", inner).Apply();
        foreach (var reference in root.Root.Descendants("vfx").Where(element => element.Attribute("vfx_ref") != null)) Configure(reference, true);
        var states = States(new TotalWarVfxSimulation(Convert(root,
            new() { [@"vfx\leaf.xml"] = leaf.Write(), [@"vfx\inner.xml"] = inner.Write() })));
        Assert.That(states, Has.Length.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(Scale(states[0]), Is.EqualTo(Scale(states[1])));
            Assert.That(Scale(states[2]), Is.EqualTo(Scale(states[3])));
            Assert.That(Number(states[0], "Start"), Is.EqualTo(Number(states[1], "Start")));
            Assert.That(Scale(states[0]), Is.Not.EqualTo(Scale(states[2])));
        });
    }

    [Test]
    public void EachParticleEventTrigger_HasItsOwnReferenceSamplesSharedByItsChildEmitters()
    {
        var leaf = TwoLayers(); var child = Composition("leaf", leaf);
        Configure(child.Root.Descendants("vfx").Single(element => element.Attribute("vfx_ref") != null), false);
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var emitter = parent.Root.Descendants("emitter").Single();
        Field(emitter, "InfiniteLife").SetAttributeValue("value", "true");
        Field(emitter, "MaximumParticles").SetAttributeValue("value", 4);
        Field(emitter, "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 4);
        Field(emitter, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        var conversion = Convert(parent, new() { [@"vfx\child.xml"] = child.Write(), [@"vfx\leaf.xml"] = leaf.Write() });
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.55f, Matrix.Identity);
        var events = States(simulation, initialize: false).Where(state => ReadNullable(state, "Parent") != null)
            .GroupBy(state => ReadNullable(state, "Parent"), ReferenceEqualityComparer.Instance).ToArray();
        Assert.That(events, Has.Length.EqualTo(3));
        foreach (var trigger in events)
        {
            var siblings = trigger.ToArray(); Assert.That(siblings, Has.Length.EqualTo(2));
            Assert.That(Scale(siblings[0]), Is.EqualTo(Scale(siblings[1])), "Only children of this event occurrence share the reference draw.");
        }
        Assert.That(events.Select(group => Scale(group.First())).Distinct().Count(), Is.EqualTo(3), "Different parent particles must not reuse a definition-global cache.");
    }

    [Test]
    public void Rewind_RecreatesTheSameReferenceAndParticleRandomState()
    {
        var leaf = TwoLayers(); var root = Composition("leaf", leaf);
        Configure(root.Root.Descendants("vfx").Single(element => element.Attribute("vfx_ref") != null), true);
        var conversion = Convert(root, new() { [@"vfx\leaf.xml"] = leaf.Write() });
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.8f, Matrix.Identity);
        var original = RandomRecords(simulation);
        simulation.Seek(0, Matrix.Identity); simulation.Seek(.8f, Matrix.Identity);
        Assert.That(RandomRecords(simulation), Is.EqualTo(original));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void SharedReferenceSampling_PreservesTheActualB401BirthRandomSequence(int layer)
    {
        var leaf = TwoLayers();
        static XElement Vector(string name, Vector3 value) => new("property", new XAttribute("name", name),
            new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
        foreach (var emitter in leaf.Root.Descendants("emitter"))
            emitter.Add(Vector("ParticleOrientationMin", new(10, 20, 30)), Vector("ParticleOrientationMax", new(40, 60, 80)),
                Vector("ParticleSpin_Min", new(2, 3, 4)), Vector("ParticleSpin_Max", new(5, 6, 7)),
                new XElement("property", new XAttribute("name", "ParticleSpinDirectionX"), new XAttribute("value", "CCW")),
                new XElement("property", new XAttribute("name", "ParticleSpinDirectionY"), new XAttribute("value", "CCW")),
                new XElement("property", new XAttribute("name", "ParticleSpinDirectionZ"), new XAttribute("value", "CCW")));
        var root = Composition("leaf", leaf);
        Configure(root.Root.Descendants("vfx").Single(element => element.Attribute("vfx_ref") != null), false);
        var simulation = new TotalWarVfxSimulation(Convert(root, new() { [@"vfx\leaf.xml"] = leaf.Write() }));
        simulation.Seek(.01f, Matrix.Identity);
        var particles = ((IEnumerable)Read(simulation, "_particles")).Cast<object>().ToArray();
        Assert.That(particles, Has.Length.EqualTo(2));
        static int[] Bits(Vector3 vector) => [BitConverter.SingleToInt32Bits(vector.X), BitConverter.SingleToInt32Bits(vector.Y), BitConverter.SingleToInt32Bits(vector.Z)];
        var particle = particles[layer];
        var actual = new[] { "RandomScale", "RandomColour", "Orientation", "Spin", "InitialScale" }
            .SelectMany(name => Bits((Vector3)Read(particle, name)))
            .Concat(new[] { "RandomAlpha", "InitialAlpha", "Life" }.Select(name => BitConverter.SingleToInt32Bits((float)Read(particle, name))))
            .Concat([(int)Read(particle, "Cell"), (int)Read(particle, "Row"), BitConverter.SingleToInt32Bits((float)Read(particle, "Loops"))]).ToArray();
        // Actual pre-fix B401 CPU output; leaf SHA C1E0FF03, root SHA ED048F22. Reference transform is intentionally excluded.
        int[][] expected =
        [
            [1054201963, 1059071044, 1028235894, 1062647317, 1021691255, 1056585823, 1103037392, 1109000843, 1112834862,
                1083191152, 1084400918, 1082656102, 1065353216, 1065353216, 1065353216, 1058169971, 1065353216, 1073741824, 0, 0, 0],
            [1062624519, 1042524185, 1044868051, 1023737537, 1060759678, 1049510514, 1101757464, 1108147558, 1111768254,
                1076503228, 1084948086, 1087876164, 1065353216, 1065353216, 1065353216, 1060583659, 1065353216, 1073741824, 0, 0, 0],
        ];
        Assert.That(actual, Is.EqualTo(expected[layer]), "Every emitter must still consume its six original reference random draws before using the shared unary values.");
    }

    [Test]
    public void CompleteOfficialBlowpipe_SiblingScaleAndTintUnariesBelongToOneReferenceInstance()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original game manifest is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(parts => parts.Length > 1)
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack" })
        {
            var path = Path.Combine(data!, name); Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var pair in new PackFileContainerLoader(null!).Load(path)!.FileList) files[pair.Key.Replace('/', '\\')] = pair.Value;
        }
        const string sourcePath = @"vfx\wh2_dlc17_weapon_trail_blowpipe_hero.xml";
        var source = files[sourcePath].DataSource.ReadData();
        Assert.That(ConvertHash(source), Is.EqualTo("dc9b7b8d10bbd9b9f76a66ea5ff45bd0a6fbc6f2911caffef831fdef2323b5f3"));
        var converted = TotalWarVfxConverter.Convert(new(source, sourcePath, path => files.GetValueOrDefault(path.Replace('/', '\\'))?.DataSource.ReadData(), "1"));
        Assert.That(converted.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "_rifle_100ms", "_arrow_glowing_large" }));
        var states = States(new TotalWarVfxSimulation(converted));
        var rifle = (Vector4)Read(states[0], "Tint"); var arrow = (Vector4)Read(states[1], "Tint");
        // Original27352 C75 DLL: both scales=.8478705883, both tint unaries=.48403145; seed equality is not asserted.
        Assert.Multiple(() =>
        {
            Assert.That(Scale(states[0]), Is.EqualTo(Scale(states[1])));
            Assert.That((rifle.X - 1.4f) / (.72f - 1.4f), Is.EqualTo((arrow.X - 1.47f) / (3.11f - 1.47f)).Within(.000001));
            Assert.That((rifle.Y - 1.78f) / (.89f - 1.78f), Is.EqualTo((arrow.Y - 2) / 2).Within(.000001));
        });
        Assert.That(files[sourcePath].DataSource.ReadData(), Is.EqualTo(source));
    }

    private static string ConvertHash(byte[] source) => System.Convert.ToHexStringLower(SHA256.HashData(source));
    private static object? ReadNullable(object value, string name) => value.GetType().GetField(name, Flags)?.GetValue(value)
        ?? value.GetType().GetProperty(name, Flags)?.GetValue(value);
    private static object Read(object value, string name) => ReadNullable(value, name) ?? throw new InvalidOperationException(name);
    private static float Number(object state, string name) => (float)Read(state, name);
    private static float Scale(object state) => ((Matrix)Read(state, "ReferenceTransform")).Right.Length();
    private static object[] States(TotalWarVfxSimulation simulation, bool initialize = true)
    {
        if (initialize) simulation.Seek(0, Matrix.Identity);
        return ((IEnumerable)Read(simulation, "_emitters")).Cast<object>().ToArray();
    }
    private static object[] RandomRecords(TotalWarVfxSimulation simulation) => ((IEnumerable)Read(simulation, "_particles")).Cast<object>()
        .Select(particle => (object)new[] { Read(particle, "Birth"), Read(particle, "Life"), Read(particle, "Root"),
            Read(particle, "RandomScale"), Read(particle, "RandomColour"), Read(particle, "RandomAlpha") }).ToArray();
    private static XElement Field(XElement emitter, string name) => emitter.Elements().Single(element => (string?)element.Attribute("name") == name);
    private static VfxDocument TwoLayers()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var first = document.Root.Descendants("emitter").Single(); first.SetAttributeValue("id", "first");
        Field(first, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        first.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", 1), new XAttribute("max", 1)));
        var second = new XElement(first); second.SetAttributeValue("id", "second"); first.AddAfterSelf(second);
        var chain = document.Root.Descendants("behaviour_chain").Single(); chain.SetAttributeValue("id", "first");
        var other = new XElement(chain); other.SetAttributeValue("id", "second"); chain.AddAfterSelf(other);
        return document;
    }
    private static VfxDocument Composition(string path, VfxDocument source)
    {
        var root = VfxDocument.CreateComposition(); root.AddReference(path, source).Apply(); return root;
    }
    private static void Configure(XElement reference, bool rangedStart)
    {
        reference.SetAttributeValue("scale_min", .8f); reference.SetAttributeValue("scale_max", 1.2f);
        reference.SetAttributeValue("start_time_min", rangedStart ? .1f : 0); reference.SetAttributeValue("start_time_max", rangedStart ? .3f : 0);
    }
    private static void ConfigureOverrides(XElement reference)
    {
        var first = reference.Elements("emitter").Single(element => (string?)element.Attribute("name") == "first");
        var second = reference.Elements("emitter").Single(element => (string?)element.Attribute("name") == "second");
        static void Range(XElement node, string field, float min, float max)
        {
            node.SetAttributeValue(field + "_min", min); node.SetAttributeValue(field + "_max", max);
        }
        Range(first, "spawn_rate_scale_factor", 1, 2); Range(second, "spawn_rate_scale_factor", 3, 5);
        Range(first, "particle_lifetime_bias", .1f, .3f); Range(second, "particle_lifetime_bias", .4f, .8f);
        Range(first, "emitter_lifetime_bias", .2f, .4f); Range(second, "emitter_lifetime_bias", .5f, 1);
        first.SetAttributeValue("tint_min_r", 1); first.SetAttributeValue("tint_max_r", 2);
        second.SetAttributeValue("tint_min_r", 4); second.SetAttributeValue("tint_max_r", 8);
        first.SetAttributeValue("tint_min_a", .2f); first.SetAttributeValue("tint_max_a", .4f);
        second.SetAttributeValue("tint_min_a", .6f); second.SetAttributeValue("tint_max_a", .8f);
    }
    private static VfxConversion Convert(VfxDocument root, Dictionary<string, byte[]> resources) => TotalWarVfxConverter.Convert(new(root.Write(),
        @"vfx\reference-random-scope-diag.xml", path => resources.GetValueOrDefault(path.Replace('/', '\\')) ?? [1]));
}
