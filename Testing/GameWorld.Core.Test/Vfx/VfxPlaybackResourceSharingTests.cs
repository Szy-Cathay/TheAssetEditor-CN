#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxPlaybackFirstPrepareTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void NewCohort_VisibilityDoesNotInitializeItsSimulationBeforeTheRealAttachment(bool retire)
    {
        var (node, factory) = Node();
        using (node)
        {
            node.PrepareFrame(Matrix.Identity);
            var original = factory.Previews.Single();
            if (retire)
            {
                original.Drained = true;
                node.RetireCurrentPlayback(.5f);
                node.CurrentPlaybackEnabled = false;
                node.PrepareFrame(Matrix.Identity);
            }
            else node.ResetPlayback();
            var clone = factory.Previews.Last();
            node.CurrentPlaybackEnabled = true;
            Assert.That(node.HasPlaybackContent, Is.True);
            Assert.That(clone.EarlyDrainQueries, Is.Zero, "Visibility must not reset a fresh simulation with the identity attachment.");
            var attachment = Matrix.CreateTranslation(3, 2, 1);
            node.PrepareFrame(attachment);
            Assert.That(clone.FirstAttachment, Is.EqualTo(attachment), "The first real Seek must initialize the new cohort using its authored attachment.");
        }
    }

    [Test]
    public void PreparedCohort_StillHidesAfterItHasActuallyDrained()
    {
        var (node, factory) = Node();
        using (node)
        {
            node.PrepareFrame(Matrix.CreateTranslation(3, 2, 1));
            var current = factory.Previews.Single();
            Assert.That(current.EarlyDrainQueries, Is.Zero);
            Assert.That(node.HasPlaybackContent, Is.True);
            current.Drained = true;
            Assert.That(node.HasPlaybackContent, Is.False, "Deferring the first query must not keep a genuinely drained current cohort visible.");
        }
    }

    private static (VfxPreviewSceneNode Node, RecordingFactory Factory) Node()
    {
        var files = new Mock<IPackFileService>();
        var file = PackFile.CreateFromBytes("empty.xml", VfxDocument.CreateComposition().Write());
        files.Setup(service => service.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\empty.xml");
        var source = new VfxPreviewDocumentSource(files.Object, file, new());
        var factory = new RecordingFactory();
        return (new(factory, Mock.Of<IDeviceResolver>(), source), factory);
    }

    private sealed class RecordingFactory : IVfxPreviewFactory
    {
        public List<RecordingPreview> Previews { get; } = [];
        public IVfxPreview Create(GraphicsDevice device) => New();
        public RecordingPreview New() { var preview = new RecordingPreview(this); Previews.Add(preview); return preview; }
    }

    private sealed class RecordingPreview(RecordingFactory factory) : IVfxPreview
    {
        private bool _initialized, _prepared;
        public bool Drained { get; set; }
        public int EarlyDrainQueries { get; private set; }
        public Matrix FirstAttachment { get; private set; }
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => _prepared && !Drained ? 1 : 0;
        public float? EmissionEndTimeSeconds { get; set; }
        public bool IsDrained
        {
            get
            {
                if (!_prepared)
                {
                    EarlyDrainQueries++;
                    if (!_initialized) { _initialized = true; FirstAttachment = Matrix.Identity; }
                }
                return Drained;
            }
        }
        public IVfxPreview? CreatePlaybackInstance() => factory.New();
        public void Reload(VfxPreviewSnapshot snapshot) { _initialized = _prepared = false; }
        public void Advance(float seconds, Matrix attachment) => Seek(seconds, attachment);
        public void Seek(float seconds, Matrix attachment)
        {
            if (!_initialized) { _initialized = true; FirstAttachment = attachment; }
            _prepared = true;
        }
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
}

public class VfxPlaybackResourceSharingTests
{
    [Test]
    public void UnloadedPreview_DoesNotPretendThatResourcesAreReady()
    {
        using var preview = new EffekseerVfxPreview(null!);
        Assert.That(preview.CreatePlaybackInstance(), Is.Null);
    }

    [Test]
    public void EmptyPlayback_SharesOnlyTheCompletedConversionAndStartsAnIndependentClock()
    {
        using var owner = Empty();
        owner.EmissionEndTimeSeconds = .5f; owner.Seek(.8f, Matrix.Identity);
        using var clone = owner.CreatePlaybackInstance();
        Assert.That(clone, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(Read(owner, "_conversion"), Is.SameAs(Read(clone!, "_conversion")));
            Assert.That(Read(owner, "_simulation"), Is.Not.SameAs(Read(clone!, "_simulation")));
            Assert.That(clone!.EmissionEndTimeSeconds, Is.Null);
            Assert.That(clone.IsDrained, Is.True);
        });
        clone!.Seek(.1f, Matrix.Identity);
        Assert.That(Read(Read(owner, "_simulation")!, "Time"), Is.EqualTo(.8f));
        Assert.That(Read(Read(clone, "_simulation")!, "Time"), Is.EqualTo(.1f));
    }

    [Test]
    public void ReloadAndCreatorDisposal_DoNotChangeTheOtherPlaybackResources()
    {
        using var owner = Empty(); using var clone = owner.CreatePlaybackInstance();
        Assert.That(clone, Is.Not.Null);
        var original = Read(clone!, "_conversion");
        owner.Reload(new(VfxDocument.CreateComposition().Write(), "vfx/new-empty.xml", _ => null));
        Assert.That(Read(owner, "_conversion"), Is.Not.SameAs(original));
        Assert.That(Read(clone!, "_conversion"), Is.SameAs(original));
        owner.Dispose();
        clone!.Seek(2, Matrix.Identity);
        using var next = clone.CreatePlaybackInstance();
        Assert.That(next, Is.Not.Null);
        Assert.That(Read(next!, "_conversion"), Is.SameAs(original));
        Assert.Throws<ObjectDisposedException>(() => owner.CreatePlaybackInstance());
    }

    private static EffekseerVfxPreview Empty()
    {
        var preview = new EffekseerVfxPreview(null!);
        preview.Reload(new(VfxDocument.CreateComposition().Write(), "vfx/empty.xml", _ => null));
        return preview;
    }

    internal static object? Read(object owner, string name)
        => owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner)
            ?? owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner);
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxPlaybackResourceSharingPixelTests
{
    private const string TexturePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private byte[] _texture = [];
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(8, 8, .1f, 100), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [OneTimeSetUp]
    public void ReadTheFullOfficialDiffuseWithoutEditingTexelsOrMips()
    {
        _official.LoadCurrentOfficialResourcesOnly();
        _texture = (byte[])typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_official, [TexturePath])!;
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new
        {
            Boundary = "DIAG independent playback clocks over the complete unchanged official DDS. No crop, synthesized image, pixel edit or complete-effect visual acceptance claim.",
            TexturePath, Sha256 = Convert.ToHexStringLower(SHA256.HashData(_texture)),
            Origins = typeof(VfxOfficialDistortionPixelTests).GetField("_origins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_official),
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedHandleCohorts_InOneSceneArePixelIdenticalToIndependentHandles(bool reverse)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device); using var independent = Preview(device);
            using var clone = first.CreatePlaybackInstance(); Assert.That(clone, Is.Not.Null);
            first.Seek(.2f, Matrix.CreateTranslation(-1, 0, 0));
            independent.Seek(.7f, Matrix.CreateTranslation(1, 0, 0)); clone!.Seek(.7f, Matrix.CreateTranslation(1, 0, 0));
            Assert.That(Handle(first), Is.SameAs(Handle(clone)));
            Assert.That(Handle(first), Is.Not.SameAs(Handle(independent)));
            var baseline = Render(device, reverse ? [independent, first] : [first, independent]);
            var shared = Render(device, reverse ? [clone, first] : [first, clone]);
            Assert.That(shared, Is.EqualTo(baseline), "Every pixel must retain both independently timed submissions, including same-handle native queue snapshots.");
            Assert.That(shared, Is.Not.EqualTo(Render(device, [first])), "The second cohort must make a real visible contribution.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void Clone_ResolvesNoDependenciesAndSurvivesCreatorDisposalUntilItsLastLease()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var reads = 0;
            using var owner = new EffekseerVfxPreview(device);
            owner.Reload(new(Source(Vector3.One), "vfx/official-texture-owner.xml", _ => { reads++; return _texture; }));
            var oldReads = reads; var handle = Handle(owner);
            using var clone = owner.CreatePlaybackInstance(); Assert.That(clone, Is.Not.Null);
            Assert.That(reads, Is.EqualTo(oldReads), "Cloning must not reconvert, reread or reupload the original resource.");
            Assert.That(VfxPlaybackResourceSharingTests.Read(owner, "_simulation"), Is.Not.SameAs(VfxPlaybackResourceSharingTests.Read(clone!, "_simulation")));
            owner.Dispose();
            Assert.That(handle.IsClosed, Is.False);
            clone!.Seek(.6f, Matrix.Identity);
            Assert.That(Render(device, [clone]).Any(p => p.R > 20), Is.True);
            clone.Dispose();
            Assert.That(handle.IsClosed, Is.True, "The final playback lease releases the shared native handle.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void Reload_DetachesOnlyItsOwnLeaseAndPreservesTheOtherCohortPixels()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var owner = Preview(device);
            using var clone = owner.CreatePlaybackInstance(); Assert.That(clone, Is.Not.Null);
            owner.Seek(.5f, Matrix.Identity); clone!.Seek(.2f, Matrix.Identity);
            var before = Render(device, [owner]); var original = Handle(owner);
            clone.Reload(new(Source(new(0, 1, 0)), "vfx/reloaded-cohort.xml", _ => _texture));
            clone.Seek(.8f, Matrix.Identity);
            Assert.Multiple(() =>
            {
                Assert.That(Handle(owner), Is.SameAs(original));
                Assert.That(Handle(clone), Is.Not.SameAs(original));
                Assert.That(original.IsClosed, Is.False);
                Assert.That(Render(device, [owner]), Is.EqualTo(before));
                Assert.That(VfxPlaybackResourceSharingTests.Read(VfxPlaybackResourceSharingTests.Read(owner, "_simulation")!, "Time"), Is.EqualTo(.5f));
            });
            owner.Dispose(); Assert.That(original.IsClosed, Is.True);
            Assert.That(Render(device, [clone]).Any(p => p.G > 20), Is.True);
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void QueuedSameHandleCohorts_SurviveDisposalOfEveryPlaybackBeforeEndScene()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var owner = Preview(device); using var independent = Preview(device);
            using var clone = owner.CreatePlaybackInstance(); Assert.That(clone, Is.Not.Null);
            owner.Seek(.2f, Matrix.CreateTranslation(-1, 0, 0));
            independent.Seek(.7f, Matrix.CreateTranslation(1, 0, 0)); clone!.Seek(.7f, Matrix.CreateTranslation(1, 0, 0));
            var expected = Render(device, [owner, independent]); var handle = Handle(owner);
            using var target = new RenderTarget2D(device, 96, 96, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black);
            using (var scene = owner.BeginScene(Camera))
            {
                Assert.That(scene, Is.Not.Null); owner.Draw(Camera, scene); clone.Draw(Camera, scene);
                owner.Dispose(); clone.Dispose();
            }
            device.SetRenderTarget(null); var actual = new Color[96 * 96]; target.GetData(actual);
            Assert.That(actual, Is.EqualTo(expected), "Native queued packet copies and scene ownership must outlive all managed playback owners.");
            Assert.That(handle.IsClosed, Is.True);
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void MissingOriginalDependency_DoesNotOfferACloneOfFailedResources()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var failed = new EffekseerVfxPreview(graphics.GraphicsDevice);
            // Missing original dependency, without substituting any image bytes.
            failed.Reload(new(Source(Vector3.One), "vfx/missing-official-dependency.xml", _ => null));
            Assert.That(failed.CreatePlaybackInstance(), Is.Null);
            Assert.That(failed.Diagnostics, Is.Not.Empty);
        }
        finally { graphics.Release(); }
    }

    private EffekseerVfxPreview Preview(GraphicsDevice device)
    {
        var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(Source(Vector3.One), "vfx/official-texture-cohort.xml", _ => _texture));
        Assert.That(preview.EmitterCount, Is.EqualTo(1));
        Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
        return preview;
    }
    private static byte[] Source(Vector3 colour)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(colour, true, 1, rate: 8));
        document.Root.Descendants("texture_property").Single().SetAttributeValue("path", TexturePath);
        return document.Write();
    }
    private static EffekseerHandle Handle(IVfxPreview preview) => (EffekseerHandle)VfxPlaybackResourceSharingTests.Read(preview, "_handle")!;
    private static Color[] Render(GraphicsDevice device, IVfxPreview[] previews)
    {
        using var target = new RenderTarget2D(device, 96, 96, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black);
        using (var scene = previews.First().BeginScene(Camera))
            foreach (var preview in previews) preview.Draw(Camera, scene);
        device.SetRenderTarget(null); var pixels = new Color[96 * 96]; target.GetData(pixels); return pixels;
    }
}
