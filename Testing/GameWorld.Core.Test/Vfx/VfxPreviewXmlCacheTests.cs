#nullable enable
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxPreviewXmlCacheTests
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void SameContentUsesOneReadOnlyTreeAcrossNormalizedPaths()
    {
        var parses = 0;
        var cache = new VfxPreviewXmlCache(parser: bytes => { parses++; return VfxDocument.Read(bytes); });
        var bytes = Source();
        var original = bytes.ToArray();
        var first = cache.Read(@"VFX\shared.xml", Hash(bytes), bytes);
        var second = cache.Read("vfx/shared.xml", Hash(bytes), bytes.ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(parses, Is.EqualTo(1));
            Assert.That(cache.EntryCount, Is.EqualTo(1));
            Assert.That(cache.RetainedSourceBytes, Is.EqualTo(bytes.LongLength));
            Assert.That(first.Write(), Is.EqualTo(original));
            Assert.That(bytes, Is.EqualTo(original));
        });
    }

    [Test]
    public void InPlaceInputChangeMissesAndDoesNotCorruptTheEarlierDocument()
    {
        var parses = 0;
        var cache = new VfxPreviewXmlCache(parser: bytes => { parses++; return VfxDocument.Read(bytes); });
        var bytes = Source();
        var original = bytes.ToArray();
        var oldHash = Hash(bytes);
        var first = cache.Read("vfx/current.xml", oldHash, bytes);
        var position = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("visible"));
        Assert.That(position, Is.GreaterThanOrEqualTo(0));
        bytes[position] = (byte)'V';
        var changedHash = Hash(bytes);
        Assert.That(changedHash, Is.Not.EqualTo(oldHash));
        var second = cache.Read("vfx/current.xml", changedHash, bytes);
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.Write(), Is.EqualTo(original), "The parsed tree owns its original-byte preservation buffer.");
            Assert.That(second.Write(), Is.EqualTo(bytes));
            Assert.That(parses, Is.EqualTo(2));
            Assert.That(cache.EntryCount, Is.EqualTo(1), "A path stores its current hash rather than unbounded edit history.");
            Assert.That(cache.RetainedSourceBytes, Is.EqualTo(bytes.LongLength));
        });
    }

    [Test]
    public void EntryLimitEvictsTheLeastRecentlyReadTree()
    {
        var parses = 0;
        var cache = new VfxPreviewXmlCache(maxEntries: 2, parser: bytes => { parses++; return VfxDocument.Read(bytes); });
        var a = Source("a"); var b = Source("b"); var c = Source("c");
        var first = cache.Read("vfx/a.xml", Hash(a), a);
        var second = cache.Read("vfx/b.xml", Hash(b), b);
        Assert.That(cache.Read("vfx/a.xml", Hash(a), a), Is.SameAs(first));
        cache.Read("vfx/c.xml", Hash(c), c);
        Assert.That(cache.Read("vfx/a.xml", Hash(a), a), Is.SameAs(first));
        Assert.That(cache.Read("vfx/b.xml", Hash(b), b), Is.Not.SameAs(second));
        Assert.Multiple(() =>
        {
            Assert.That(parses, Is.EqualTo(4));
            Assert.That(cache.EntryCount, Is.EqualTo(2));
            Assert.That(cache.RetainedSourceBytes, Is.EqualTo(a.LongLength + b.LongLength));
        });
    }

    [Test]
    public void SourceByteBudgetAndOversizedBypassDoNotDiscardAUsefulEntry()
    {
        var parses = 0;
        var a = Source("a"); var b = Source("b");
        var limit = a.LongLength + b.LongLength - 1;
        var cache = new VfxPreviewXmlCache(limit, 16, bytes => { parses++; return VfxDocument.Read(bytes); });
        var first = cache.Read("vfx/a.xml", Hash(a), a);
        var second = cache.Read("vfx/b.xml", Hash(b), b);
        Assert.That(cache.EntryCount, Is.EqualTo(1));
        Assert.That(cache.RetainedSourceBytes, Is.EqualTo(b.LongLength));
        Assert.That(cache.Read("vfx/a.xml", Hash(a), a), Is.Not.SameAs(first));
        var current = cache.Read("vfx/a.xml", Hash(a), a);
        var large = Source(new string('x', checked((int)limit + 1)));
        Assert.That(large.LongLength, Is.GreaterThan(limit));
        var largeFirst = cache.Read("vfx/large.xml", Hash(large), large);
        Assert.That(cache.Read("vfx/large.xml", Hash(large), large), Is.Not.SameAs(largeFirst));
        Assert.Multiple(() =>
        {
            Assert.That(cache.Read("vfx/a.xml", Hash(a), a), Is.SameAs(current));
            Assert.That(cache.EntryCount, Is.EqualTo(1));
            Assert.That(cache.RetainedSourceBytes, Is.LessThanOrEqualTo(limit));
            Assert.That(parses, Is.EqualTo(5));
            Assert.That(second.Write(), Is.EqualTo(b), "Eviction never invalidates a document already owned by a conversion.");
        });
    }

    [Test]
    public void InvalidParsingDoesNotPoisonOrSilentlyReturnTheEarlierVersion()
    {
        var parses = 0;
        var cache = new VfxPreviewXmlCache(parser: bytes =>
        {
            parses++;
            if (bytes[0] == 0) throw new InvalidDataException("DIAG parse failure");
            return VfxDocument.Read(bytes);
        });
        var valid = Source(); var invalid = new byte[] { 0, 1, 2 };
        var original = cache.Read("vfx/file.xml", Hash(valid), valid);
        Assert.Throws<InvalidDataException>(() => cache.Read("vfx/file.xml", Hash(invalid), invalid));
        Assert.Multiple(() =>
        {
            Assert.That(cache.Read("vfx/file.xml", Hash(valid), valid), Is.SameAs(original));
            Assert.That(cache.EntryCount, Is.EqualTo(1));
            Assert.That(cache.RetainedSourceBytes, Is.EqualTo(valid.LongLength));
            Assert.That(parses, Is.EqualTo(2));
        });
    }

    [Test]
    public void AlreadyCanceledReadsNeitherParseNorReturnACachedDocument()
    {
        var parses = 0;
        var cache = new VfxPreviewXmlCache(parser: bytes => { parses++; return VfxDocument.Read(bytes); });
        var bytes = Source();
        var original = cache.Read("vfx/a.xml", Hash(bytes), bytes);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cache.Read("vfx/a.xml", Hash(bytes), bytes, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => cache.Read("vfx/b.xml", Hash(bytes), bytes, cancellation.Token));
        Assert.Multiple(() =>
        {
            Assert.That(cache.Read("vfx/a.xml", Hash(bytes), bytes), Is.SameAs(original));
            Assert.That(parses, Is.EqualTo(1));
            Assert.That(cache.EntryCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CancellationDuringParsingNeverPublishesThatTreeToAnotherOwner()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var parses = 0;
        VfxDocument? canceledDocument = null;
        var cache = new VfxPreviewXmlCache(parser: bytes =>
        {
            var call = Interlocked.Increment(ref parses);
            if (call == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("DIAG parser gate");
                return canceledDocument = VfxDocument.Read(bytes);
            }
            return VfxDocument.Read(bytes);
        });
        var bytes = Source(); var hash = Hash(bytes);
        var canceled = Task.Run(() => cache.Read("vfx/parallel.xml", hash, bytes, cancellation.Token));
        Task<VfxDocument>? active = null;
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
            active = Task.Run(() => cache.Read("vfx/parallel.xml", hash, bytes));
            cancellation.Cancel(); release.Set();
            try { await canceled.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Fail("Canceled parser result must not be returned."); }
            catch (OperationCanceledException) { }
            var valid = await active.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(valid, Is.Not.SameAs(canceledDocument));
                Assert.That(cache.Read("vfx/parallel.xml", hash, bytes), Is.SameAs(valid));
                Assert.That(parses, Is.EqualTo(2));
                Assert.That(cache.EntryCount, Is.EqualTo(1));
                Assert.That(cache.RetainedSourceBytes, Is.EqualTo(bytes.LongLength));
                Assert.That(valid.Write(), Is.EqualTo(bytes));
            });
        }
        finally
        {
            cancellation.Cancel(); release.Set();
            try { await canceled.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
            if (active != null) await active.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedReferenceScaleEditsKeepExactDtosDiagnosticsAndFutureParticles(bool collectEditorInfo)
    {
        var parses = 0;
        var documents = new List<(VfxDocument Document, byte[] Bytes)>();
        var cache = new VfxPreviewXmlCache(parser: bytes =>
        {
            parses++; var doc = VfxDocument.Read(bytes); documents.Add((doc, bytes.ToArray())); return doc;
        });
        var child = Source(); var root = Composition(child); var childReads = 0; var textureReads = 0;
        var diagnostics = new List<object>();
        for (var revision = 0; revision < 10; revision++)
        {
            var value = (.8 + revision * .05).ToString("G9", CultureInfo.InvariantCulture);
            foreach (var reference in root.Root.Element("vfx_references")!.Elements("vfx"))
            { reference.SetAttributeValue("scale_min", value); reference.SetAttributeValue("scale_max", value); }
            var bytes = root.Write(); var before = bytes.ToArray();
            byte[]? Resolve(string path)
            {
                if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { childReads++; return child; }
                textureReads++; return [1, 2, 3];
            }
            var snapshot = new VfxPreviewSnapshot(bytes, "vfx/scale-DIAG.xml", Resolve);
            var cached = TotalWarVfxConverter.Convert(snapshot, collectEditorInfo, CancellationToken.None, cache);
            var uncached = TotalWarVfxConverter.Convert(snapshot, collectEditorInfo);
            AssertEquivalent(cached, uncached);
            Assert.That(bytes, Is.EqualTo(before));
            Assert.That(cache.EntryCount, Is.EqualTo(2));
            diagnostics.Add(new { Revision = revision, Parses = parses, cached.TotalEmitterCount, cached.DisabledEmitterCount });
        }
        Assert.Multiple(() =>
        {
            Assert.That(parses, Is.EqualTo(11), "Ten changing roots and one unchanged dependency; no cross-reload reparse of the library.");
            Assert.That(childReads, Is.EqualTo(40), "Both references still resolve current bytes in both cached and uncached conversions.");
            Assert.That(textureReads, Is.GreaterThanOrEqualTo(20));
            Assert.That(documents.All(pair => pair.Document.Write().SequenceEqual(pair.Bytes)), Is.True, "No conversion may mutate any shared parse tree.");
        });
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_XML_CACHE_EVIDENCE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"xml-cache-{collectEditorInfo}.json"), JsonSerializer.Serialize(new
            { Scope = "CPU DIAG only: exact per-revision conversion/particle equivalence, unchanged roots/dependency bytes and parser count; not GUI or game visual acceptance.", Parses = parses, ChildReads = childReads, TextureReads = textureReads, Rows = diagnostics }, Json));
        }
    }

    [Test]
    public void IdenticalXmlFromDifferentPackOwnersStillUsesEachCurrentResourceResolver()
    {
        var cache = new VfxPreviewXmlCache();
        var child = Source(); var parent = Composition(child).Write();
        var firstChildReads = 0; var secondChildReads = 0;
        byte[]? First(string path) { if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { firstChildReads++; return child; } return [1, 2, 3]; }
        byte[]? Second(string path) { if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { secondChildReads++; return child; } return [4, 5, 6]; }
        var a = new VfxPreviewSnapshot(parent, "vfx/same-path-DIAG.xml", First);
        var b = new VfxPreviewSnapshot(parent, "vfx/same-path-DIAG.xml", Second);
        var first = TotalWarVfxConverter.Convert(a, true, CancellationToken.None, cache);
        var second = TotalWarVfxConverter.Convert(b, true, CancellationToken.None, cache);
        AssertEquivalent(first, TotalWarVfxConverter.Convert(a));
        AssertEquivalent(second, TotalWarVfxConverter.Convert(b));
        Assert.Multiple(() =>
        {
            Assert.That(firstChildReads, Is.EqualTo(4));
            Assert.That(secondChildReads, Is.EqualTo(4));
            Assert.That(first.Textures, Has.Count.EqualTo(2));
            Assert.That(second.Textures, Has.Count.EqualTo(2));
            Assert.That(first.Textures.All(texture => texture.SequenceEqual(new byte[] { 1, 2, 3 })), Is.True);
            Assert.That(second.Textures.All(texture => texture.SequenceEqual(new byte[] { 4, 5, 6 })), Is.True);
            Assert.That(VfxPlaybackResourceIdentity.Create(second), Is.Not.EqualTo(VfxPlaybackResourceIdentity.Create(first)));
            Assert.That(first.Emitters, Has.Count.EqualTo(2));
            Assert.That(second.Emitters, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ChangedDependencyAndInvalidDependencyAreNeverHiddenByTheEarlierCachedTree()
    {
        var cache = new VfxPreviewXmlCache();
        var originalChild = Source(); var current = originalChild;
        var root = Composition(originalChild).Write(); var reads = 0;
        byte[]? Resolve(string path) { if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { reads++; return current; } return [1, 2, 3]; }
        var snapshot = new VfxPreviewSnapshot(root, "vfx/dependency-DIAG.xml", Resolve);
        var first = TotalWarVfxConverter.Convert(snapshot, true, CancellationToken.None, cache);
        var changed = VfxDocument.Read(originalChild);
        changed.Root.Descendants("property").Single(element => (string?)element.Attribute("name") == "EmissionRateCurve")
            .Value = VfxLibraryVisibilityTests.Constant("x", 3);
        current = changed.Write();
        var second = TotalWarVfxConverter.Convert(snapshot, true, CancellationToken.None, cache);
        AssertEquivalent(second, TotalWarVfxConverter.Convert(snapshot));
        var oldSimulation = new TotalWarVfxSimulation(first); var newSimulation = new TotalWarVfxSimulation(second);
        oldSimulation.Seek(.8f, Matrix.Identity); newSimulation.Seek(.8f, Matrix.Identity);
        Assert.That(newSimulation.ParticleCount, Is.Not.EqualTo(oldSimulation.ParticleCount), "The changed dependency must actually affect the output.");
        current = Encoding.UTF8.GetBytes("<vfx broken-DIAG");
        var invalid = TotalWarVfxConverter.Convert(snapshot, true, CancellationToken.None, cache);
        AssertEquivalent(invalid, TotalWarVfxConverter.Convert(snapshot));
        Assert.That(invalid.Emitters, Is.Empty);
        Assert.That(invalid.Diagnostics, Is.Not.Empty);
        current = originalChild;
        var restored = TotalWarVfxConverter.Convert(snapshot, true, CancellationToken.None, cache);
        AssertEquivalent(restored, first);
        Assert.That(reads, Is.EqualTo(12));
    }

    private static void AssertEquivalent(VfxConversion actual, VfxConversion expected)
    {
        Assert.That(JsonSerializer.Serialize(actual, Json), Is.EqualTo(JsonSerializer.Serialize(expected, Json)),
            "Complete DTO, editor feedback, diagnostics and resource arrays must match the uncached projection.");
        var first = new TotalWarVfxSimulation(actual); var second = new TotalWarVfxSimulation(expected);
        foreach (var time in new[] { .2f, .8f, .3f, 1.4f })
        {
            first.Seek(time, Matrix.Identity); second.Seek(time, Matrix.Identity);
            Assert.That(first.ParticleCount, Is.EqualTo(second.ParticleCount));
            Assert.That(first.ProjectedFrame(Camera), Is.EqualTo(second.ProjectedFrame(Camera)));
            if (time == .8f && actual.Emitters.Count > 0) Assert.That(first.ParticleCount, Is.GreaterThan(0));
        }
    }

    private static VfxDocument Composition(byte[] child)
    {
        var root = VfxDocument.CreateComposition(); var source = VfxDocument.Read(child);
        root.AddReference("shared", source).Apply(); root.AddReference("shared", source).Apply();
        return root;
    }

    // DIAG controls for pure cache/conversion/RNG behavior, not official visual acceptance samples.
    private static byte[] Source(string marker = "")
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("property").Single(element => (string?)element.Attribute("name") == "EmissionRateCurve")
            .Value = VfxLibraryVisibilityTests.Constant("x", 20);
        if (marker.Length != 0) document.Root.Add(new XComment(marker));
        return document.Write();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
