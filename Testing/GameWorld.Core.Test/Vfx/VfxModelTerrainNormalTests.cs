#nullable enable
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxModelTerrainNormalTests
{
    private static CommonShaderParameters Camera(float? ground) => new(Matrix.Identity, Matrix.Identity, new(0, 0, 20),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: ground);

    [TestCase("WORLD_SPACE", .5f)]
    [TestCase("WORLD_SPACE", 2f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", .5f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", 2f)]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION", .5f)]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION", 2f)]
    public void WorldUpReceiver_NormalAlignmentKeepsTheAbsoluteBirthQuaternionAndActualMotion(string space, float scale)
    {
        var original = Source(space, true); var conversion = Convert(original); var control = Convert(Source(space, false));
        Assert.That(conversion.Emitters.Single().ModelTerrainNormal, Is.True);
        Assert.That(conversion.Emitters.Single().RequiresFlatTerrain, Is.False);
        var root = Matrix.CreateScale(scale) * Matrix.CreateRotationX(.2f) * Matrix.CreateRotationY(.4f) * Matrix.CreateTranslation(2, 4, -1);
        var simulation = Prepare(conversion, root); var baseline = Prepare(control, root);
        Vector3? first = null;
        foreach (var time in new[] { 0f, .2f, .4f })
        {
            simulation.Seek(time, root); baseline.Seek(time, root);
            var actual = simulation.Frame(Camera(0)).Single(); var expected = baseline.Frame(Camera(0)).Single();
            Assert.That(actual.Transform, Is.EqualTo(expected.Transform), "Native Up-to-Up is identity; it does not add emitter or node rotation.");
            Assert.That(simulation.TerrainBasisUnsupported, Is.False);
            first ??= actual.Transform.Translation;
            if (time > 0) Assert.That(Vector3.Distance(actual.Transform.Translation, first.Value), Is.GreaterThan(.01));
        }
        Assert.That(VfxDocument.Read(original).Write(), Is.EqualTo(original));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void InvalidReceiverRoot_IsDiagnosedWithoutInventingANormal(int kind)
    {
        var shear = Matrix.Identity; shear.M12 = .2f;
        var root = kind == 0 ? Matrix.CreateScale(-1, 1, 1) : kind == 1 ? Matrix.CreateScale(1, 2, 1) : shear;
        var simulation = Prepare(Convert(Source("WORLD_SPACE", true)), root); simulation.Seek(.2f, root);
        Assert.That(simulation.Frame(Camera(0)), Is.Empty); Assert.That(simulation.TerrainBasisUnsupported, Is.True);
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY")]
    public void ZeroSpinNormalBirth_DoesNotRotateWorldUpWithTheNode(string space)
    {
        var document = VfxDocument.Read(Source(space, true)); var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Where(e => (string?)e.Attribute("name") is "ParticleSpin_Min" or "ParticleSpin_Max").Remove();
        var conversion = Convert(document.Write()); Assert.That(conversion.Emitters.Single().ModelQuaternionSpin, Is.True);
        var root = Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(.7f);
        var simulation = Prepare(conversion, root); simulation.Seek(0, root);
        var frame = simulation.Frame(Camera(0)).Single();
        Assert.That(Vector3.Distance(Vector3.Normalize(frame.Transform.Right), Vector3.Right), Is.LessThan(.00002f));
        Assert.That(Vector3.Distance(Vector3.Normalize(frame.Transform.Up), Vector3.Up), Is.LessThan(.00002f));
    }

    [Test]
    public void ReceiverMustBeExplicit_AndGroundPreparationRecoversTheActualModel()
    {
        var conversion = Convert(Source("WORLD_SPACE", true)); var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera(null)), Is.Empty); Assert.That(simulation.TerrainBasisUnsupported, Is.True);
        Assert.That(simulation.Frame(Camera(0)), Has.Length.EqualTo(1)); Assert.That(simulation.TerrainBasisUnsupported, Is.False);
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION")]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void NormalAlignmentGpu_EqualsTheIndependentAbsoluteQuaternionControl(string space)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var eye = new Vector3(0, 0, 12);
            var camera = Camera(0) with { View = Matrix.CreateLookAt(eye, new(0, 1, 0), Vector3.Up),
                Projection = Matrix.CreateOrthographic(12, 12, .1f, 100), CameraPosition = eye };
            Vector4[] Render(bool normal)
            {
                using var preview = new EffekseerVfxPreview(device);
                preview.Reload(new(Source(space, normal), "DIAG/rigid-terrain-normal.xml", path => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                    ? VfxModelVariantPixelTests.Dds(new Color(70, 180, 230, 155)) : VfxModelVariantTests.Resolve(path)));
                preview.Seek(.4f, Matrix.Identity);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
            }
            var actual = Render(true); var control = Render(false);
            Assert.That(control.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(12));
            Assert.That(actual.Zip(control).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThanOrEqualTo(.00001f));
        }
        finally { graphics.Release(); }
    }

    private static byte[] Source(string space, bool normal)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1)); var emitter = document.Root.Descendants("emitter").Single();
        void Set(string name, string value, bool attribute = false) => VfxParticleEventMotionTests.Set(emitter, name, value, attribute);
        Set("MaximumParticles", "1", true); Set("ParticleSimulationSpace", space, true);
        Set("ParticleRelativeSpawnOrientation", normal ? "RELATIVE_TO_TERRAIN_NORMAL" : "DISABLED", true);
        Set("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 3));
        Set("ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 2));
        Set("EmitterPosition", VfxParticleEventMotionTests.VectorCurve(Vector3.Zero));
        Set("EmitterOrientation", VfxParticleEventMotionTests.VectorCurve(new(17, -32, 11)));
        Set("ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", .3f));
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
        {
            emitter.Elements().Where(e => (string?)e.Attribute("name") == name).Remove();
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", 10), new XAttribute("y", 20), new XAttribute("z", 30)));
        }
        return document.Write();
    }
    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "DIAG/rigid-terrain-normal.xml", VfxModelVariantTests.Resolve));
    private static TotalWarVfxSimulation Prepare(VfxConversion conversion, Matrix root)
    {
        var simulation = new TotalWarVfxSimulation(conversion); simulation.PrepareInitialGround(0, root, null, false, false, null); return simulation;
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxModelTerrainNormalOfficialTests
{
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private static readonly MethodInfo ReadOfficial = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [OneTimeSetUp]
    public void RequireManifestOwnedOriginals() => _official.LoadCurrentOfficialResourcesOnly();

    private static IEnumerable<TestCaseData> Originals()
    {
        foreach (var layer in new[] { "rat_splash", "rat_run_up_attack", "rat_run_up_spread", "rat_run_spread_attack" })
            foreach (var quality in new[] { "0", "1", "2" }) yield return new(layer, quality);
    }

    [TestCaseSource(nameof(Originals))]
    public void CompleteOriginalRigidTerrainNormal_ConvertsAndDrawsAtEachQuality(string layer, string quality)
    {
        const string path = @"vfx\_rat_test_lib.xml";
        byte[]? Read(string key) => (byte[]?)ReadOfficial.Invoke(_official, [key]);
        var original = Read(path) ?? throw new FileNotFoundException(path);
        var request = new VfxPreviewSnapshot(original, path, Read, quality, new([layer], []));
        var conversion = TotalWarVfxConverter.Convert(request);
        var definition = conversion.Emitters.Single(e => !e.IsEventTemplate);
        Assert.That(definition.Name, Is.EqualTo(layer)); Assert.That(definition.ModelTerrainNormal, Is.True);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleRelativeSpawnOrientation" && d.ReasonKey == "Vfx.Preview.OrientationUnsupported"), Is.False);
        var time = Math.Max(0, definition.Start.Max) + .15f;
        var simulation = new TotalWarVfxSimulation(conversion); simulation.PrepareInitialGround(0, Matrix.Identity, null, false, false, null);
        simulation.Seek(time, Matrix.Identity);
        Assert.That(simulation.ProjectedFrame(new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 20), Vector3.Zero,
            0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0), false).Count, Is.GreaterThan(0));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device); preview.Reload(request);
            var bounds = preview.PreviewBounds; var focus = bounds.HasValue ? (bounds.Value.Min + bounds.Value.Max) * .5f : Vector3.Zero;
            var width = bounds.HasValue ? Math.Max(4, (bounds.Value.Max - bounds.Value.Min).Length() * 1.1f) : 12;
            var eye = focus + Vector3.Normalize(new Vector3(16, 16, 28)) * 20;
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, focus, Vector3.Up), Matrix.CreateOrthographic(width, width * .75f, .1f, 300),
                eye, focus, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0, VfxWriteGroundDepth: true);
            preview.Seek(time, Matrix.Identity);
            using var target = new RenderTarget2D(device, 192, 144, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkSlateGray, 1, 0);
            preview.Draw(camera); device.SetRenderTarget(null);
            var pixels = new Color[192 * 144]; target.GetData(pixels);
            Assert.That(pixels.Count(c => Math.Abs(c.R - Color.DarkSlateGray.R) + Math.Abs(c.G - Color.DarkSlateGray.G) + Math.Abs(c.B - Color.DarkSlateGray.B) > 6), Is.GreaterThan(12));
            Assert.That(Read(path), Is.EqualTo(original));
            TestContext.Out.WriteLine($"Original {path}/{layer}/Q{quality}: authored source unchanged; explicit flat receiver; retained diagnostics: {string.Join(';', preview.Diagnostics.Select(d => d.Field + ':' + d.ReasonKey).Distinct())}");
        }
        finally { graphics.Release(); }
    }
}
