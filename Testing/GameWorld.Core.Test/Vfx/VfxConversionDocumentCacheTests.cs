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

public class VfxConversionDocumentCacheTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedLibraryParsingPreservesEveryConversionAndRandomOutput(bool omitExcludedRow)
    {
        var child = Child("vfx/first.dds");
        var originalChild = child.Write();
        var parent = Composition(child, 3, omitExcludedRow);
        var originalParent = parent.Write();
        var cachedContext = Context();
        var uncachedContext = Context();
        var cachedReads = 0;
        var uncachedReads = 0;
        byte[] Resolve(string path, object context, bool clear)
        {
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                if (clear) { uncachedReads++; Documents(context).Clear(); }
                else cachedReads++;
                return originalChild;
            }
            return [1, 2, 3];
        }
        var cached = Convert(new(originalParent, "vfx/root-DIAG.xml", path => Resolve(path, cachedContext, false)), cachedContext);
        var uncached = Convert(new(originalParent, "vfx/root-DIAG.xml", path => Resolve(path, uncachedContext, true)), uncachedContext);
        Assert.Multiple(() =>
        {
            Assert.That(cachedReads, Is.EqualTo(3), "Every reference still resolves its actual current bytes.");
            Assert.That(uncachedReads, Is.EqualTo(cachedReads));
            Assert.That(Documents(cachedContext).Count, Is.EqualTo(2), "Only the root and one read-only library tree are retained in this conversion.");
            Assert.That(cached.TotalEmitterCount, Is.EqualTo(6));
            Assert.That(cached.DisabledEmitterCount, Is.EqualTo(3));
            Assert.That(cached.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "visible", "visible", "visible" }));
            Assert.That(cached.Diagnostics.Any(diagnostic => diagnostic.Layer == "excluded"), Is.False,
                "An excluded layer's malformed Enable and unsupported geometry remain unparsed, as in the original short circuit.");
        });
        AssertEquivalent(cached, uncached);
        foreach (var parsed in Documents(cachedContext).Values.Cast<VfxDocument>())
            Assert.That(parsed.Write(), Is.EqualTo(parsed.Root.Elements("vfx_references").Any() ? originalParent : originalChild));
        Assert.That(child.Write(), Is.EqualTo(originalChild));
        Assert.That(parent.Write(), Is.EqualTo(originalParent));
    }

    [Test]
    public void PathNormalizationReusesOnlyTheTreeAndNeverMergesReferenceInstances()
    {
        var child = Child("vfx/first.dds");
        var root = Composition(child, 2, false);
        var references = root.Root.Elements("vfx_references").Elements("vfx").ToArray();
        references[0].SetAttributeValue("vfx_ref", "vfx/shared.xml");
        references[1].SetAttributeValue("vfx_ref", @"VFX\SHARED.XML");
        var context = Context();
        var reads = 0;
        var bytes = child.Write();
        var conversion = Convert(new(root.Write(), "vfx/root-DIAG.xml", path =>
        {
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { reads++; return bytes; }
            return [1, 2, 3];
        }), context);
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(Documents(context).Count, Is.EqualTo(2));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(conversion.Emitters[0], Is.Not.SameAs(conversion.Emitters[1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.4f, Matrix.Identity);
        var states = ((IEnumerable)Read(simulation, "_emitters")).Cast<object>().ToArray();
        Assert.That(states, Has.Length.EqualTo(2));
        Assert.That(((Matrix)Read(states[0], "ReferenceTransform")).Right.Length(),
            Is.Not.EqualTo(((Matrix)Read(states[1], "ReferenceTransform")).Right.Length()),
            "Equal library XML must not merge the two reference occurrences' scale random samples.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TheSamePathWithDifferentBytesMissesEvenWhenTheResolverMutatesOneArray(bool reuseArray)
    {
        var first = Child("vfx/first.dds");
        var second = Child("vfx/other.dds");
        var original = first.Write();
        var changed = second.Write();
        var resolved = original.ToArray();
        Assert.That(changed.Length, Is.EqualTo(resolved.Length));
        var root = Composition(first, 2, false);
        var context = Context();
        var reads = 0;
        var conversion = Convert(new(root.Write(), "vfx/root-DIAG.xml", path =>
        {
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return [1, 2, 3];
            reads++;
            if (!reuseArray) return reads == 1 ? original : changed;
            if (reads == 2) changed.CopyTo(resolved, 0);
            return resolved;
        }), context);
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(Documents(context).Count, Is.EqualTo(3));
        Assert.That(conversion.TexturePaths, Is.EqualTo(new[] { "vfx/first.dds", "vfx/other.dds" }));
        var parsed = Documents(context).Values.Cast<VfxDocument>().Select(document => document.Write()).ToArray();
        Assert.That(parsed.Any(bytes => bytes.SequenceEqual(original)), Is.True, "Previously parsed bytes and baseline must survive resolver mutation.");
        Assert.That(parsed.Any(bytes => bytes.SequenceEqual(changed)), Is.True);
        Assert.That(first.Write(), Is.EqualTo(original));
        Assert.That(second.Write(), Is.EqualTo(changed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidRevisionsOfTheSamePathRetainTheirDiagnosticAndDoNotPoisonTheValidTree(bool invalidFirst)
    {
        var child = Child("vfx/first.dds");
        var valid = child.Write();
        var root = Composition(child, 2, false).Write();
        VfxConversion Run(object context, bool uncached)
        {
            var reads = 0;
            return Convert(new(root, "vfx/root-DIAG.xml", path =>
            {
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return [1, 2, 3];
                if (uncached) Documents(context).Clear();
                return (reads++ == 0) == invalidFirst ? Encoding.UTF8.GetBytes("<vfx revision=\"2\"><broken>") : valid;
            }), context);
        }
        var cachedContext = Context();
        var cached = Run(cachedContext, false);
        var uncached = Run(Context(), true);
        Assert.That(cached.Emitters, Has.Count.EqualTo(1));
        Assert.That(cached.Diagnostics.Count(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.InvalidData"), Is.EqualTo(1));
        Assert.That(Documents(cachedContext).Count, Is.EqualTo(2), "Failed XML is never inserted into the document cache.");
        AssertEquivalent(cached, uncached);
        Assert.That(child.Write(), Is.EqualTo(valid));
    }

    [Test]
    public void SeparateConversionsOwnSeparateReadOnlyTrees()
    {
        var bytes = Child("vfx/first.dds").Write();
        var first = Context();
        var second = Context();
        var snapshot = new VfxPreviewSnapshot(bytes, "vfx/shared.xml", _ => [1, 2, 3]);
        Convert(snapshot, first);
        Convert(snapshot, second);
        Assert.That(Documents(first).Values.Cast<VfxDocument>().Single(), Is.Not.SameAs(Documents(second).Values.Cast<VfxDocument>().Single()));
    }

    private static void AssertEquivalent(VfxConversion cached, VfxConversion uncached)
    {
        Assert.Multiple(() =>
        {
            Assert.That(cached.Emitters.Select(emitter => emitter.Name), Is.EqualTo(uncached.Emitters.Select(emitter => emitter.Name)));
            Assert.That(cached.TotalEmitterCount, Is.EqualTo(uncached.TotalEmitterCount));
            Assert.That(cached.DisabledEmitterCount, Is.EqualTo(uncached.DisabledEmitterCount));
            Assert.That(cached.Diagnostics, Is.EqualTo(uncached.Diagnostics));
            Assert.That(VfxPlaybackResourceIdentity.Create(cached), Is.EqualTo(VfxPlaybackResourceIdentity.Create(uncached)));
        });
        var first = new TotalWarVfxSimulation(cached);
        var second = new TotalWarVfxSimulation(uncached);
        foreach (var seconds in new[] { .4f, .8f, .2f, 1.1f })
        {
            first.Seek(seconds, Matrix.Identity);
            second.Seek(seconds, Matrix.Identity);
            Assert.That(first.ParticleCount, Is.EqualTo(second.ParticleCount));
            Assert.That(first.Frame(Camera), Is.EqualTo(second.Frame(Camera)), "Reference and particle RNG must retain exact output at " + seconds);
            Assert.That(RandomRecords(first), Is.EqualTo(RandomRecords(second)), "The complete particle random state must match at " + seconds);
            if (seconds == .4f) Assert.That(first.ParticleCount, Is.GreaterThan(0));
        }
    }

    private static object[] RandomRecords(TotalWarVfxSimulation simulation) => ((IEnumerable)Read(simulation, "_particles")).Cast<object>()
        .Select(particle => (object)new[] { Read(particle, "Birth"), Read(particle, "Life"), Read(particle, "Root"),
            Read(particle, "RandomScale"), Read(particle, "RandomColour"), Read(particle, "RandomAlpha") }).ToArray();
    private static object Read(object value, string name) => value.GetType().GetField(name, Private)!.GetValue(value)!;

    private static object Context() => Activator.CreateInstance(typeof(TotalWarVfxConverter).GetNestedType("ConversionContext", Private)!, nonPublic: true)!;
    private static IDictionary Documents(object context) => (IDictionary)context.GetType().GetField("_documents", Private)!.GetValue(context)!;
    private static VfxConversion Convert(VfxPreviewSnapshot snapshot, object context) => (VfxConversion)typeof(TotalWarVfxConverter)
        .GetMethod("ConvertNested", Private)!.Invoke(null, [snapshot, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, null, context])!;

    private static VfxDocument Child(string diffuse)
    {
        // DIAG: pure conversion/cache/RNG control, never a native visual acceptance sample.
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        child.Root.Descendants("texture_property").Single().SetAttributeValue("path", diffuse);
        var effect = child.Root.Element("vfx")!;
        var excluded = new XElement(effect.Element("emitters")!.Element("emitter")!);
        excluded.SetAttributeValue("id", "excluded");
        excluded.SetAttributeValue("type", "unsupported-DIAG");
        excluded.Add(new XElement("property", new XAttribute("name", "Enable"), new XAttribute("value", "malformed-DIAG")));
        effect.Element("emitters")!.Add(excluded);
        return child;
    }

    private static VfxDocument Composition(VfxDocument child, int count, bool omitExcludedRow)
    {
        var root = VfxDocument.CreateComposition();
        for (var index = 0; index < count; index++)
        {
            var edit = root.AddReference("shared", child);
            edit.Apply();
            var excluded = edit.Selection.Elements("emitter").Single(emitter => (string?)emitter.Attribute("name") == "excluded");
            if (omitExcludedRow) excluded.Remove();
            else excluded.SetAttributeValue("enable", "false");
            edit.Selection.SetAttributeValue("scale_min", ".8");
            edit.Selection.SetAttributeValue("scale_max", "1.2");
            edit.Selection.SetAttributeValue("start_time_min", ".1");
            edit.Selection.SetAttributeValue("start_time_max", ".3");
        }
        return root;
    }
}
