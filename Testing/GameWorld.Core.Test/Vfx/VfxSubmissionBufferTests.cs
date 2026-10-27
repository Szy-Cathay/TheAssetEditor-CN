#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxSubmissionBufferTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void GrowthShrinkAndEmptyRetainCapacityWithoutSubmittingOldTail()
    {
        using var preview = Preview(); var prepare = Prepare(preview);
        var large = Frame(17); var count = prepare(large); var buffer = Buffer(preview);
        Assert.That(count, Is.EqualTo(51)); AssertBytes(buffer.AsSpan(0,count), Expected(large));
        var shortFrame = Frame(1);
        Assert.That(prepare(shortFrame), Is.EqualTo(3));
        Assert.That(Buffer(preview), Is.SameAs(buffer), "A smaller frame must reuse allocated packet storage.");
        AssertBytes(buffer.AsSpan(0,3), Expected(shortFrame));
        Assert.That(prepare([]), Is.Zero, "Capacity is never the native submission count.");
        Assert.That(Buffer(preview), Is.SameAs(buffer));
        Assert.That(prepare(Frame(2)), Is.EqualTo(6)); AssertBytes(buffer.AsSpan(0,6), Expected(Frame(2)));
    }

    [Test]
    public void StableWarmSubmissionAllocatesNoNewListsOrPacketArrays()
    {
        using var preview = Preview(); var prepare = Prepare(preview); var frame = Frame(1024);
        for (var i = 0; i < 10; ++i) prepare(frame);
        var buffer = Buffer(preview); var before = GC.GetAllocatedBytesForCurrentThread(); var total = 0;
        for (var i = 0; i < 64; ++i) total += prepare(frame);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(64*3072));
            Assert.That(Buffer(preview), Is.SameAs(buffer));
            Assert.That(allocated, Is.Zero, "Measure the real production expansion method after warming; no reflection or assertion is in this interval.");
        });
    }

    [Test]
    public void IndependentInstancesNeverShareMutableSubmissionStorage()
    {
        using var first = Preview(); using var second = Preview();
        var prepareFirst = Prepare(first); var prepareSecond = Prepare(second);
        prepareFirst(Frame(4)); var original = Buffer(first).AsSpan(0,12).ToArray();
        prepareSecond(Frame(8)); prepareSecond(Frame(1)); prepareSecond([]);
        Assert.That(Buffer(first), Is.Not.SameAs(Buffer(second)));
        AssertBytes(Buffer(first).AsSpan(0,12), original);
    }

    [Test]
    public void ExpansionDoesNotMutateTheCallerOwnedProjectedSnapshot()
    {
        using var preview = Preview(); var prepare = Prepare(preview); var frame = Frame(4);
        var before = MemoryMarshal.AsBytes(frame.AsSpan()).ToArray();
        prepare(frame); prepare(Frame(8)); prepare(frame);
        Assert.That(MemoryMarshal.AsBytes(frame.AsSpan()).ToArray(),Is.EqualTo(before),
            "Native render-layer IDs and tint multiplication belong in submission storage, never in a caller's projected snapshot.");
    }

    private static void AssertBytes(ReadOnlySpan<EffekseerParticle> actual, ReadOnlySpan<EffekseerParticle> expected)
        => Assert.That(MemoryMarshal.AsBytes(actual).ToArray(), Is.EqualTo(MemoryMarshal.AsBytes(expected).ToArray()));

    private static VfxPreviewParticle[] Frame(int count) => Enumerable.Range(0,count).Select(index => new VfxPreviewParticle(new()
    {
        Layer = 0, Transform = Matrix.CreateTranslation(index,2,3), Colour = new(1,2,3,.25f),
        UV = new(.1f,.2f,.3f,.4f), Time = index+.125f, MaterialTime = index+.25f, AppearancePhase = .375f,
        AlphaCrush = new(.05f,1), NormalRotation = new(.2f,.3f,.4f,.5f), SoftParticleScale = .75f,
    },0)).ToArray();

    private static EffekseerParticle[] Expected(VfxPreviewParticle[] frame)
    {
        var opaque = new List<EffekseerParticle>(); var transparent = new List<EffekseerParticle>();
        foreach (var item in frame)
        {
            var coverage = item.Render; coverage.Colour.X *= .5f; coverage.Colour.Y *= .5f; coverage.Colour.Z *= .5f;
            coverage.Layer = 10; opaque.Add(coverage);
            coverage.Layer = 11; transparent.Add(coverage);
            var emission = item.Render; emission.Layer = 12; emission.Colour.X *= 2; emission.Colour.Y *= 2; emission.Colour.Z *= 2;
            transparent.Add(emission);
        }
        return [..opaque,..transparent];
    }

    private static EffekseerVfxPreview Preview()
    {
        // DIAG packet expansion only: no texture, game resolver, renderer or GPU is constructed.
        var curve = new VfxPreviewCurve(null,"x",1);
        var definition = new VfxPreviewEmitter
        {
            Name = "DIAG split model parts", Render = EffekseerLayer.Create(),
            Emission = curve, Life = curve, Scale = curve, InitialScale = curve, InitialAlpha = curve,
            Position = curve, Orientation = curve, Speed = curve, SpawnBox = curve, SpawnRadius = curve,
            Colour = curve, Alpha = curve, Force = curve, Deceleration = curve,
            CoveredColourMultiplier = .5f, AdditiveMultiplier = 2,
        };
        var conversion = new VfxConversion(); conversion.Emitters.Add(definition);
        var preview = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_conversion",Private)!.SetValue(preview,conversion);
        var mappingType = typeof(EffekseerVfxPreview).GetNestedType("RenderLayers",BindingFlags.NonPublic)!;
        var mapping = Activator.CreateInstance(mappingType, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
            null,[new[]{10,11},new[]{-1,12}],null)!;
        var variants = Array.CreateInstance(mappingType,1); variants.SetValue(mapping,0);
        var mappings = Array.CreateInstance(variants.GetType(),1); mappings.SetValue(variants,0);
        typeof(EffekseerVfxPreview).GetField("_renderLayers",Private)!.SetValue(preview,mappings);
        ((HashSet<int>)typeof(EffekseerVfxPreview).GetField("_opaqueLayers",Private)!.GetValue(preview)!).Add(10);
        return preview;
    }

    private static Func<VfxPreviewParticle[],int> Prepare(EffekseerVfxPreview preview)
    {
        var method = typeof(EffekseerVfxPreview).GetMethod("PrepareSubmission",Private);
        Assert.That(method, Is.Not.Null, "Invoke the actual draw-path packet expansion, not a copy of its implementation.");
        return method!.CreateDelegate<Func<VfxPreviewParticle[],int>>(preview);
    }
    private static EffekseerParticle[] Buffer(EffekseerVfxPreview preview)
        => (EffekseerParticle[])typeof(EffekseerVfxPreview).GetField("_submissionParticles",Private)!.GetValue(preview)!;
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSubmissionBufferPixelTests
{
    private readonly VfxPlaybackResourceSharingPixelTests _official = new();
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up),
        Matrix.CreateOrthographic(8,8,.1f,100),new(0,0,10),Vector3.Zero,0,0,0,1,Vector3.One,[]);
    [OneTimeSetUp]
    public void ReuseOriginalOfficialDiffuseLoader()
        => _official.ReadTheFullOfficialDiffuseWithoutEditingTexelsOrMips();

    [TestCase(0)]
    [TestCase(4)]
    public void SameSceneReusedBufferKeepsEarlierLargeSnapshotAndLaterSmallSubmission(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device); using var second = Preview(device); using var reused = Preview(device);
            first.Seek(.7f,Matrix.CreateTranslation(-1,0,0)); second.Seek(.2f,Matrix.CreateTranslation(1,0,0));
            var expected = Render(device,samples,scope => { first.Draw(Camera,scope); second.Draw(Camera,scope); },first);
            reused.Seek(.7f,Matrix.CreateTranslation(-1,0,0));
            var actual = Render(device,samples,scope =>
            {
                reused.Draw(Camera,scope);
                reused.Seek(0,Matrix.CreateTranslation(1,0,0)); reused.Seek(.2f,Matrix.CreateTranslation(1,0,0));
                reused.Draw(Camera,scope);
            },reused);
            Assert.That(actual, Is.EqualTo(expected), "The second smaller write must not overwrite native's retained first-frame snapshot or submit stale capacity.");
            Assert.That(actual, Is.Not.EqualTo(Render(device,samples,scope => second.Draw(Camera,scope),second)),
                "Both separated submissions must make a real visible contribution.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void GrowthShrinkClearAndPlaybackCloneKeepTheirOwnPackets()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var owner = Preview(device); using var reference = Preview(device);
            using var clone = owner.CreatePlaybackInstance(); Assert.That(clone,Is.Not.Null);
            foreach (var t in new[]{.2f,.7f,.2f})
            {
                owner.Seek(t,Matrix.Identity); reference.Seek(t,Matrix.Identity);
                Assert.That(Render(device,4,scope => owner.Draw(Camera,scope),owner),
                    Is.EqualTo(Render(device,4,scope => reference.Draw(Camera,scope),reference)));
            }
            clone!.Seek(.5f,Matrix.CreateTranslation(1,0,0));
            Render(device,4,scope => clone.Draw(Camera,scope),clone);
            Assert.That(Read(owner,"_submissionParticles"),Is.Not.SameAs(Read(clone,"_submissionParticles")));
            owner.EmissionEndTimeSeconds = .21f; owner.Seek(3,Matrix.Identity);
            Assert.That(owner.ParticleCount,Is.Zero);
            Assert.That(Render(device,4,scope => owner.Draw(Camera,scope),owner),Is.All.EqualTo(Color.Black),
                "An empty current frame must not replay particles from a larger retained buffer.");
            Assert.That(clone.ParticleCount,Is.GreaterThan(0));
        }
        finally { graphics.Release(); }
    }
    private static object? Read(object preview,string name)
        => typeof(EffekseerVfxPreview).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(preview);
    private EffekseerVfxPreview Preview(GraphicsDevice device)
        => (EffekseerVfxPreview)typeof(VfxPlaybackResourceSharingPixelTests).GetMethod("Preview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_official,[device])!;
    private static Color[] Render(GraphicsDevice device,int samples,Action<IDisposable?> draw,IVfxPreview owner)
    {
        using var target = new RenderTarget2D(device,96,96,false,SurfaceFormat.Color,DepthFormat.Depth24,samples,RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);
        using (var scope = owner.BeginScene(Camera)) draw(scope);
        device.SetRenderTarget(null); var pixels = new Color[96*96]; target.GetData(pixels); return pixels;
    }
}
