#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxProjectedFrameBufferTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = VfxSpriteLowOpacityCullingTests.Camera(-1);

    [Test]
    public void ReusedSortMatchesLegacyLayerDistanceAndStableTieOrderIncludingSpecialFloatKeys()
    {
        var simulation = Simulation(1);
        var sort = typeof(TotalWarVfxSimulation).GetMethod("SortProjectedFrame", Private)!
            .CreateDelegate<Action<List<VfxPreviewParticle>, Vector3>>(simulation);
        var coordinates = new[] { 2f, -2f, 0f, -0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 3f, -3f };
        var frame = Enumerable.Range(0, 81).Select(index => new VfxPreviewParticle(new()
        {
            Layer = (index * 7) % 3, Transform = Matrix.CreateTranslation(coordinates[index % coordinates.Length], 0, 0),
            Colour = new(index, .25f, .5f, 1), Time = index,
        }, index % 2)).ToList();
        var expected = frame.OrderBy(x => x.Render.Layer)
            .ThenByDescending(x => Vector3.DistanceSquared(x.Render.Transform.Translation, Vector3.Zero)).ToArray();
        sort(frame, Vector3.Zero);
        AssertBytes(frame, expected);
        frame.Reverse();
        expected = frame.OrderBy(x => x.Render.Layer)
            .ThenByDescending(x => Vector3.DistanceSquared(x.Render.Transform.Translation, Vector3.Zero)).ToArray();
        sort(frame, Vector3.Zero);
        AssertBytes(frame, expected);
    }

    [Test]
    public void FillKeepsCallerStorageAndPreviouslyReturnedSnapshotsIndependentAcrossSeekAndEmptyFrames()
    {
        var simulation = Simulation(1024); simulation.Seek(.75f, Matrix.Identity);
        var snapshot = simulation.ProjectedFrame(Camera); var before = Bytes(snapshot);
        var native = simulation.Frame(Camera); var nativeBefore = MemoryMarshal.AsBytes(native.AsSpan()).ToArray();
        var reused = new List<VfxPreviewParticle>(); simulation.FillProjectedFrame(Camera, reused);
        AssertBytes(reused, snapshot); var capacity = reused.Capacity;
        simulation.Seek(.125f, Matrix.CreateTranslation(1, 0, 0)); simulation.FillProjectedFrame(Camera, reused);
        Assert.That(reused.Count, Is.LessThan(snapshot.Length));
        Assert.That(reused.Capacity, Is.EqualTo(capacity));
        AssertBytes(reused, simulation.ProjectedFrame(Camera));
        // Native emission already has its initial birth at zero; diagnostic culling supplies an empty frame.
        simulation.Seek(0, Matrix.Identity);
        simulation.FillProjectedFrame(Camera with { VfxLowOpacityCulling = float.MaxValue }, reused);
        Assert.That(reused, Is.Empty);
        Assert.That(Bytes(snapshot), Is.EqualTo(before));
        Assert.That(MemoryMarshal.AsBytes(native.AsSpan()).ToArray(), Is.EqualTo(nativeBefore));
    }

    [TestCase(1)]
    [TestCase(4096)]
    public void WarmFillDoesNotAllocateParticleArraysOrSortingBuffers(int rate)
    {
        var simulation = Simulation(rate); simulation.Seek(.75f, Matrix.Identity);
        var reused = new List<VfxPreviewParticle>();
        for (var index = 0; index < 12; index++) simulation.FillProjectedFrame(Camera, reused);
        Assert.That(reused.Count, Is.GreaterThan(0));
        var count = reused.Count; var capacity = reused.Capacity;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 64; index++) simulation.FillProjectedFrame(Camera, reused);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero, "Measure the production fill path after capacity/JIT warmup, without reflection or assertions in the interval.");
            Assert.That(reused.Count, Is.EqualTo(count));
            Assert.That(reused.Capacity, Is.EqualTo(capacity));
        });
    }

    [Test]
    public void SeparateSimulationsAndPreviewInstancesKeepTheirPrivateProjectedStorageIndependent()
    {
        var first = Simulation(64); var second = Simulation(512);
        first.Seek(.5f, Matrix.Identity); second.Seek(.5f, Matrix.CreateTranslation(3, 0, 0));
        var a = new List<VfxPreviewParticle>(); var b = new List<VfxPreviewParticle>();
        first.FillProjectedFrame(Camera, a); var original = Bytes(a.ToArray());
        second.FillProjectedFrame(Camera, b); second.Seek(0, Matrix.Identity); second.FillProjectedFrame(Camera, b);
        Assert.That(Bytes(a.ToArray()), Is.EqualTo(original));
        using var firstPreview = new EffekseerVfxPreview(null!); using var secondPreview = new EffekseerVfxPreview(null!);
        var storage = typeof(EffekseerVfxPreview).GetField("_projectedParticles", Private)!;
        Assert.That(storage.GetValue(firstPreview), Is.Not.SameAs(storage.GetValue(secondPreview)));
    }

    private static void AssertBytes(List<VfxPreviewParticle> actual, VfxPreviewParticle[] expected)
        => Assert.That(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(actual)).ToArray(), Is.EqualTo(Bytes(expected)));
    private static byte[] Bytes(VfxPreviewParticle[] value) => MemoryMarshal.AsBytes(value.AsSpan()).ToArray();

    private static TotalWarVfxSimulation Simulation(int rate)
    {
        // DIAG CPU-only load and sort control: no texture decoding, GPU, native renderer or desktop.
        var source = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = source.Root.Descendants("emitter").Single();
        emitter.Elements("property").Single(x => (string?)x.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", rate);
        emitter.Elements("property").Single(x => (string?)x.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        return new(TotalWarVfxConverter.Convert(new(source.Write(), "DIAG projected-buffer.xml", _ => [1])));
    }
}
