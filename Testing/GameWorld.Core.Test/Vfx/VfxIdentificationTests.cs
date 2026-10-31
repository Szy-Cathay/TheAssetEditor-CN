#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxIdentificationTests
{
    private static readonly VfxPreviewLayerIdentity Own = new("Emitter", "visible");
    private static readonly VfxPreviewLayerIdentity FirstReference = new("Reference", "visible");
    private static readonly VfxPreviewLayerIdentity SecondReference = new("Reference", "visible", 1);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(2.4f, 2.4f, .1f, 100), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [],
        VfxGroundHeight: 0);

    [TestCase(false)]
    [TestCase(true)]
    public void SwitchingIdentificationDoesNotReloadSeekPrepareOrChangePlayback(bool playing)
    {
        var preview = new RecordingPreview();
        using var component = Create(() => preview);
        component.Reload(FakeSnapshot()); Update(component);
        component.Seek(2.5f); Update(component);
        component.IsPlaying = playing;
        component.Loop = false;
        var playbackChanges = 0; var attachmentPreparations = 0;
        component.PlaybackChanged += () => playbackChanges++;
        component.PreparingFrame += (_, _) => attachmentPreparations++;
        var before = (preview.Reloads, preview.Seeks, preview.Preparations, component.CurrentTime, component.Duration);
        var identificationStart = preview.Identifications.Count;

        component.BeginIdentification(Own);
        component.BeginIdentification(FirstReference);
        component.BeginIdentification(SecondReference);
        component.EndIdentification();

        Assert.Multiple(() =>
        {
            Assert.That(preview.Identifications.Skip(identificationStart), Is.EqualTo(new VfxPreviewLayerIdentity?[] { Own, FirstReference, SecondReference, null }));
            Assert.That((preview.Reloads, preview.Seeks, preview.Preparations, component.CurrentTime, component.Duration), Is.EqualTo(before));
            Assert.That(component.IsPlaying, Is.EqualTo(playing));
            Assert.That(component.Loop, Is.False);
            Assert.That(playbackChanges, Is.Zero);
            Assert.That(attachmentPreparations, Is.Zero);
        });
        Update(component);
        Assert.That(preview.Reloads, Is.EqualTo(before.Reloads), "A later normal update must not turn identification into a queued reload.");
        Assert.That(preview.Seeks, Is.EqualTo(before.Seeks + 1), "Only the ordinary zero-delta frame seek may occur.");
        Assert.That(component.CurrentTime, Is.EqualTo(before.CurrentTime));
    }

    [Test]
    public void IdentificationSurvivesLiveReloadAndStopCancelsItForTheNextPreview()
    {
        var first = new RecordingPreview(); var second = new RecordingPreview(); var creations = 0;
        using var component = Create(() => creations++ == 0 ? first : second);
        component.BeginIdentification(SecondReference);
        Assert.That(creations, Is.Zero, "Identifying before a preview exists must not create graphics resources.");
        component.Reload(FakeSnapshot()); Update(component);
        Assert.That(first.LastIdentification, Is.EqualTo(SecondReference));
        component.Seek(2.5f); Update(component); component.IsPlaying = false;
        component.Reload(FakeSnapshot(2), preservePlaybackState: true); Update(component);
        Assert.Multiple(() =>
        {
            Assert.That(first.Reloads, Is.EqualTo(2));
            Assert.That(first.LastIdentification, Is.EqualTo(SecondReference), "Reload resets the fake filter, so the component must replay its active identification afterward.");
            Assert.That(component.CurrentTime, Is.EqualTo(2.5f));
            Assert.That(component.IsPlaying, Is.False);
        });
        component.Stop();
        var firstCalls = first.Identifications.Count;
        component.EndIdentification();
        component.Reload(FakeSnapshot()); Update(component);
        Assert.Multiple(() =>
        {
            Assert.That(first.Disposed, Is.True);
            Assert.That(first.Identifications, Has.Count.EqualTo(firstCalls), "Ending identification after Stop must not access the disposed preview.");
            Assert.That(second.LastIdentification, Is.Null);
            Assert.That(creations, Is.EqualTo(2));
        });
    }

    [Test]
    public void DisposedComponentIgnoresLateIdentificationCallbacks()
    {
        var preview = new RecordingPreview();
        var component = Create(() => preview);
        component.Reload(FakeSnapshot()); Update(component);
        component.BeginIdentification(Own);
        component.Dispose();
        var calls = preview.Identifications.Count;
        Assert.DoesNotThrow(() => { component.BeginIdentification(FirstReference); component.EndIdentification(); });
        Assert.That(preview.Identifications, Has.Count.EqualTo(calls));
    }

    [TestCase("", false)]
    [TestCase("", true)]
    [TestCase("FULLRES_DEFERRED_ALPHA", false)]
    [TestCase("FULLRES_DEFERRED_ALPHA", true)]
    [TestCase("HALFRES_DEFERRED_ALPHA", false)]
    [TestCase("HALFRES_DEFERRED_ALPHA", true)]
    public void IdentificationMatchesOneRootAndEndingRestoresTheCompleteFrame(string pass, bool sharedScene) => WithGraphics((device, original) =>
    {
        var (document, child) = Composition(pass);
        var complete = Snapshot(document.Write(), child, original);
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(complete); preview.Seek(.2f, Matrix.Identity);
        var baseline = Render(device, preview, sharedScene);
        var simulation = Field(preview, "_simulation"); var handle = Field(preview, "_handle");
        var bounds = preview.PreviewBounds; var particles = preview.ParticleCount;
        Assert.That(particles, Is.EqualTo(3));
        foreach (var identity in new[] { Own, FirstReference, SecondReference })
        {
            preview.Identify(identity);
            var isolated = Render(device, preview, sharedScene);
            using var control = new EffekseerVfxPreview(device);
            control.Reload(Snapshot(SingleRoot(document, identity), child, original)); control.Seek(.2f, Matrix.Identity);
            AssertPixels(Render(device, control, sharedScene), isolated);
            Assert.That(isolated.SequenceEqual(baseline), Is.False, "The known three separated roots must visibly isolate one root.");
            Assert.Multiple(() =>
            {
                Assert.That(Field(preview, "_simulation"), Is.SameAs(simulation));
                Assert.That(Field(preview, "_handle"), Is.SameAs(handle));
                Assert.That(preview.ParticleCount, Is.EqualTo(particles));
                Assert.That(preview.PreviewBounds, Is.EqualTo(bounds));
                Assert.That(((TotalWarVfxSimulation)simulation).Time, Is.EqualTo(.2f));
            });
            preview.Identify(null);
            AssertPixels(baseline, Render(device, preview, sharedScene));
        }
        preview.Identify(SecondReference);
        using var independent = (EffekseerVfxPreview)preview.CreatePlaybackInstance()!;
        independent.Seek(.2f, Matrix.Identity);
        AssertPixels(baseline, Render(device, independent, sharedScene));
        preview.Identify(new("Reference", "missing-root"));
        Assert.That(Render(device, preview, sharedScene, requireVisible: false).All(pixel => pixel == new Vector4(0, 0, 0, 1)), Is.True,
            "An unknown root must not silently display the complete effect.");
        preview.Seek(.6f, Matrix.Identity); preview.Identify(null);
        using var uninterrupted = new EffekseerVfxPreview(device);
        uninterrupted.Reload(complete); uninterrupted.Seek(.6f, Matrix.Identity);
        AssertPixels(Render(device, uninterrupted, sharedScene), Render(device, preview, sharedScene));
    });

    [Test]
    public void EndingIdentificationRestoresOnlyTheOriginallyLoadedPreviewScope() => WithGraphics((device, original) =>
    {
        var (document, child) = Composition("");
        var snapshot = Snapshot(document.Write(), child, original) with { RootSelection = new(["visible"], []) };
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(snapshot); preview.Seek(.2f, Matrix.Identity);
        var baseline = Render(device, preview, false);
        Assert.That(preview.EmitterCount, Is.EqualTo(1));
        preview.Identify(FirstReference);
        Render(device, preview, false, requireVisible: false);
        preview.Identify(null);
        Assert.That(preview.EmitterCount, Is.EqualTo(1));
        AssertPixels(baseline, Render(device, preview, false));
    });

    [Test]
    public void IdentifyingAnEventHelperKeepsItsDifferentlyNamedEventChild() => WithGraphics((device, original) =>
    {
        var document = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var child = Leaf(""); var childBytes = child.Write();
        var own = Leaf("");
        own.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "EmitterPosition"), VectorCurve(-.65f, 0, 0)));
        document.Root.Element("vfx")!.Element("emitters")!.Add(new XElement(own.Root.Descendants("emitter").Single()));
        document.Root.Element("vfx")!.Add(new XElement(own.Root.Element("vfx")!.Element("behaviour_chains")!));
        byte[]? Resolve(string path) => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? childBytes : original;
        var bytes = document.Write();
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(bytes, "vfx/identification-events-DIAG.xml", Resolve)); preview.Seek(.2f, Matrix.Identity);
        var full = Render(device, preview, true);
        preview.Identify(new("Emitter", "event_test"));
        var selected = Render(device, preview, true);
        using var control = new EffekseerVfxPreview(device);
        control.Reload(new(bytes, "vfx/identification-events-DIAG.xml", Resolve, RootSelection: new(["event_test"], [])));
        control.Seek(.2f, Matrix.Identity);
        AssertPixels(Render(device, control, true), selected);
        Assert.That(selected.SequenceEqual(full), Is.False);
        preview.Identify(null);
        AssertPixels(full, Render(device, preview, true));
    });

    private static VfxPreviewSnapshot FakeSnapshot(byte value = 1) => new([value], "vfx/identification-fake-DIAG.xml", _ => null);
    private static VfxPreviewComponent Create(Func<RecordingPreview> create)
    {
        var factory = new Mock<IVfxPreviewFactory>();
        factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(() => create());
        return new(factory.Object, Mock.Of<IDeviceResolver>(), null!);
    }
    private static void Update(VfxPreviewComponent component) => component.Update(new(TimeSpan.FromSeconds(20), TimeSpan.Zero));

    private sealed class RecordingPreview : IVfxPreview
    {
        public int Reloads, Seeks, Preparations;
        public bool Disposed;
        public List<VfxPreviewLayerIdentity?> Identifications { get; } = [];
        public VfxPreviewLayerIdentity? LastIdentification { get; private set; }
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 3;
        public int TotalEmitterCount => 3;
        public int ParticleCount => 3;
        public float CycleDuration => 8;
        public void Reload(VfxPreviewSnapshot snapshot) { Reloads++; LastIdentification = null; }
        public void Identify(VfxPreviewLayerIdentity? identity)
        {
            Assert.That(Disposed, Is.False);
            LastIdentification = identity; Identifications.Add(identity);
        }
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? history) => Preparations++;
        public void Advance(float seconds, Matrix attachment) => Seek(seconds, attachment);
        public void Seek(float seconds, Matrix attachment) => Seeks++;
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() => Disposed = true;
    }

    // DIAG: fixed particle parameters separate draw filtering from root-index RNG differences.
    // The smoke diffuse is the unmodified embedded official original, checked before and after each GPU case.
    private static VfxDocument Leaf(string pass)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Single(element => (string?)element.Attribute("name") == "ParticleScale").Value = VectorCurve(.35f, .35f, 1);
        if (pass.Length != 0)
            document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
                new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", pass)))));
        return document;
    }

    private static (VfxDocument Document, byte[] Child) Composition(string pass)
    {
        var child = Leaf(pass); var childBytes = child.Write();
        var document = Leaf(pass);
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "EmitterPosition"), VectorCurve(-.65f, 0, 0)));
        var first = document.AddReference("identification-child-DIAG", child); first.Apply();
        first.Selection.SetAttributeValue("inst_name", "visible");
        var second = document.AddReference("identification-child-DIAG", child); second.Apply();
        second.Selection.SetAttributeValue("inst_name", "visible"); second.Selection.SetAttributeValue("pos_x", .65f);
        return (document, childBytes);
    }

    private static byte[] SingleRoot(VfxDocument complete, VfxPreviewLayerIdentity identity)
    {
        var document = VfxDocument.Read(complete.Write());
        if (identity.Kind == "Emitter") document.Root.Elements("vfx_references").Remove();
        else
        {
            document.Root.Element("vfx")!.Element("emitters")!.Elements().Remove();
            var references = document.Root.Elements("vfx_references").Elements("vfx").ToArray();
            foreach (var reference in references.Where((_, index) => index != identity.Occurrence)) reference.Remove();
        }
        return document.Write();
    }

    private static string VectorCurve(float x, float y, float z) => VfxParticleEventTests.Curve(["x", "y", "z"], x, y, z);
    private static VfxPreviewSnapshot Snapshot(byte[] document, byte[] child, byte[] original) => new(document, "vfx/identification-DIAG.xml",
        path => path.EndsWith("identification-child-DIAG.xml", StringComparison.OrdinalIgnoreCase) ? child : original);
    private static object Field(EffekseerVfxPreview preview, string name) => typeof(EffekseerVfxPreview)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;

    private static Vector4[] Render(GraphicsDevice device, EffekseerVfxPreview preview, bool sharedScene, bool requireVisible = true)
    {
        Assert.That(preview.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black);
        try
        {
            using var scene = sharedScene ? preview.BeginScene(Camera) : null;
            preview.Draw(Camera, scene);
        }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[64 * 64]; target.GetData(pixels);
        if (requireVisible) Assert.That(pixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .01f), Is.GreaterThan(10));
        return pixels;
    }
    private static void AssertPixels(Vector4[] expected, Vector4[] actual) => Assert.That(expected.Zip(actual, Vector4.Distance).Max(), Is.LessThan(.000001f));
    private static void WithGraphics(Action<GraphicsDevice, byte[]> test)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen identification checks are opt-in.");
        var assembly = typeof(VfxIdentificationTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("OfficialSiegeSmokeDiffuse.dds", StringComparison.Ordinal)))!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var original = bytes.ToArray();
        const string expected = "18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70";
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo(expected));
        var graphics = new GraphicsDeviceServiceMock();
        try { test(graphics.GraphicsDevice, original); }
        finally { graphics.Release(); }
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo(expected));
    }
}
