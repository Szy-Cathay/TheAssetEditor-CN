using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxResourceReloadPixelTests
{
    private static readonly Vector3 CameraPosition = new(0, 0, 10);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(CameraPosition, Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(2, 2, .1f, 100), CameraPosition, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void ParameterEditsReuseTheExclusiveHandleAndMatchACompleteUploadAtTheSamePhase() => WithGraphics((device, original) =>
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(Snapshot(1, _ => original));
        preview.Seek(.2f, Matrix.Identity);
        var baseline = Render(device, preview);
        var owner = Handle(preview);
        preview.Reload(Snapshot(.4f, _ => original));
        Assert.That(Handle(preview), Is.SameAs(owner));
        Assert.That(owner.IsClosed, Is.False);
        preview.Seek(.2f, Matrix.Identity);
        var edited = Render(device, preview);
        Assert.That(edited.SequenceEqual(baseline), Is.False, "The real parameter edit must change the rendered footprint.");
        using (var control = new EffekseerVfxPreview(device))
        {
            control.Reload(Snapshot(.4f, _ => original));
            control.Seek(.2f, Matrix.Identity);
            AssertPixels(edited, Render(device, control));
        }
        preview.Reload(Snapshot(1, _ => original));
        Assert.That(Handle(preview), Is.SameAs(owner));
        preview.Seek(.2f, Matrix.Identity);
        AssertPixels(baseline, Render(device, preview));
    });

    [Test]
    public void ASharedPlaybackKeepsItsOldLayersAndSurvivesTheEditorsReloadAndDisposal() => WithGraphics((device, original) =>
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(Snapshot(1, _ => original));
        using var tail = (EffekseerVfxPreview)preview.CreatePlaybackInstance()!;
        var oldHandle = Handle(tail);
        tail.Seek(.2f, Matrix.Identity);
        var baseline = Render(device, tail);
        preview.Reload(Snapshot(.4f, _ => original));
        Assert.That(Handle(preview), Is.Not.SameAs(oldHandle), "A live Effect tail must prevent reconfiguration of its shared native layers.");
        preview.Dispose();
        Assert.That(oldHandle.IsClosed, Is.False);
        AssertPixels(baseline, Render(device, tail));
        tail.Dispose();
        Assert.That(oldHandle.IsClosed, Is.True);
    });

    [Test]
    public void AQueuedSceneSubmissionPreventsExclusiveTransferUntilEndScene() => WithGraphics((device, original) =>
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(Snapshot(1, _ => original));
        preview.Seek(.2f, Matrix.Identity);
        var oldHandle = Handle(preview);
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(Color.Black);
        try
        {
            using (var scene = preview.BeginScene(Camera))
            {
                Assert.That(scene, Is.Not.Null);
                preview.Draw(Camera, scene);
                preview.Reload(Snapshot(.4f, _ => original));
                Assert.That(Handle(preview), Is.Not.SameAs(oldHandle));
                Assert.That(oldHandle.IsClosed, Is.False, "The queued draw must retain its resource owner through native EndScene.");
            }
            Assert.That(oldHandle.IsClosed, Is.True);
        }
        finally { device.SetRenderTarget(null); }
        preview.Seek(.2f, Matrix.Identity);
        Render(device, preview);
    });

    [Test]
    public void InPlaceResourceChangesForceAnUploadEvenWhenTheirVisiblePixelsAreUnchanged() => WithGraphics((device, original) =>
    {
        using var preview = new EffekseerVfxPreview(device);
        var resolved = original.ToArray();
        preview.Reload(Snapshot(1, _ => resolved));
        preview.Seek(.2f, Matrix.Identity);
        var baseline = Render(device, preview);
        var oldHandle = Handle(preview);
        // DIAG: a reserved DDS header byte; original embedded official bytes remain untouched.
        resolved[32] ^= 1;
        preview.Reload(Snapshot(1, _ => resolved));
        Assert.That(Handle(preview), Is.Not.SameAs(oldHandle));
        Assert.That(oldHandle.IsClosed, Is.True);
        preview.Seek(.2f, Matrix.Identity);
        AssertPixels(baseline, Render(device, preview));
    });

    [Test]
    public void AFailedResourceNeverMakesAPartialUploadReusable() => WithGraphics((device, original) =>
    {
        var document = VfxDocument.Read(Snapshot(1, _ => original).GameBytes);
        var effect = document.Root.Element("vfx")!;
        var emitter = new XElement(effect.Element("emitters")!.Element("emitter")!);
        emitter.SetAttributeValue("id", "failed-resource-DIAG");
        effect.Element("emitters")!.Add(emitter);
        var chain = new XElement(effect.Element("behaviour_chains")!.Element("behaviour_chain")!);
        chain.SetAttributeValue("id", "failed-resource-DIAG");
        chain.Descendants("texture_property").Single().SetAttributeValue("path", "DIAG-broken.dds");
        effect.Element("behaviour_chains")!.Add(chain);
        var snapshot = new VfxPreviewSnapshot(document.Write(), "vfx/resource-reload-DIAG.xml", path => path == "DIAG-broken.dds" ? [1, 2, 3] : original);
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(snapshot);
        var oldHandle = Handle(preview);
        Assert.That(preview.EmitterCount, Is.EqualTo(1));
        Assert.That(preview.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.LayerTextureUnavailable"), Is.True);
        preview.Reload(snapshot);
        Assert.That(Handle(preview), Is.Not.SameAs(oldHandle));
        Assert.That(oldHandle.IsClosed, Is.True);
        preview.Seek(.2f, Matrix.Identity);
        Render(device, preview);
    });

    private static VfxPreviewSnapshot Snapshot(float scale, Func<string, byte[]?> resolve)
    {
        // DIAG: isolate upload ownership using an unmodified, hash-checked official diffuse texture.
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("property").Single(property => (string?)property.Attribute("name") == "ParticleScale").Value =
            string.Join('|', new[] { "x", "y", "z" }.Select(channel => VfxLibraryVisibilityTests.Constant(channel, scale).Split(':')[0]))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
        return new(document.Write(), "vfx/resource-reload-DIAG.xml", resolve);
    }

    private static EffekseerHandle Handle(EffekseerVfxPreview preview) => (EffekseerHandle)typeof(EffekseerVfxPreview)
        .GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;

    private static Vector4[] Render(GraphicsDevice device, EffekseerVfxPreview preview)
    {
        Assert.That(preview.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(Color.Black);
        try { preview.Draw(Camera); }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[64 * 64];
        target.GetData(pixels);
        Assert.That(pixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .01f), Is.GreaterThan(10));
        return pixels;
    }

    private static void AssertPixels(Vector4[] expected, Vector4[] actual) => Assert.That(
        expected.Zip(actual, Vector4.Distance).Max(), Is.LessThan(.000001f), "Reconfiguration must preserve the complete-upload pixel result.");

    private static void WithGraphics(Action<GraphicsDevice, byte[]> test)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var assembly = typeof(VfxResourceReloadPixelTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("OfficialSiegeSmokeDiffuse.dds", StringComparison.Ordinal)))!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var original = bytes.ToArray();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo("18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70"));
        var graphics = new GraphicsDeviceServiceMock();
        try { test(graphics.GraphicsDevice, original); }
        finally { graphics.Release(); }
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo("18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70"));
    }
}
