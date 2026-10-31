#nullable enable
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxInteractiveConversionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    [TestCase(false)]
    [TestCase(true)]
    public void BackgroundConversionPreservesSynchronousMetadataDiagnosticsResourcesAndPackets(bool collectEditorInfo)
    {
        var source = Source();
        var child = VfxDocument.Read(source);
        var root = VfxDocument.CreateComposition();
        root.AddReference("child", child).Apply();
        root.AddReference("child", child).Apply();
        var bytes = root.Write();
        byte[]? Resolve(string path) => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? source : [1, 2, 3];
        var snapshot = new VfxPreviewSnapshot(bytes, "vfx/interactive-DIAG.xml", Resolve);
        var expected = TotalWarVfxConverter.Convert(snapshot, collectEditorInfo);
        using var helper = new VfxInteractiveConversion(snapshot, collectEditorInfo);
        var prepared = Take(helper);
        AssertEquivalent(prepared.Conversion, expected);
        Assert.That(prepared.ResourceIdentity, Is.EqualTo(VfxPlaybackResourceIdentity.Create(expected)));
        Assert.That(prepared.ResourcePaths.Select(path => path.Replace('\\', '/')), Is.EquivalentTo(new[] { "vfx/child.xml", "vfx/texture.dds" }));
        Assert.That(root.Write(), Is.EqualTo(bytes));
        Assert.That(child.Write(), Is.EqualTo(source));
    }

    [Test]
    public void ConstructionDoesNotReadResourcesAndOnlyTheOwnerPumpCallsTheResolver()
    {
        var owner = Environment.CurrentManagedThreadId;
        var reads = 0;
        using var helper = new VfxInteractiveConversion(new(Source(), "vfx/thread-DIAG.xml", _ =>
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner), "Pack and live-document services are owner-thread only.");
            reads++;
            return [1, 2, 3];
        }), true, [@"vfx\texture.dds"]);
        Assert.That(reads, Is.Zero, "Construction must only enqueue resource demand.");
        Assert.That(Pending(helper).IsCompleted, Is.False);
        var prepared = Take(helper);
        Assert.That(reads, Is.EqualTo(1));
        Assert.That(prepared.Conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(prepared.Conversion.Textures.Single(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void EveryNewSnapshotReadsItsCurrentResourceBytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var reads = 0;
        var snapshot = new VfxPreviewSnapshot(Source(), "vfx/current-DIAG.xml", _ => { reads++; return bytes; });
        using var first = new VfxInteractiveConversion(snapshot, false);
        var original = Take(first);
        bytes = [4, 5, 6];
        using var second = new VfxInteractiveConversion(snapshot, false, original.ResourcePaths);
        var changed = Take(second);
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(original.Conversion.Textures.Single(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(changed.Conversion.Textures.Single(), Is.EqualTo(new byte[] { 4, 5, 6 }));
        Assert.That(changed.ResourceIdentity, Is.Not.EqualTo(original.ResourceIdentity));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingAndThrowingResourcesKeepTheSynchronousFailureDiagnostics(bool throwing)
    {
        byte[]? Resolve(string _) => throwing ? throw new InvalidDataException("DIAG resolver failure") : null;
        var snapshot = new VfxPreviewSnapshot(Source(), "vfx/missing-DIAG.xml", Resolve);
        var expected = TotalWarVfxConverter.Convert(snapshot);
        using var helper = new VfxInteractiveConversion(snapshot, true);
        var prepared = Take(helper);
        AssertEquivalent(prepared.Conversion, expected);
        Assert.That(prepared.Conversion.Emitters, Is.Empty);
        Assert.That(prepared.Conversion.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void PumpBudgetDoesNotDrainEveryQueuedResourceInOneFrame()
    {
        var reads = new List<string>();
        using var helper = new VfxInteractiveConversion(new(Source(), "vfx/budget-DIAG.xml", path =>
        {
            reads.Add(path);
            return [1, 2, 3];
        }), false, ["unused/first-DIAG", "unused/second-DIAG", @"vfx\texture.dds"]);
        Assert.That(helper.TryTake(out _, TimeSpan.Zero), Is.False);
        Assert.That(reads, Is.EqualTo(new[] { "unused/first-DIAG" }));
        var prepared = Take(helper);
        Assert.That(reads, Is.EqualTo(new[] { "unused/first-DIAG", "unused/second-DIAG", @"vfx\texture.dds" }));
        Assert.That(prepared.Conversion.Emitters, Has.Count.EqualTo(1));
    }

    [Test]
    public void DisposedRequestsNeverReadTheOwnerAndReleaseBothBoundedWorkerSlots()
    {
        var reads = 0;
        var helpers = Enumerable.Range(0, 6).Select(index => new VfxInteractiveConversion(
            new(Source(), "vfx/cancel-" + index + "-DIAG.xml", _ => { reads++; return [1]; }), false)).ToArray();
        try
        {
            WaitUntil(() => helpers.All(HasQueuedRequest), "Waiting for an inactive owner's resource pump must not retain a CPU worker slot.");
            Assert.That(Workers.CurrentCount, Is.InRange(0, 2), "The shared semaphore still limits simultaneous CPU conversion work to two.");
            foreach (var helper in helpers) { helper.Dispose(); helper.Dispose(); }
            foreach (var helper in helpers) Assert.That(helper.TryTake(out _), Is.False);
            WaitUntil(() => helpers.All(helper => Pending(helper).IsCompleted), "Canceled CPU jobs must drain without an owner resolver call.");
            WaitUntil(() => Workers.CurrentCount == 2, "Cancellation must return every CPU worker permit exactly once.");
            Assert.That(reads, Is.Zero);
            using var replacement = new VfxInteractiveConversion(new(Source(), "vfx/replacement-DIAG.xml", _ => [1]), false);
            Assert.That(Take(replacement).Conversion.Emitters, Has.Count.EqualTo(1), "Canceled workers must release their bounded slots.");
        }
        finally
        {
            foreach (var helper in helpers) helper.Dispose();
            WaitUntil(() => helpers.All(helper => Pending(helper).IsCompleted), "Drain all test-owned jobs before leaving the test.");
        }
    }

    [Test]
    public void TwoHiddenOwnersWaitingForResourcePumpsDoNotBlockAVisibleThirdPreview()
    {
        var hiddenReads = 0;
        var visibleReads = 0;
        var hidden = Enumerable.Range(0, 2).Select(index => new VfxInteractiveConversion(
            new(Source(), "vfx/hidden-" + index + "-DIAG.xml", _ => { hiddenReads++; return [1]; }), false)).ToArray();
        VfxInteractiveConversion? visible = null;
        try
        {
            WaitUntil(() => hidden.All(HasQueuedRequest), "Both hidden viewports must be waiting for their unpumped owner-thread resource requests.");
            Assert.That(hiddenReads, Is.Zero);
            Assert.That(hidden.All(helper => !Pending(helper).IsCompleted), Is.True);
            visible = new VfxInteractiveConversion(new(Source(), "vfx/visible-DIAG.xml", _ => { visibleReads++; return [2]; }), false);
            var prepared = Take(visible);
            Assert.That(prepared.Conversion.Emitters, Has.Count.EqualTo(1), "A visible viewport must complete while both hidden owners remain inactive.");
            Assert.That(visibleReads, Is.EqualTo(1));
            Assert.That(hiddenReads, Is.Zero, "Progress must never invoke an inactive owner's resolver from another viewport or worker.");
            Assert.That(hidden.All(helper => !Pending(helper).IsCompleted), Is.True,
                "The hidden conversions remain pending; visible progress must not discard or complete their snapshots.");
            Assert.That(Workers.CurrentCount, Is.InRange(0, 2));
        }
        finally
        {
            visible?.Dispose();
            foreach (var helper in hidden) helper.Dispose();
            WaitUntil(() => hidden.All(helper => Pending(helper).IsCompleted) && (visible == null || Pending(visible).IsCompleted),
                "Dispose must drain all three test-owned conversions without pumping hidden owners.");
            WaitUntil(() => Workers.CurrentCount == 2, "All bounded CPU permits must be recovered after inactive viewports are disposed.");
        }
    }

    [Test]
    public void QueuedConversionOwnsTheGameBytesAndBothRootSelectionLists()
    {
        var bytes = Source();
        var original = bytes.ToArray();
        var emitters = new List<string> { "visible" };
        var references = new List<string>();
        var snapshot = new VfxPreviewSnapshot(bytes, "vfx/captured-DIAG.xml", _ => [1],
            RootSelection: new(emitters, references));
        var expected = TotalWarVfxConverter.Convert(snapshot);
        using var helper = new VfxInteractiveConversion(snapshot, true);
        Array.Fill(bytes, (byte)0);
        emitters.Clear();
        references.Add("later-DIAG");
        var prepared = Take(helper);
        AssertEquivalent(prepared.Conversion, expected);
        Assert.That(prepared.Conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(original, Is.Not.EqualTo(bytes));
    }

    private static VfxPreparedConversion Take(VfxInteractiveConversion helper)
    {
        VfxPreparedConversion? result = null;
        WaitUntil(() => helper.TryTake(out result), "Owner pump must complete a small deterministic conversion within its deadline.");
        return result!;
    }

    private static Task Pending(VfxInteractiveConversion helper) =>
        (Task)typeof(VfxInteractiveConversion).GetField("_pending", Private)!.GetValue(helper)!;

    private static SemaphoreSlim Workers =>
        (SemaphoreSlim)typeof(VfxInteractiveConversion).GetField("Workers", Private)!.GetValue(null)!;

    private static bool HasQueuedRequest(VfxInteractiveConversion helper) =>
        ((IEnumerable)typeof(VfxInteractiveConversion).GetField("_requests", Private)!.GetValue(helper)!).Cast<object>().Any();

    private static void WaitUntil(Func<bool> condition, string message)
    {
        var started = Stopwatch.GetTimestamp();
        while (!condition() && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10)) Thread.Sleep(1);
        Assert.That(condition(), Is.True, message);
    }

    private static void AssertEquivalent(VfxConversion actual, VfxConversion expected)
    {
        Assert.That(JsonSerializer.Serialize(actual, Json), Is.EqualTo(JsonSerializer.Serialize(expected, Json)),
            "Conversion DTO, exact metadata states, diagnostics and uploaded resource payloads must remain identical.");
        var actualSimulation = new TotalWarVfxSimulation(actual);
        var expectedSimulation = new TotalWarVfxSimulation(expected);
        foreach (var time in new[] { .4f, .8f, .2f, 1.1f })
        {
            actualSimulation.Seek(time, Matrix.Identity);
            expectedSimulation.Seek(time, Matrix.Identity);
            Assert.That(actualSimulation.ParticleCount, Is.EqualTo(expectedSimulation.ParticleCount));
            Assert.That(actualSimulation.ProjectedFrame(Camera), Is.EqualTo(expectedSimulation.ProjectedFrame(Camera)),
                "All submitted particle values and seeded future simulation must match at " + time);
        }
    }

    // DIAG: conversion/thread/lifecycle control only; never a native visual acceptance sample.
    private static byte[] Source() => VfxLibraryVisibilityTests.Source("");
}
