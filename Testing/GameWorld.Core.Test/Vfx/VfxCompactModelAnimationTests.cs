#nullable enable
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxCompactModelAnimationTests
{
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public void SharedSupportedClipVersions_KeepTheirOriginalPoses(int version)
    {
        var original = VfxModelAnimationTests.Animation(false);
        var clip = AnimationFile.Create(new PackFile("clip.anim", new MemorySource(original)));
        clip.Header.Version = (uint)version;
        var bytes = AnimationFile.ConvertToBytes(clip);
        var source = VfxModelAnimationTests.Source();
        var converted = Convert(source, bytes);
        var reference = Convert(source, original);
        Assert.That(converted.Emitters, Has.Count.EqualTo(1), Reasons(converted));
        foreach (var (actual, expected) in converted.Models.Single().AnimationVertices!.Zip(reference.Models.Single().AnimationVertices!))
            Assert.That(Vector3.Distance(actual.Position, expected.Position), Is.LessThan(.0001f));
    }

    [Test]
    public void LongLife_KeepsTheSameSampledPosesWithinTheOriginalByteBudget()
    {
        // DIAG: storage control, without changing an official source or its particle lifetime.
        var source = VfxModelAnimationTests.Source(life: 31);
        var converted = Convert(source);
        Assert.That(converted.Emitters, Has.Count.EqualTo(1), Reasons(converted));
        var model = converted.Models.Single();
        Assert.That(model.AnimationVertices, Is.Null);
        Assert.That(model.AnimationData, Is.Not.Null);
        Assert.That(model.AnimationFrameCount, Is.EqualTo(1860));
        Assert.That(model.AnimationCycles, Is.EqualTo(1));
        Assert.That(converted.Emitters.Single().Life.Maximum, Is.EqualTo(31));
        var reference = Convert(VfxModelAnimationTests.Source()).Models.Single();
        foreach (var phase in new[] { 0f, .25f, .5f, .75f })
        {
            var frame = (int)(phase * model.AnimationFrameCount);
            var expectedFrame = (int)(phase * reference.AnimationFrameCount);
            var actual = model.AnimationData!.Sample(model.Vertices, frame);
            for (var index = 0; index < actual.Length; ++index)
            {
                var expected = reference.AnimationVertices![expectedFrame * reference.Vertices.Length + index];
                Assert.That(Vector3.Distance(actual[index].Position, expected.Position), Is.LessThan(.000001f));
                Assert.That(Vector3.Distance(actual[index].Normal, expected.Normal), Is.LessThan(.000001f));
                Assert.That(actual[index].UV, Is.EqualTo(expected.UV));
            }
        }
        Assert.That(VfxModelAnimationData.ReservedVertices(model.Vertices.Length, model.AnimationFrameCount, model.AnimationData!.BoneCount) * 72,
            Is.LessThan(96000000));
        Assert.That(Marshal.SizeOf<EffekseerSkinWeight>(), Is.EqualTo(32));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void CompactPalette_StillRejectsAnUnsafeAllocationAndRestoresBudget()
    {
        var budget = new VfxModelAnimationBudget();
        var before = budget.Capture();
        Assert.Throws<VfxMappingException>(() => budget.Reserve(VfxModelAnimationData.ReservedVertices(3, 65536, 256), 15));
        Assert.That(budget.Capture(), Is.EqualTo(before));
        Assert.That(budget.TryReserve(3, 3), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AuthoredEntryPivot_IsAnOffsetAfterSkinning(bool compact)
    {
        var document = VfxDocument.Read(VfxModelAnimationTests.Source(life: compact ? 31 : 1));
        var pivot = document.Root.Descendants("rigid_entry").Single().Element("PivotPoint")!;
        pivot.SetAttributeValue("x", -2.6f); pivot.SetAttributeValue("y", .5f); pivot.SetAttributeValue("z", .6f);
        var actual = Convert(document.Write());
        var reference = Convert(VfxModelAnimationTests.Source(life: compact ? 31 : 1));
        Assert.That(actual.Emitters, Has.Count.EqualTo(1), Reasons(actual));
        var model = actual.Models.Single(); var control = reference.Models.Single();
        var frame = model.AnimationFrameCount / 2;
        var points = model.AnimationData?.Sample(model.Vertices, frame)
            ?? model.AnimationVertices!.Skip(frame * model.Vertices.Length).Take(model.Vertices.Length).ToArray();
        var expected = control.AnimationData?.Sample(control.Vertices, frame)
            ?? control.AnimationVertices!.Skip(frame * control.Vertices.Length).Take(control.Vertices.Length).ToArray();
        for (var index = 0; index < points.Length; ++index)
            Assert.That(Vector3.Distance(points[index].Position - expected[index].Position, new(3.1f, 0, -.1f)), Is.LessThan(.000001f));
        Assert.That(VfxPlaybackResourceIdentity.Create(actual), Is.Not.EqualTo(VfxPlaybackResourceIdentity.Create(reference)));
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void OnDemandFrames_MatchTheExistingBakedGpuAndSurviveCacheEviction(bool pivot)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var actual = new EffekseerVfxPreview(graphics.GraphicsDevice);
            using var reference = new EffekseerVfxPreview(graphics.GraphicsDevice);
            byte[] Source(float life)
            {
                var source = VfxDocument.Read(VfxModelAnimationTests.Source(life: life));
                if (pivot) source.Root.Descendants("rigid_entry").Single().Element("PivotPoint")!.SetAttributeValue("x", -.5f);
                return source.Write();
            }
            actual.Reload(new(Source(31), "DIAG/compact-animation.xml", VfxModelAnimationTests.Resolve));
            reference.Reload(new(Source(1), "DIAG/baked-animation.xml", VfxModelAnimationTests.Resolve));
            foreach (var phase in new[] { .1f, .2f, .3f, .4f, .5f, .6f, .7f, .1f })
            {
                var pixels = Render(actual, phase * 31); var expected = Render(reference, phase);
                Assert.That(expected.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
                Assert.That(pixels.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.00001f),
                    "Compact and baked paths must use the same original pose samples, including after LRU eviction.");
            }

            Vector4[] Render(EffekseerVfxPreview preview, float time)
            {
                preview.Seek(time, Matrix.Identity);
                using var target = new RenderTarget2D(graphics.GraphicsDevice, 240, 120, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                var eye = new Vector3(3, 1.5f, 10);
                var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, new(3, 1.5f, 0), Vector3.Up),
                    Matrix.CreateOrthographic(6, 3, .1f, 100), eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
                graphics.GraphicsDevice.SetRenderTarget(target); graphics.GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); graphics.GraphicsDevice.SetRenderTarget(null);
                var pixels = new Vector4[240 * 120]; target.GetData(pixels); return pixels;
            }
        }
        finally { graphics.Release(); }
    }

    [Test]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void OneDraw_InterleavedFramesAndAliasesMatchIndependentBakedGeometry()
    {
        WithRawDevice(device =>
        {
            // DIAG: one shared compact palette, its alias, and an independent model in the same draw.
            using var actual = CreateRawPreview(device, true);
            using var reference = CreateRawPreview(device, false);
            int[] frames = [0, 1, 2, 3, 4, 4, 5, 6, 0, 7, 0, 3, 1, 2, 4, 0];
            int[] layers = [0, 1, 0, 1, 2, 1, 0, 1, 2, 0, 1, 2, 0, 1, 0, 1];
            var particles = frames.Select((frame, index) => RawParticle(frame, layers[index],
                new((index % 4 - 1.5f) * 1.5f, (1.5f - index / 4) * 1.2f, 0))).ToArray();
            foreach (var reverse in new[] { false, true })
            {
                var ordered = reverse ? particles.Reverse().ToArray() : particles;
                var pixels = RenderRaw(device, actual, ordered);
                var expected = RenderRaw(device, reference, ordered);
                for (var tile = 0; tile < 16; ++tile)
                {
                    var left = tile % 4 * 160; var top = tile / 4 * 128;
                    var covered = Enumerable.Range(top, 128).Sum(y => Enumerable.Range(left, 160)
                        .Count(x => expected[y * RawWidth + x].X + expected[y * RawWidth + x].Y + expected[y * RawWidth + x].Z > .05f));
                    Assert.That(covered, Is.GreaterThan(100), $"Every independent frame/alias tile must be visible: {tile}.");
                }
                Assert.That(pixels.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.00001f),
                    "The same draw must retain each requested frame while aliases evict and revisit the shared four-frame cache.");
                var wrongPhase = ordered.Select(particle => { particle.AppearancePhase = 0; return particle; }).ToArray();
                var control = RenderRaw(device, reference, wrongPhase);
                Assert.That(expected.Zip(control).Count(pair => Vector4.Distance(pair.First, pair.Second) > .05f), Is.GreaterThan(1000),
                    "The reference must distinguish the requested poses from drawing every particle at frame zero.");
            }
        });
    }

    [Test]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void FailedFifthFrame_DoesNotLeaveAnEvictedAliasAsAFalseCacheHit()
    {
        WithRawDevice(device =>
        {
            // DIAG: a finite zero palette reaches the real normal validation without a failure hook.
            using var actual = CreateRawPreview(device, true, true);
            using var reference = CreateRawPreview(device, false);
            for (var frame = 0; frame < 4; ++frame)
            {
                var warmup = RenderRaw(device, actual, [RawParticle(frame, frame % 2)]);
                Assert.That(warmup.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
            }
            RenderRaw(device, actual, [RawParticle(4, 1)], true);
            foreach (var (frame, layer) in new[] { (0, 1), (3, 0), (1, 1) })
            {
                var particle = RawParticle(frame, layer);
                var pixels = RenderRaw(device, actual, [particle]);
                var expected = RenderRaw(device, reference, [particle]);
                Assert.That(expected.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
                Assert.That(pixels.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.00001f),
                    "After failed preparation the same handle must rebuild an evicted frame and retain the other cached frames.");
            }
        });
    }

    private const int RawFrames = 8, RawWidth = 640, RawHeight = 512;

    private static EffekseerHandle CreateRawPreview(GraphicsDevice device, bool compact, bool invalidFrame = false)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        try
        {
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [255, 255, 255, 255], 4, 1, 1, 0));
            Upload(.25f);
            EffekseerNative.Check(EffekseerNative.AecnVfxModelAlias(handle, 0));
            Upload(.1875f);
            var layers = Enumerable.Range(0, 3).Select(index =>
            {
                var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = index;
                return layer;
            }).ToArray();
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, layers, layers.Length));
            return handle;

            void Upload(float halfWidth)
            {
                var vertices = new[] { new Vector3(-halfWidth, -.25f, 0), new(halfWidth, -.25f, 0),
                    new(-halfWidth, .25f, 0), new(halfWidth, .25f, 0) }
                    .Select(position => new EffekseerVertex { Position = position, Normal = Vector3.Backward,
                        Tangent = Vector3.Right, Binormal = Vector3.Up, UV = Vector2.Zero }).ToArray();
                int[] indices = [0, 1, 2, 2, 1, 3];
                if (compact)
                {
                    var weights = Enumerable.Repeat(new EffekseerSkinWeight { Weights = new(1, 0, 0, 0) }, vertices.Length).ToArray();
                    var poses = Enumerable.Range(0, RawFrames).Select(frame => invalidFrame && frame == 4 ? new Matrix()
                        : Matrix.CreateTranslation(frame / 16f, frame % 3 / 32f, 0)).ToArray();
                    EffekseerNative.Check(EffekseerNative.AecnVfxModelSkinned(handle, vertices, vertices.Length, indices, indices.Length,
                        weights, Marshal.SizeOf<EffekseerSkinWeight>(), poses, poses.Length, 1, RawFrames, 1, 0, 0, 0));
                }
                else
                {
                    // Independent analytic geometry: no compact sampler or skinning helper is used by the oracle.
                    var baked = Enumerable.Range(0, RawFrames).SelectMany(frame => vertices.Select(vertex =>
                    {
                        vertex.Position += new Vector3(frame / 16f, frame % 3 / 32f, 0);
                        return vertex;
                    })).ToArray();
                    EffekseerNative.Check(EffekseerNative.AecnVfxModelFrames(handle, baked, baked.Length, vertices.Length,
                        RawFrames, indices, indices.Length, 1));
                }
            }
        }
        catch { handle.Dispose(); throw; }
    }

    private static EffekseerParticle RawParticle(int frame, int layer, Vector3 offset = default) => new()
    {
        Layer = layer, Transform = Matrix.CreateTranslation(offset), UV = new(0, 0, 1, 1), AppearancePhase = frame / (float)RawFrames,
        Colour = layer switch { 0 => new(1, .35f, .2f, 1), 1 => new(.2f, 1, .35f, 1), _ => new(.35f, .2f, 1, 1) },
    };

    private static Vector4[] RenderRaw(GraphicsDevice device, EffekseerHandle handle, EffekseerParticle[] particles, bool expectFailure = false)
    {
        using var target = new RenderTarget2D(device, RawWidth, RawHeight, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreateOrthographic(6, 4.8f, .1f, 100);
        int result;
        try { result = EffekseerNative.AecnVfxDraw(handle, MatrixValues(view), MatrixValues(projection), particles, particles.Length); }
        finally { device.SetRenderTarget(null); }
        if (expectFailure)
        {
            Assert.That(result, Is.Zero, "The deliberately degenerate fifth pose must reach real native preparation failure.");
            var exception = Assert.Throws<InvalidOperationException>(() => EffekseerNative.Check(result));
            Assert.That(exception!.Message, Does.Contain("Invalid animated normal"));
            return [];
        }
        EffekseerNative.Check(result);
        var pixels = new Vector4[RawWidth * RawHeight]; target.GetData(pixels); return pixels;
    }

    private static float[] MatrixValues(Matrix matrix) => [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44];

    private static void WithRawDevice(Action<GraphicsDevice> action)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try { action(graphics.GraphicsDevice); }
        finally { graphics.Release(); }
    }

    private static VfxConversion Convert(byte[] source, byte[]? clip = null) => TotalWarVfxConverter.Convert(new(source, "DIAG/model-animation.xml",
        path => clip != null && path.Replace('\\', '/') == "animations/animated.anim" ? clip : VfxModelAnimationTests.Resolve(path)));
    private static string Reasons(VfxConversion conversion) => string.Join(';', conversion.Diagnostics.Select(item => item.Field + "/" + item.ReasonKey));
}
