using System.Runtime.InteropServices;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxAnimationByteBudgetTests
{
    [Test]
    public void VertexAbi_RetainsTheOriginalSeventyTwoByteLayout()
    {
        Assert.That(Marshal.SizeOf<EffekseerVertex>(), Is.EqualTo(72));
    }

    [TestCase(1033950L, 5186700L)]
    [TestCase(1100000L, 4000000L)]
    [TestCase(1000000L, 6000000L)]
    public void OriginalCombinedByteBudget_AcceptsDifferentVertexIndexRatios(long vertices, long indices)
    {
        Assert.That(vertices * 72 + indices * 4, Is.LessThanOrEqualTo(96000000));
        var budget = new VfxModelAnimationBudget();
        Assert.DoesNotThrow(() => budget.Reserve(vertices, indices));
        Assert.That(budget.Capture(), Is.EqualTo((vertices, indices)));
    }

    [TestCase(1000001L, 6000000L)]
    [TestCase(1333334L, 0L)]
    [TestCase(3L, 6000003L)]
    [TestCase(-1L, 0L)]
    [TestCase(0L, -1L)]
    [TestCase(long.MaxValue, 0L)]
    [TestCase(0L, long.MaxValue)]
    public void ByteOrIndexLimit_RejectsOverflowWithoutChargingIt(long vertices, long indices)
    {
        var budget = new VfxModelAnimationBudget();
        var error = Assert.Throws<VfxMappingException>(() => budget.Reserve(vertices, indices));
        Assert.That(error!.Field, Is.EqualTo("RigidData/Animation"));
        Assert.That(error.Reason, Is.EqualTo("Vfx.Preview.RangeUnsupported"));
        Assert.That(budget.Capture(), Is.EqualTo((0L, 0L)));
    }

    [TestCase(long.MaxValue, 0L)]
    [TestCase(0L, long.MaxValue)]
    public void CumulativeCounterOverflow_IsRejectedWithoutChangingTheCheckpoint(long vertices, long indices)
    {
        var budget = new VfxModelAnimationBudget();
        budget.Reserve(1, 3);
        var checkpoint = budget.Capture();
        Assert.Throws<VfxMappingException>(() => budget.Reserve(vertices, indices));
        Assert.That(budget.Capture(), Is.EqualTo(checkpoint));
    }

    [Test]
    public void CheckpointAndRelease_RestoreBothCostsAfterAFailedLayer()
    {
        var budget = new VfxModelAnimationBudget();
        budget.Reserve(1100000, 4000000);
        var checkpoint = budget.Capture();
        Assert.Throws<VfxMappingException>(() => budget.Reserve(12000, 6));
        Assert.That(budget.Capture(), Is.EqualTo(checkpoint));
        budget.Release(100000, 1000000);
        Assert.That(budget.Capture(), Is.EqualTo((1000000L, 3000000L)));
        budget.Reserve(100000, 1000000);
        Assert.That(budget.Capture(), Is.EqualTo(checkpoint));
        budget.Restore((0L, 0L));
        Assert.DoesNotThrow(() => budget.Reserve(1000000, 6000000));
    }

    [Test]
    public void NewlyAdmittedWeightedGeometry_PreservesEveryExistingBakedVertexAndFrame()
    {
        var source = VfxModelAnimationTests.Source();
        var control = TotalWarVfxConverter.Convert(new(source, "vfx/control.xml", VfxModelAnimationTests.Resolve)).Models.Single();
        var file = ModelFactory.Create().Load(VfxModelAnimationTests.Model());
        var part = file.ModelList[0][0];
        var original = part.Mesh.VertexList;
        part.Mesh.VertexList = Enumerable.Range(0, 4300).Select(index => original[index % original.Length]).ToArray();
        file.RecalculateOffsets();
        var modelBytes = ModelFactory.Create().Save(file);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/vertex-heavy.xml", path =>
            path.EndsWith(".rigid_model_v2", StringComparison.OrdinalIgnoreCase) ? modelBytes : VfxModelAnimationTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
        var expanded = conversion.Models.Single();
        Assert.Multiple(() =>
        {
            Assert.That(expanded.AnimationFrameCount, Is.EqualTo(control.AnimationFrameCount));
            Assert.That(expanded.AnimationCycles, Is.EqualTo(control.AnimationCycles));
            Assert.That(expanded.AnimationVertices, Has.Length.EqualTo(1032000));
        });
        var mismatches = 0;
        for (var frame = 0; frame < control.AnimationFrameCount; ++frame)
            for (var vertex = 0; vertex < 4300; ++vertex)
                if (!expanded.AnimationVertices![frame * 4300 + vertex].Equals(
                    control.AnimationVertices![frame * original.Length + vertex % original.Length])) ++mismatches;
        Assert.That(mismatches, Is.Zero, "Budget admission must not resample poses or alter any vertex attribute.");
    }

    [Test]
    public void NativeUpload_AdmitsTheSameByteCostAndRetainsBudgetAfterARejectedModel()
    {
        WithDevice(device =>
        {
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            var indices = Enumerable.Range(0, 3060).Select(index => index % 3).ToArray();
            foreach (var frames in new[] { 420, 435, 420, 420 })
            {
                var vertices = Vertices(610, frames);
                EffekseerNative.Check(EffekseerNative.AecnVfxModelFrames(preview, vertices, vertices.Length, 610, frames, indices, indices.Length, 4));
            }
            var overflow = Vertices(6000, 2);
            Assert.That(EffekseerNative.AecnVfxModelFrames(preview, overflow, overflow.Length, 6000, 2, [0, 1, 2], 3, 1), Is.Zero);
            var small = Vertices(3, 2);
            EffekseerNative.Check(EffekseerNative.AecnVfxModelFrames(preview, small, small.Length, 3, 2, [0, 1, 2], 3, 1));
        });
    }

    [Test]
    public void NativeUpload_RetainsTheOriginalSixMillionIndexLimit()
    {
        WithDevice(device =>
        {
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            var vertices = Vertices(3, 2);
            var indices = new int[3000003];
            Assert.That(EffekseerNative.AecnVfxModelFrames(preview, vertices, vertices.Length, 3, 2, indices, indices.Length, 1), Is.Zero);
        });
    }

    private static EffekseerVertex[] Vertices(int verticesPerFrame, int frames) =>
        Enumerable.Range(0, verticesPerFrame * frames).Select(index => new EffekseerVertex
        {
            Position = new(index % 3 == 1 ? 1 : 0, index % 3 == 2 ? 1 : 0, 0),
            Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up,
            Colour = Vector4.One,
        }).ToArray();

    private static void WithDevice(Action<GraphicsDevice> action)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try { action(graphics.GraphicsDevice); }
        finally { graphics.Release(); }
    }
}
