#nullable enable
using System.Diagnostics;
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

[NonParallelizable]
public class VfxPrewarmBudgetTests
{
    [TestCase(-600f, true)]
    [TestCase(-100f, true)]
    [TestCase(-30f, true)]
    [TestCase(600f, true)]
    [TestCase(-600.01f, false)]
    [TestCase(600.01f, false)]
    public void AuthoredStart_UsesTheBoundedOrdinarySeekRange(float start, bool supported)
    {
        var document = Source("source", start, start);
        var bytes = document.Write();
        var plan = TotalWarVfxConverter.Convert(new(bytes, "vfx/diagnostic-prewarm.xml", _ => [1]));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Has.Count.EqualTo(supported ? 1 : 0));
            Assert.That(plan.Diagnostics.Any(d => d.Field == "StartTime/LifeTime" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.EqualTo(!supported));
            if (supported) Assert.That(plan.Emitters.Single().Start, Is.EqualTo(new VfxRange(start, start)), "Keep authored time; do not clamp it to the old thirty-second limit.");
            Assert.That(document.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void ReferencedStartBudget_DropsOnlyTheOffendingEmitterAndKeepsOtherLayers()
    {
        var child = Source("too_early", -20, -20);
        CopyLayer(Source("reference_control", 0, 0), child);
        var parent = VfxDocument.CreateComposition();
        CopyLayer(Source("direct_control", 0, 0), parent);
        var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.SetAttributeValue("simulation_speed_multiplier", ".01");
        var childBytes = child.Write(); var bytes = parent.Write();
        var plan = TotalWarVfxConverter.Convert(new(bytes, "vfx/diagnostic-parent.xml", path => path == @"vfx\child.xml" ? childBytes : [1]));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters.Select(e => e.Name), Is.EquivalentTo(new[] { "direct_control", "reference_control" }));
            Assert.That(plan.TotalEmitterCount, Is.EqualTo(3));
            Assert.That(plan.Diagnostics.Any(d => d.Layer == "too_early" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True,
                "A positive reference speed can turn an otherwise valid start into an unbounded prewarm; report the dropped layer explicitly.");
            Assert.That(child.Write(), Is.EqualTo(childBytes));
            Assert.That(parent.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void FinalNestedStartBudget_AllowsAParentOffsetToRescueAnIntermediateStart()
    {
        var leaf = Source("nested", -400, -400);
        var inner = VfxDocument.CreateComposition();
        var innerEdit = inner.AddReference("leaf", leaf); innerEdit.Apply();
        innerEdit.Selection.SetAttributeValue("simulation_speed_multiplier", ".5");
        var outer = VfxDocument.CreateComposition();
        var outerEdit = outer.AddReference("inner", inner); outerEdit.Apply();
        outerEdit.Selection.SetAttributeValue("start_time_min", "300");
        outerEdit.Selection.SetAttributeValue("start_time_max", "300");
        var leafBytes = leaf.Write(); var innerBytes = inner.Write(); var outerBytes = outer.Write();
        byte[]? Resolve(string path) => path switch { @"vfx\leaf.xml" => leafBytes, @"vfx\inner.xml" => innerBytes, _ => [1] };
        var plan = TotalWarVfxConverter.Convert(new(outerBytes, "vfx/diagnostic-outer.xml", Resolve));
        Assert.That(plan.Emitters, Has.Count.EqualTo(1), "The intermediate -800 start is rescued to -500 by the outer offset.");
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(0, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(EmitterStart(simulation), Is.EqualTo(-500));
            Assert.That(plan.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.False);
            Assert.That(leaf.Write(), Is.EqualTo(leafBytes));
            Assert.That(inner.Write(), Is.EqualTo(innerBytes));
            Assert.That(outer.Write(), Is.EqualTo(outerBytes));
        });
    }

    [Test]
    public void FinalStartBudget_ChecksTheWholeRandomEnvelope()
    {
        var child = Source("partial_range", -50, 0);
        var parent = VfxDocument.CreateComposition();
        CopyLayer(Source("control", 0, 0), parent);
        var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.SetAttributeValue("simulation_speed_multiplier", ".05");
        var source = child.Write(); var bytes = parent.Write();
        var plan = TotalWarVfxConverter.Convert(new(bytes, "vfx/diagnostic-parent.xml", path => path == @"vfx\child.xml" ? source : [1]));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "control" }));
            Assert.That(plan.Diagnostics.Any(d => d.Layer == "partial_range" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
            Assert.That(child.Write(), Is.EqualTo(source));
            Assert.That(parent.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void FinalStartBudget_RemovesRejectedEventTemplatesFromTheEventGraph()
    {
        var leaf = Source("event_too_early", -20, -20);
        var child = VfxDocument.CreateComposition();
        CopyLayer(Source("event_control", 0, 0), child);
        var edit = child.AddReference("leaf", leaf); edit.Apply();
        edit.Selection.SetAttributeValue("simulation_speed_multiplier", ".01");
        var parent = VfxParticleEventTests.Source(true, "true");
        var leafBytes = leaf.Write(); var childBytes = child.Write();
        byte[]? Resolve(string path) => path switch { @"vfx\leaf.xml" => leafBytes, @"vfx\child.xml" => childBytes, _ => [1] };
        var plan = TotalWarVfxConverter.Convert(new(parent, "vfx/diagnostic-event-parent.xml", Resolve));
        var trigger = plan.Emitters.Single(e => !e.IsEventTemplate).Events.Single();
        var simulation = new TotalWarVfxSimulation(plan); simulation.Seek(.2f, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.Children.Select(e => e.Name), Is.EqualTo(new[] { "event_control" }));
            Assert.That(plan.Emitters.Any(e => e.Name == "event_too_early"), Is.False);
            Assert.That(plan.Diagnostics.Any(d => d.Layer == "event_too_early" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
            Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
            Assert.That(child.Write(), Is.EqualTo(childBytes));
            Assert.That(leaf.Write(), Is.EqualTo(leafBytes));
        });
    }

    [Test]
    public void FinalStartBudget_RejectsFloatOverflowEvenWhenDoubleOffsetsWouldCancel()
    {
        var leaf = Source("overflow", 0, 0);
        leaf.Root.Element("vfx")!.Element("emitters")!.Element("emitter")!.Add(new XElement("ranged_property",
            new XAttribute("name", "LifeTime"), new XAttribute("min", ".0001"), new XAttribute("max", ".0001")));
        VfxDocument Wrap(string name, VfxDocument source, float speed, float start)
        {
            var document = VfxDocument.CreateComposition();
            var edit = document.AddReference(name, source); edit.Apply();
            edit.Selection.SetAttributeValue("simulation_speed_multiplier", speed.ToString(CultureInfo.InvariantCulture));
            edit.Selection.SetAttributeValue("start_time_min", start.ToString(CultureInfo.InvariantCulture));
            edit.Selection.SetAttributeValue("start_time_max", start.ToString(CultureInfo.InvariantCulture));
            return document;
        }
        var inner = Wrap("leaf", leaf, 1, -MathF.Pow(2, 120));
        var amplified = Wrap("inner", inner, 1f / 8192, 0);
        var slowed = Wrap("amplified", amplified, 16, 0);
        var parent = Wrap("slowed", slowed, 16, MathF.Pow(2, 125));
        var resources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [@"vfx\leaf.xml"] = leaf.Write(), [@"vfx\inner.xml"] = inner.Write(),
            [@"vfx\amplified.xml"] = amplified.Write(), [@"vfx\slowed.xml"] = slowed.Write(),
        };
        var bytes = parent.Write();
        var plan = TotalWarVfxConverter.Convert(new(bytes, "vfx/diagnostic-overflow.xml", path => resources.GetValueOrDefault(path, [1])));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Is.Empty, "The runtime uses float; a -Infinity start must never enter its fixed-step prewarm loop.");
            Assert.That(plan.Diagnostics.Any(d => d.Layer == "overflow" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
            Assert.That(parent.Write(), Is.EqualTo(bytes));
            Assert.That(leaf.Write(), Is.EqualTo(resources[@"vfx\leaf.xml"]));
            Assert.That(inner.Write(), Is.EqualTo(resources[@"vfx\inner.xml"]));
            Assert.That(amplified.Write(), Is.EqualTo(resources[@"vfx\amplified.xml"]));
            Assert.That(slowed.Write(), Is.EqualTo(resources[@"vfx\slowed.xml"]));
        });
    }

    [Test]
    public void SixHundredSecondPrewarmAndSeek_KeepTheirAuthoredTimeline()
    {
        var document = Source("budget_boundary", -600, -600);
        var source = document.Write();
        var plan = TotalWarVfxConverter.Convert(new(source, "vfx/diagnostic-budget-boundary.xml", _ => [1]));
        Assert.That(plan.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(600, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(EmitterStart(simulation), Is.EqualTo(-600));
            Assert.That(typeof(TotalWarVfxSimulation).GetField("_simulatedTime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(simulation), Is.EqualTo(600d).Within(.000001));
            Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
            Assert.That(simulation.CapacityReached, Is.False);
            Assert.That(document.Write(), Is.EqualTo(source));
        });
        // 600 seconds prewarm plus 600 seconds seek is a bounded maximum of 144000 ordinary fixed steps.
    }

    [Test]
    public void CompleteOfficialSiegeAshes_PreserveTheOriginalHundredSecondPrewarm()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Official resources require explicit VFX opt-in.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !Directory.Exists(data)) Assert.Ignore("Original game data is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(l => l.Split('\t')).Where(p => p.Length > 1)
            .ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var loader = new PackFileContainerLoader(null!);
        var packs = new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" }.Select(name =>
        {
            var path = Path.Combine(data!, name);
            Assert.That(manifest.TryGetValue(name, out var size), Is.True);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(size));
            return loader.Load(path)!;
        }).OrderBy(p => p.Header.LoadOrder).ThenBy(p => p.Name).ToArray();
        var index = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        foreach (var entry in pack.FileList) index[entry.Key.Replace('/', '\\')] = entry.Value;
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? Read(string path)
        {
            var key = path.Replace('/', '\\');
            if (!index.TryGetValue(key, out var file)) return null;
            if (!originals.TryGetValue(key, out var bytes)) originals[key] = bytes = file.DataSource.ReadData();
            return bytes;
        }
        const string root = @"vfx\wh_dlc03_enviro_large_siege_smoke.xml";
        const string library = @"vfx\wh_dlc03_lib_large_fire_smoke.xml";
        var rootBytes = Read(root) ?? throw new FileNotFoundException(root);
        var libraryBytes = Read(library) ?? throw new FileNotFoundException(library);
        var diagnostic = VfxDocument.Read(libraryBytes);
        var diagnosticStart = diagnostic.Root.Descendants("emitter").Single(e => (string?)e.Attribute("id") == "rising_ashes2")
            .Elements().Where(e => (string?)e.Attribute("name") == "StartTime" && (e.Attribute("quality") == null || (string?)e.Attribute("quality") is "0" or "1"))
            .OrderBy(e => (string?)e.Attribute("quality") == "1" ? 1 : 0).Last();
        diagnosticStart.SetAttributeValue("min", "-30"); diagnosticStart.SetAttributeValue("max", "-30");
        var diagnosticBytes = diagnostic.Write();
        // DIAG changes only the old start limit, proving there is no second unsupported-field blocker.
        var diagnosticPlan = TotalWarVfxConverter.Convert(new(rootBytes, root, path => path.Replace('/', '\\').Equals(library, StringComparison.OrdinalIgnoreCase) ? diagnosticBytes : Read(path), "1"));
        TestContext.Progress.WriteLine($"Ash diagnostic Start=-30: {diagnosticPlan.Emitters.Count}/{diagnosticPlan.TotalEmitterCount} emitters");
        Assert.That(diagnosticPlan.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "rising_ashes2" }));
        var diagnosticEmitter = diagnosticPlan.Emitters.Single();
        TestContext.Progress.WriteLine($"Ash capacity contract: MaximumParticles={diagnosticEmitter.MaximumParticles}, EmissionMaximum={diagnosticEmitter.Emission.Maximum}, LifeMaximum={diagnosticEmitter.Life.Maximum}, references={diagnosticEmitter.References.Count}");
        var hashes = originals.ToDictionary(pair => pair.Key, pair => Convert.ToHexString(SHA256.HashData(pair.Value)), StringComparer.OrdinalIgnoreCase);
        var plan = TotalWarVfxConverter.Convert(new(rootBytes, root, Read, "1"));
        Assert.That(plan.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "rising_ashes2" }), "The complete unmodified original is the acceptance source.");
        var emitter = plan.Emitters.Single();
        var watch = Stopwatch.StartNew();
        var simulation = new TotalWarVfxSimulation(plan); simulation.Seek(0, Matrix.Identity); watch.Stop();
        TestContext.Progress.WriteLine($"Original Ash Start={emitter.Start}, MaximumParticles={emitter.MaximumParticles}, EmissionMaximum={emitter.Emission.Maximum}, LifeMaximum={emitter.Life.Maximum}, active={simulation.ParticleCount}, prewarmMs={watch.ElapsedMilliseconds}");
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Start, Is.EqualTo(new VfxRange(-100, -100)));
            Assert.That(EmitterStart(simulation), Is.EqualTo(-100));
            Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
            Assert.That(simulation.CapacityReached, Is.False);
            Assert.That(plan.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RangeUnsupported" || d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
            Assert.That(VfxDocument.Read(rootBytes).Write(), Is.EqualTo(rootBytes));
            Assert.That(VfxDocument.Read(libraryBytes).Write(), Is.EqualTo(libraryBytes));
            foreach (var pair in hashes) Assert.That(Convert.ToHexString(SHA256.HashData(originals[pair.Key])), Is.EqualTo(pair.Value));
        });
    }

    private static VfxDocument Source(string name, float min, float max)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var effect = document.Root.Element("vfx")!;
        var emitter = effect.Element("emitters")!.Element("emitter")!;
        emitter.SetAttributeValue("id", name);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", min.ToString(CultureInfo.InvariantCulture)), new XAttribute("max", max.ToString(CultureInfo.InvariantCulture))));
        effect.Element("behaviour_chains")!.Element("behaviour_chain")!.SetAttributeValue("id", name);
        return document;
    }

    private static void CopyLayer(VfxDocument source, VfxDocument target)
    {
        var from = source.Root.Element("vfx")!; var to = target.Root.Element("vfx")!;
        foreach (var container in new[] { "emitters", "behaviour_chains" })
        {
            if (to.Element(container) == null) to.Add(new XElement(container));
            to.Element(container)!.Add(from.Element(container)!.Elements().Select(e => new XElement(e)));
        }
    }

    private static float EmitterStart(TotalWarVfxSimulation simulation)
    {
        var states = (System.Collections.IEnumerable)typeof(TotalWarVfxSimulation).GetField("_emitters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(simulation)!;
        var state = states.Cast<object>().Single();
        return (float)state.GetType().GetField("Start")!.GetValue(state)!;
    }
}
