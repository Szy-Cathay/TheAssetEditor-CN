#nullable enable
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxOfficialDistortionPixelTests
{
    private const string SourcePath = @"vfx\wh3_dlc28_lib_neferata.xml";
    private const string Layer = "fire_beam_impact3";
    private const int Size = 256;
    private readonly Dictionary<string, (PackFile File, string Pack)> _official = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _origins = new(StringComparer.OrdinalIgnoreCase);
    private byte[] _source = [];
    private byte[] _control = [];
    private string _manifestHash = "";
    private static readonly Vector3 Eye = new(0, 0, 10);
    private static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, .01f, 100);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(Eye, Vector3.Zero, Vector3.Up),
        Projection, Eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [OneTimeSetUp]
    public void LoadCurrentOfficialResourcesOnly()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !Directory.Exists(data))
            Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA must point to the original game's data directory.");
        var manifestPath = Path.Combine(data!, "manifest.txt");
        if (!File.Exists(manifestPath)) Assert.Ignore("The official manifest is required; arbitrary Pack scanning is prohibited.");
        var layerBytes = Marshal.SizeOf<EffekseerLayer>();
        if (int.TryParse(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_LAYER_BYTES"), out var expectedBytes))
            Assert.That(layerBytes, Is.EqualTo(expectedBytes), "Do not mix the preserved old layer ABI with a new native runtime.");
        Assert.That(EffekseerNative.AecnVfxAbi(layerBytes, Marshal.SizeOf<EffekseerParticle>(), Marshal.SizeOf<EffekseerVertex>(), Marshal.SizeOf<EffekseerLighting>()), Is.EqualTo(1));
        TestContext.Progress.WriteLine("Managed/native checked layer ABI bytes=" + layerBytes);
        _manifestHash = Hash(File.ReadAllBytes(manifestPath));
        var names = File.ReadAllLines(manifestPath).Select(line => line.Split('\t')[0].Trim())
            .Where(name => name.EndsWith(".pack", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.That(names, Is.Not.Empty);
        Assert.That(names.All(name => Path.GetFileName(name) == name && !name.Contains(':')), Is.True);
        var loader = new PackFileContainerLoader(null!);
        var packs = names.Select(name =>
        {
            var path = Path.Combine(data!, name);
            Assert.That(File.Exists(path), Is.True, "Missing official manifest Pack: " + path);
            return loader.Load(path)!;
        }).OrderBy(pack => pack.Header.LoadOrder).ThenBy(pack => pack.Name).ToArray();
        foreach (var pack in packs)
            foreach (var file in pack.FileList) _official[Key(file.Key)] = (file.Value, pack.SystemFilePath);
        _source = Read(SourcePath) ?? throw new FileNotFoundException(SourcePath);
        _control = DiagnosticControl(_source);
        TestContext.Progress.WriteLine("Official distortion diagnostic: " + SourcePath + " / " + Layer
            + "; manifest SHA256=" + _manifestHash + "; original VFX SHA256=" + Hash(_source));
    }

    [Test]
    public void ZeroVertexAlpha_PreservesTheUnwarpedBackground()
    {
        var zero = Edit(_control, "vs_vertex_alpha", "curve_alpha", Constant("alpha", 0));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var hidden = Render(graphics.GraphicsDevice, zero, "vertex-alpha-zero");
            var visible = Render(graphics.GraphicsDevice, _control, "original-mask");
            Assert.That(MaximumDifference(background, hidden), Is.LessThanOrEqualTo(1));
            Assert.That(ChangedPixels(background, visible), Is.GreaterThan(20), "The original diffuse mask must produce measurable refraction in this diagnostic geometry.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void GradientAlphaZeroAndOne_ControlRefractionWithoutReplacingTheOfficialTexture()
    {
        var mapped = Edit(_control, "ps_gradient", "alpha_mapping", "true", scalar: true);
        var zero = Edit(mapped, "ps_gradient", "curve_alpha", Constant("alpha", 0));
        var one = Edit(mapped, "ps_gradient", "curve_alpha", Constant("alpha", 1));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var hidden = Render(graphics.GraphicsDevice, zero, "gradient-alpha-zero");
            var visible = Render(graphics.GraphicsDevice, one, "gradient-alpha-one");
            Assert.That(MaximumDifference(background, hidden), Is.LessThanOrEqualTo(1));
            Assert.That(ChangedPixels(background, visible), Is.GreaterThan(20));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void GradientRgb_DoesNotTintOrChangeTheRefractedBackground()
    {
        var red = Edit(_control, "ps_gradient", "curve_colour", Rgb(4, 0, 0));
        var blue = Edit(_control, "ps_gradient", "curve_colour", Rgb(0, 0, 4));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var redPixels = Render(graphics.GraphicsDevice, red, "gradient-red");
            var bluePixels = Render(graphics.GraphicsDevice, blue, "gradient-blue");
            Assert.That(MaximumDifference(redPixels, bluePixels), Is.LessThanOrEqualTo(1), "DISTORTION uses gradient alpha, not gradient RGB as emissive colour.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OriginalAtlas_ChangesRefractionAsItsMaskFramesAdvance()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var earlier = Render(graphics.GraphicsDevice, _control, "atlas-earlier", time: 3);
            var later = Render(graphics.GraphicsDevice, _control, "atlas-later", time: 3.125f);
            Assert.That(ChangedPixels(earlier, later), Is.GreaterThan(20), "Only the original atlas phase changes; geometry, opacity and gradient remain fixed.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void AlphaCrush_ContractsTheOriginalDiffuseMaskBeforeRefraction()
    {
        var noCrush = AddCrush(_control, 0);
        var crushed = Edit(noCrush, "ps_alpha_crush", "curve_alpha_crush", Constant("progress", .8f));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var full = Render(graphics.GraphicsDevice, noCrush, "crush-zero");
            var reduced = Render(graphics.GraphicsDevice, crushed, "crush-point-eight");
            Assert.That(ChangedPixels(full, reduced), Is.GreaterThan(20));
            Assert.That(TotalDifference(background, reduced), Is.LessThan(TotalDifference(background, full)));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void SoftDepth_ReducesRefractionAtTheSameReceivingSurface()
    {
        var soft = Edit(_control, "ps_soft_edge", "soft_factor", "10", scalar: true);
        var surface = Vector4.Transform(new Vector4(0, 0, -10.1f, 1), Projection);
        var depth = surface.Z / surface.W;
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background", depth: depth);
            var hard = Render(graphics.GraphicsDevice, _control, "soft-off", depth: depth);
            var faded = Render(graphics.GraphicsDevice, soft, "soft-ten", depth: depth);
            Assert.That(ChangedPixels(background, hard), Is.GreaterThan(20));
            Assert.That(TotalDifference(background, faded), Is.LessThan(TotalDifference(background, hard) * .8));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void WeightedOppositeDirections_CancelWithoutErasingTheBackground()
    {
        var first = SpatialDiagnostic(Vector3.Zero, new(0, 45, 0), 4, uniformMask: true);
        var opposite = SpatialDiagnostic(Vector3.Zero, new(0, 225, 0), 4, uniformMask: true);
        var combined = CombineDiagnostics(("first", first), ("opposite", opposite));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var positive = Render(graphics.GraphicsDevice, first, "positive-alone");
            var negative = Render(graphics.GraphicsDevice, opposite, "negative-alone");
            var cancelled = Render(graphics.GraphicsDevice, combined, "opposite-combined", emitters: 2, particles: 2);
            Assert.That(ChangedPixels(background, positive), Is.GreaterThan(20));
            Assert.That(ChangedPixels(background, negative), Is.GreaterThan(20));
            Assert.That(MaximumDifference(background, cancelled), Is.LessThanOrEqualTo(1),
                "Equal opacity and opposite geometric directions on the same plane must cancel, rather than the last particle replacing the first.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void WeightedOverlappingLayers_AreIndependentOfTheirSubmissionOrder()
    {
        var first = SpatialDiagnostic(new(-.35f, 0, 0), new(20, 45, 0), 4);
        var second = SpatialDiagnostic(new(.35f, 0, 0), new(-15, -30, 0), 4);
        var forward = CombineDiagnostics(("first", first), ("second", second));
        var reversed = CombineDiagnostics(("second", second), ("first", first));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var forwardPixels = Render(graphics.GraphicsDevice, forward, "forward", emitters: 2, particles: 2);
            var reversedPixels = Render(graphics.GraphicsDevice, reversed, "reversed", emitters: 2, particles: 2);
            Assert.That(ChangedPixels(background, forwardPixels), Is.GreaterThan(20));
            Assert.That(MaximumDifference(forwardPixels, reversedPixels), Is.LessThanOrEqualTo(1),
                "Continuous distortion layers accumulate together; neither emitter order nor texture batch boundaries choose the winning direction.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void WeightedGrouping_SameLayerParticlesMatchConsecutiveLayers()
    {
        var particle = SpatialDiagnostic(Vector3.Zero, new(20, 45, 0), 4);
        var split = CombineDiagnostics(("first", particle), ("second", particle));
        var document = VfxDocument.Read(particle);
        SetEmitter(document, "MaximumParticles", "2", scalar: true);
        SetEmitter(document, "EmissionRateCurve", Constant("x", 1));
        SetEmitter(document, "ParticleLifeTime", Constant("x", 5));
        var sameLayer = document.Write();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var grouped = Render(graphics.GraphicsDevice, sameLayer, "same-layer", particles: 2);
            var separate = Render(graphics.GraphicsDevice, split, "split-layers", emitters: 2, particles: 2);
            Assert.That(ChangedPixels(background, grouped), Is.GreaterThan(20));
            Assert.That(MaximumDifference(grouped, separate), Is.LessThanOrEqualTo(1),
                "The same two fixed particles must not change appearance when divided between consecutive material layers.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void WeightedDisjointRegions_KeepTheirOwnRefractionAndUntouchedPixels()
    {
        var leftSource = SpatialDiagnostic(new(-1.8f, 0, 0), new(0, 45, 0), 2.2f);
        var rightSource = SpatialDiagnostic(new(1.8f, 0, 0), new(0, -30, 0), 2.2f);
        var combined = CombineDiagnostics(("left", leftSource), ("right", rightSource));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var left = Render(graphics.GraphicsDevice, leftSource, "left-alone");
            var right = Render(graphics.GraphicsDevice, rightSource, "right-alone");
            var both = Render(graphics.GraphicsDevice, combined, "disjoint-combined", emitters: 2, particles: 2);
            var leftRegion = Enumerable.Range(0, background.Length).Where(index => PixelDifference(left[index], background[index]) > 2).ToArray();
            var rightRegion = Enumerable.Range(0, background.Length).Where(index => PixelDifference(right[index], background[index]) > 2).ToArray();
            Assert.That(leftRegion, Has.Length.GreaterThan(20));
            Assert.That(rightRegion, Has.Length.GreaterThan(20));
            Assert.That(leftRegion.Intersect(rightRegion).ToArray(), Is.Empty, "The diagnostic footprints must remain disjoint.");
            Assert.That(leftRegion.Max(index => PixelDifference(left[index], both[index])), Is.LessThanOrEqualTo(1));
            Assert.That(rightRegion.Max(index => PixelDifference(right[index], both[index])), Is.LessThanOrEqualTo(1));
            var untouched = Enumerable.Range(0, background.Length).Where(index => PixelDifference(left[index], background[index]) <= 1
                && PixelDifference(right[index], background[index]) <= 1).ToArray();
            Assert.That(untouched, Has.Length.GreaterThan(background.Length / 2));
            Assert.That(untouched.Max(index => PixelDifference(background[index], both[index])), Is.LessThanOrEqualTo(1),
                "A full-screen resolve must not introduce rectangular seams outside either official mask.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WeightedZeroMaskRectangle_PreservesEarlierRefractionAndColour(bool precedingColour)
    {
        var visible = SpatialDiagnostic(Vector3.Zero, new(20, 45, 0), 3);
        var zero = SpatialDiagnostic(Vector3.Zero, new(0, -30, 0), 7, uniformMask: true);
        zero = Edit(zero, "ps_gradient", "curve_alpha", Constant("alpha", 0));
        var document = VfxDocument.Read(SpatialDiagnostic(Vector3.Zero, new(0, 0, 0), 6));
        document.Root.Descendants("effect_render_stack").Single().Elements().Single(property => (string?)property.Attribute("name") == "scene_view_type")
            .SetAttributeValue("value", "FULLRES_DEFERRED_ALPHA");
        var colour = Edit(document.Write(), "ps_gradient", "curve_colour", Rgb(.1f, .6f, .2f));
        colour = Edit(colour, "ps_emissive", "emissive_mul", "0", scalar: true);
        var existing = precedingColour ? CombineDiagnostics(("colour", colour), ("visible", visible)) : visible;
        var withZero = precedingColour ? CombineDiagnostics(("colour", colour), ("visible", visible), ("zero", zero))
            : CombineDiagnostics(("visible", visible), ("zero", zero));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background");
            var earlier = Render(graphics.GraphicsDevice, existing, "earlier", emitters: precedingColour ? 2 : 1, particles: precedingColour ? 2 : 1);
            var extended = Render(graphics.GraphicsDevice, withZero, "with-zero-rectangle", emitters: precedingColour ? 3 : 2, particles: precedingColour ? 3 : 2);
            Assert.That(ChangedPixels(background, earlier), Is.GreaterThan(20));
            Assert.That(MaximumDifference(earlier, extended), Is.LessThanOrEqualTo(1),
                "A zero-alpha mask contributes neither displacement nor rectangle coverage, including after an ordinary colour layer.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void WeightedResolve_PreservesTheExistingSceneAlphaAcrossMaskEdges()
    {
        var source = SpatialDiagnostic(Vector3.Zero, new(20, 45, 0), 4);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "background-alpha-96", backgroundAlpha: 96);
            var refracted = Render(graphics.GraphicsDevice, source, "refracted-alpha-96", backgroundAlpha: 96);
            Assert.That(background.Select(pixel => pixel.A).ToArray(), Has.All.EqualTo((byte)96));
            Assert.That(ChangedPixels(background, refracted), Is.GreaterThan(20));
            Assert.That(refracted.Select(pixel => pixel.A).ToArray(), Is.EqualTo(background.Select(pixel => pixel.A).ToArray()),
                "Refraction changes scene RGB while preserving its existing alpha, including transparent mask texels and rectangle edges.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(2f, 1, .08f)]
    [TestCase(2f, 2, 0f)]
    [TestCase(3f, 2, .18f)]
    public void NativeRawOpacity_UsesTheCapturedSignedTransmittance(float alpha, int particles, float nativeUvOffset)
    {
        // These equivalent outputs follow the captured native PS/resolve, including T=-1, T=1 and T=4.
        var source = SpatialDiagnostic(Vector3.Zero, new(0, 90, 0), 5);
        source = Edit(source, "ps_gradient", "alpha_mapping", "false", scalar: true);
        var document = VfxDocument.Read(source);
        document.Root.Descendants("texture_property").Single(property => (string?)property.Attribute("name") == "diffuse").SetAttributeValue("path", "");
        SetEmitter(document, "ParticleInitialAlpha", Constant("x", alpha));
        SetEmitter(document, "MaximumParticles", particles.ToString(CultureInfo.InvariantCulture), scalar: true);
        SetEmitter(document, "EmissionRateCurve", Constant("x", 1));
        SetEmitter(document, "ParticleLifeTime", Constant("x", 20));
        source = document.Write();
        var eye = new Vector3(10, 0, 0);
        var camera = Camera with { View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up), CameraPosition = eye };
        var conversion = TotalWarVfxConverter.Convert(new(source, SourcePath, Read));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters.Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.TextureIndex, Is.EqualTo(-1));
            Assert.That(render.DistortionMaskIndex, Is.EqualTo(-1));
            Assert.That(render.GradientIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(render.GradientAlphaMapping, Is.Zero);
        });
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(4, Matrix.Identity);
        var frame = simulation.Frame(camera);
        Assert.That(frame, Has.Length.EqualTo(particles));
        Assert.That(frame.Select(particle => particle.Colour.W).ToArray(), Has.All.EqualTo(alpha));
        TestContext.Progress.WriteLine($"Native raw-opacity diagnostic: A={alpha}, particles={particles}, CPU Colour.W={string.Join(',', frame.Select(particle => particle.Colour.W))}; Bridge alphaScale={alpha}, packed alpha=255; native equivalent UV offset={nativeUvOffset}.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Render(graphics.GraphicsDevice, null, "raw-opacity-background", cameraOverride: camera);
            var actual = Render(graphics.GraphicsDevice, source, "raw-opacity", time: 4, particles: particles, cameraOverride: camera);
            if (nativeUvOffset == 0)
            {
                Assert.That(MaximumDifference(background, actual), Is.LessThanOrEqualTo(1),
                    "Two coincident native A=2 particles have T=1 and preserve the original scene exactly.");
                return;
            }
            var compared = 0;
            var maximumDifference = 0;
            for (var y = 96; y < 160; ++y)
                for (var x = 74; x < 180; ++x)
                {
                    var sample = x + nativeUvOffset * Size;
                    var low = (int)MathF.Floor(sample);
                    var high = (int)MathF.Ceiling(sample);
                    var expected = background[y * Size + low];
                    if (PixelDifference(expected, background[y * Size + high]) != 0 || PixelDifference(expected, background[y * Size + x]) < 20) continue;
                    ++compared;
                    maximumDifference = Math.Max(maximumDifference, PixelDifference(expected, actual[y * Size + x]));
                }
            Assert.That(compared, Is.GreaterThan(500), "The native-equivalent displaced samples must differ from the unwarped scene inside the particle.");
            Assert.That(maximumDifference, Is.LessThanOrEqualTo(1),
                "Native signed T is not saturated: A=2 gives T=-1 and UV offset .08; two A=3 particles give T=4 and UV offset .18 after the signed visibility is squared.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeNormalBasis_MirroredCameraFacingPreservesTheSamePhysicalQuad()
    {
        var basis = Matrix.Identity;
        basis.Right = new(-.57943f, .80658f, -.11696f);
        basis.Up = new(.70986f, .42894f, -.55867f);
        basis.Backward = new(-.40045f, -.40673f, -.82110f);
        var reflected = basis; reflected.Right *= -1; reflected.Backward *= -1;
        var layer = OfficialNormalLayer();
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(4) * basis, Colour = Vector4.One, UV = new(.25f, .625f, .125f, .125f), ParticleSize = 4 };
        var projection = Projection * Matrix.CreateScale(-1, 1, 1);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = RenderOfficialNormal(graphics.GraphicsDevice, layer, null, projection, 256, 256, new(0, 0, 256, 256), false, "normal-background");
            var spatial = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 256, new(0, 0, 256, 256), false, "spatial-normal");
            layer.CameraFacing = 1; particle.Transform = Matrix.CreateScale(4) * reflected;
            var facing = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 256, new(0, 0, 256, 256), false, "reflected-facing-normal");
            Assert.That(ChangedPixels(background, spatial), Is.GreaterThan(30), "The original normal DDS must visibly refract the diagnostic background.");
            Assert.That(MaximumDifference(spatial, facing), Is.LessThanOrEqualTo(1),
                "Mirroring local X to preserve the same quad also changes the normal X basis; CameraFacing must preserve native tangent-space refraction.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeNormalBasis_HorizontalPhysicalPlaneCannotRefractHorizontalStripes()
    {
        const string originalSource = @"vfx\wh_main_machine_gyrocopter_rotor.xml";
        const string originalNormal = @"vfx\textures\melee_slash_1_normal.dds";
        var basis = Matrix.Identity;
        basis.Right = new(0, 0, -1); basis.Up = new(1, 0, 0); basis.Backward = new(0, -1, 0);
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(5) * basis, Colour = new(1, 1, 1, 2), UV = new(0, 0, 1, 1), ParticleSize = 5 };
        var orientation = Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(26), MathHelper.ToRadians(-24), 0);
        var view = Matrix.CreateLookAt(new(5.930199f, 7.5604596f, 12.1587105f), new(0, 1.5374922f, 0), Vector3.Transform(Vector3.Up, orientation));
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 360f / 655, 1, 12000) * Matrix.CreateScale(-1, 1, 1);
        var layer = OfficialNormalLayer();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = Draw(null, true, "horizontal-stripes-background");
            var refracted = Draw(particle, true, "horizontal-stripes-refracted");
            var checkerBackground = Draw(null, false, "pure-x-checker-background");
            var checker = Draw(particle, false, "pure-x-checker-refracted");
            Assert.That(ChangedPixels(checkerBackground, checker), Is.GreaterThan(30), "The official original normal must produce real X refraction in the positive control.");
            Assert.That(MaximumDifference(background, refracted), Is.LessThanOrEqualTo(1),
                "Captured native horizontal T=(0,0,-1), B=(1,0,0), N=(0,-1,0) yields exact d.y=0 for every official normal texel. Pure X refraction cannot change a scene constant along X; packed-normal zero bias must not bend horizontal stripes.");

            Color[] Draw(EffekseerParticle? value, bool stripes, string label) => RenderOfficialNormal(graphics.GraphicsDevice, layer, value, projection,
                1707, 1067, new(0, 0, 360, 655), false, label, normalPath: originalNormal, referenceSourcePath: originalSource, horizontalStripes: stripes, viewOverride: view);
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeSoftDepth_SubViewportSamplesTheActualSceneCoordinates()
    {
        var layer = OfficialNormalLayer(); layer.SoftDistance = 1;
        layer = SoftDepthFields(layer, true, 1);
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(5), Colour = Vector4.One, UV = new(.25f, .625f, .125f, .125f), ParticleSize = 1 };
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 256f / 192, .01f, 100);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = RenderOfficialNormal(graphics.GraphicsDevice, layer, null, projection, 256, 192, new(0, 0, 256, 192), true, "depth-background");
            var full = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 192, new(0, 0, 256, 192), true, "depth-full-viewport");
            var clear = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 192, new(0, 0, 256, 192), false, "depth-clear");
            layer.Distortion *= .5f;
            var partial = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 512, 384, new(73, 41, 256, 192), true, "depth-offset-viewport");
            var unoccluded = Enumerable.Range(52, 88).SelectMany(y => Enumerable.Range(140, 36).Select(x => y * 256 + x)).ToArray();
            var occluded = Enumerable.Range(52, 88).SelectMany(y => Enumerable.Range(84, 34).Select(x => y * 256 + x)).ToArray();
            Assert.That(unoccluded.Count(index => PixelDifference(background[index], full[index]) > 2), Is.GreaterThan(20));
            Assert.That(occluded.Max(index => PixelDifference(background[index], full[index])), Is.LessThanOrEqualTo(1));
            Assert.That(Enumerable.Range(52, 88).Max(y => PixelDifference(background[y * 256 + 127], full[y * 256 + 127])), Is.LessThanOrEqualTo(1),
                "The last occluded depth pixel must not acquire a linearly filtered visibility band.");
            Assert.That(Enumerable.Range(52, 88).SelectMany(y => Enumerable.Range(130, 4).Select(x => y * 256 + x))
                .Max(index => PixelDifference(clear[index], full[index])), Is.LessThanOrEqualTo(1),
                "Beyond the resolve's single half-texel boundary, point-loaded depth must retain full clear-side refraction.");
            Assert.That(MaximumDifference(full, partial), Is.LessThanOrEqualTo(1),
                "The same physical viewport and half-plane depth must match after compensating only the full-RT UV displacement scale; depth lookup must include viewport origin and dimensions.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeSoftDepth_ScaleStrengthChangesOpacityAtTheSamePhysicalGeometry()
    {
        var layer = OfficialNormalLayer(); layer.SoftDistance = 2;
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(5), Colour = Vector4.One, UV = new(.25f, .625f, .125f, .125f), ParticleSize = .25f };
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = RenderOfficialNormal(graphics.GraphicsDevice, layer, null, Projection, 256, 256, new(0, 0, 256, 256), false, "scale-background");
            var weak = RenderOfficialNormal(graphics.GraphicsDevice, SoftDepthFields(layer, true, 0), particle, Projection, 256, 256, new(0, 0, 256, 256), false, "scale-strength-zero", depthDistance: 10.5f);
            var strong = RenderOfficialNormal(graphics.GraphicsDevice, SoftDepthFields(layer, true, 1), particle, Projection, 256, 256, new(0, 0, 256, 256), false, "scale-strength-one", depthDistance: 10.5f);
            var negative = RenderOfficialNormal(graphics.GraphicsDevice, SoftDepthFields(layer, true, 2), particle, Projection, 256, 256, new(0, 0, 256, 256), false, "scale-strength-two", depthDistance: 10.5f);
            Assert.That(ChangedPixels(background, weak), Is.GreaterThan(10));
            Assert.That(TotalDifference(background, strong), Is.GreaterThan(TotalDifference(background, weak) * 2),
                "Native size=.25 and strengths0/1 give approximately quarter/full visibility at a half-unit gap, with identical quad geometry and official normal.");
            Assert.That(MaximumDifference(background, negative), Is.LessThanOrEqualTo(1),
                "Strength2 makes the authored soft denominator negative for size=.25; the native saturated depth ratio is zero.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeSoftDepth_ZeroFactorStillUsesTheOneThousandthDepthTransition()
    {
        var layer = SoftDepthFields(OfficialNormalLayer(), true, 1);
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(5), Colour = Vector4.One, UV = new(.25f, .625f, .125f, .125f), ParticleSize = .25f };
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, 1, 20);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = RenderOfficialNormal(graphics.GraphicsDevice, layer, null, projection, 256, 256, new(0, 0, 256, 256), false, "zero-factor-background");
            var contact = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 256, new(0, 0, 256, 256), false, "zero-factor-contact", depthDistance: 10);
            var quarter = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 256, new(0, 0, 256, 256), false, "zero-factor-quarter", depthDistance: 10.00025f);
            var threeQuarters = RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, projection, 256, 256, new(0, 0, 256, 256), false, "zero-factor-three-quarters", depthDistance: 10.00075f);
            Assert.That(MaximumDifference(background, contact), Is.LessThanOrEqualTo(1), "Enabled soft_factor=0 must still hide particles at contact.");
            Assert.That(TotalDifference(background, quarter), Is.GreaterThan(20));
            Assert.That(TotalDifference(background, threeQuarters), Is.GreaterThan(TotalDifference(background, quarter) * 2),
                "The native +.001 denominator preserves a finite quarter/three-quarter depth transition when soft_factor=0.");
        }
        finally { graphics.Release(); }
    }

    private static EffekseerLayer OfficialNormalLayer()
    {
        var layer = EffekseerLayer.Create(); layer.TextureIndex = 0; layer.Distortion = 1; layer.ZTest = 0;
        return layer;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NativeBasisPayload_MatchesExplicitReferenceWithoutMovingTheQuad(bool normalTexture)
    {
        var layer = OfficialNormalLayer();
        if (!normalTexture) { layer.TextureIndex = -1; layer.DistortionMaskIndex = 0; }
        var texturePath = normalTexture ? @"vfx\textures\general_anim_normal.dds" : @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
        var expectedNormal = normalTexture ? Vector3.UnitZ : new Vector3(.6f, 0, .8f);
        var reference = new EffekseerParticle
        {
            Transform = Matrix.CreateScale(5), Colour = new(1, 1, 1, 2), UV = new(.25f, .625f, .125f, .125f), ParticleSize = 5,
        };
        reference.Transform.Backward = expectedNormal * 5;
        var oldGeometry = reference;
        oldGeometry.Transform.Backward = -reference.Transform.Backward;
        var payload = oldGeometry;
        payload.NormalRotation = normalTexture ? new(1, 0, 0, 1) : new(expectedNormal, 1);
        reference.NormalRotation = payload.NormalRotation;
        // Every z=0 quad corner stays identical when only the unused geometry third row changes.
        foreach (var corner in new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) })
            Assert.That(Vector3.Transform(corner, reference.Transform), Is.EqualTo(Vector3.Transform(corner, oldGeometry.Transform)));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            Color[] Draw(EffekseerParticle? particle, string label) => RenderOfficialNormal(graphics.GraphicsDevice, layer, particle, Projection,
                256, 256, new(0, 0, 256, 256), false, label, normalPath: texturePath, sampleAsMask: !normalTexture);
            var background = Draw(null, "payload-background");
            var equivalent = Draw(reference, "explicit-basis-reference");
            var previous = Draw(oldGeometry, "zero-payload-fallback");
            var actual = Draw(payload, "explicit-basis-payload");
            Assert.That(ChangedPixels(background, equivalent), Is.GreaterThan(20), "Official original texture must produce measurable refraction.");
            Assert.That(ChangedPixels(equivalent, previous), Is.GreaterThan(20), "This control must detect the prior opposite normal basis; a dormant payload cannot pass.");
            Assert.That(MaximumDifference(equivalent, actual), Is.LessThanOrEqualTo(1),
                "Explicit full normal basis must reach the shader while preserving the original quad geometry.");
        }
        finally { graphics.Release(); }
    }

    private static EffekseerLayer SoftDepthFields(EffekseerLayer layer, bool enabled, float strength)
    {
        layer.DistortionSoftDepth = enabled ? 1 : 0;
        layer.DistortionSoftScaleStrength = strength;
        return layer;
    }

    private Color[] RenderOfficialNormal(GraphicsDevice device, EffekseerLayer layer, EffekseerParticle? particle, Matrix projection,
        int width, int height, Rectangle viewport, bool halfDepth, string label, float? depthDistance = null,
        string normalPath = @"vfx\textures\general_anim_normal.dds", string referenceSourcePath = SourcePath, bool horizontalStripes = false, Matrix? viewOverride = null,
        bool sampleAsMask = false)
    {
        var referenceSource = referenceSourcePath == SourcePath ? _source : Read(referenceSourcePath) ?? throw new FileNotFoundException(referenceSourcePath);
        Assert.That(VfxDocument.Read(referenceSource).Root.Descendants("texture_property").Any(property => Key((string?)property.Attribute("path") ?? "") == normalPath), Is.True);
        var bytes = Read(normalPath) ?? throw new FileNotFoundException(normalPath);
        var decoder = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.NonPublic | BindingFlags.Static)!;
        var usage = sampleAsMask ? VfxTextureUsage.Diffuse : VfxTextureUsage.DistortionNormal;
        var image = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))decoder.Invoke(null, [bytes, usage])!;
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, image.Pixels, image.Pixels.Length, image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        device.Viewport = new(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        device.ScissorRectangle = viewport;
        DrawBackground(device, 255, horizontalStripes);
        if (halfDepth || depthDistance != null) DrawSceneDepth(device, projection, depthDistance ?? 9, halfDepth);
        var view = viewOverride ?? Camera.View;
        if (particle is { } value) EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view), Values(projection), [value], 1));
        device.SetRenderTarget(null);
        var pixels = new Color[width * height]; target.GetData(pixels);
        var crop = new Color[viewport.Width * viewport.Height];
        for (var y = 0; y < viewport.Height; ++y) Array.Copy(pixels, (viewport.Y + y) * width + viewport.X, crop, y * viewport.Width, viewport.Width);
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var directory = Path.Combine(output, "official-distortion", TestContext.CurrentContext.Test.Name); Directory.CreateDirectory(directory);
            using (var stream = File.Create(Path.Combine(directory, label + "-full.png"))) target.SaveAsPng(stream, width, height);
            using var cropped = new Texture2D(device, viewport.Width, viewport.Height); cropped.SetData(crop);
            using (var stream = File.Create(Path.Combine(directory, label + ".png"))) cropped.SaveAsPng(stream, viewport.Width, viewport.Height);
            File.WriteAllBytes(Path.Combine(directory, "official-original.xml"), referenceSource);
            File.WriteAllBytes(Path.Combine(directory, sampleAsMask ? "official-opacity-mask.dds" : "official-normal.dds"), bytes);
            File.WriteAllText(Path.Combine(directory, label + ".json"), JsonSerializer.Serialize(new
            {
                SourcePath = referenceSourcePath, OriginalSha256 = Hash(referenceSource), NormalPath = sampleAsMask ? null : normalPath,
                NormalSha256 = sampleAsMask ? null : Hash(bytes), TexturePath = normalPath, TextureSha256 = Hash(bytes), TextureUsage = usage.ToString(), Origins = _origins.Values,
                LayerAbiBytes = Marshal.SizeOf<EffekseerLayer>(), FullRenderTarget = new[] { width, height }, Viewport = new[] { viewport.X, viewport.Y, viewport.Width, viewport.Height },
                HalfDepth = halfDepth, DepthDistance = depthDistance ?? (halfDepth ? 9 : (float?)null), layer.SoftDistance, layer.Distortion, layer.CameraFacing, layer.ZTest,
                SoftDepthEnabled = typeof(EffekseerLayer).GetField("DistortionSoftDepth")?.GetValue(layer), SoftScaleStrength = typeof(EffekseerLayer).GetField("DistortionSoftScaleStrength")?.GetValue(layer),
                Transform = particle == null ? null : Values(particle.Value.Transform), UV = particle == null ? null : new[] { particle.Value.UV.X, particle.Value.UV.Y, particle.Value.UV.Z, particle.Value.UV.W },
                ParticleColour = particle == null ? null : new[] { particle.Value.Colour.X, particle.Value.Colour.Y, particle.Value.Colour.Z, particle.Value.Colour.W },
                ParticleSize = particle?.ParticleSize,
                NormalBasisPayload = particle == null ? null : new[] { particle.Value.NormalRotation.X, particle.Value.NormalRotation.Y, particle.Value.NormalRotation.Z, particle.Value.NormalRotation.W },
                layer.TextureIndex, layer.DistortionMaskIndex,
                View = Values(view), Projection = Values(projection), Background = horizontalStripes ? "Untextured horizontal stripes, scene colour constant along X" : "Untextured checker scene geometry",
                Scope = sampleAsMask
                    ? "Official original diffuse DDS used only as opacity mask in a no-normal fixed-quad diagnostic. Remove normal/gradient/depth; replace simulation with recorded transform, alpha, size, UV and basis payload. Equivalent-reference and target have identical z=0 quad corners; only the unused geometry third row and independent shading payload differ. This is not a complete VFX or native phase acceptance. The checker is untextured scene geometry."
                    : "Official original normal DDS, decoded through the current loader. Normal-only fixed-quad diagnostic, not a complete VFX: remove diffuse/gradient/depth except the explicitly recorded scene surface; replace simulation with the recorded transform, colour/alpha, particle size, UV and basis payload. The checker/stripes and depth surface are scene geometry, never replacement VFX textures."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return crop;
    }

    private static void DrawSceneDepth(GraphicsDevice device, Matrix projection, float distance, bool half)
    {
        var projected = Vector4.Transform(new Vector4(0, 0, -distance, 1), projection);
        var z = projected.Z / projected.W;
        var right = half ? 0 : 1;
        VertexPositionColor[] vertices = [new(new(-1, -1, z), Color.White), new(new(right, -1, z), Color.White), new(new(-1, 1, z), Color.White),
            new(new(-1, 1, z), Color.White), new(new(right, -1, z), Color.White), new(new(right, 1, z), Color.White)];
        using var effect = new BasicEffect(device) { VertexColorEnabled = true, World = Matrix.Identity, View = Matrix.Identity, Projection = Matrix.Identity };
        using var noColour = new BlendState { ColorWriteChannels = ColorWriteChannels.None };
        device.BlendState = noColour; device.DepthStencilState = DepthStencilState.Default; device.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in effect.CurrentTechnique.Passes) { pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2); }
        device.BlendState = BlendState.Opaque;
    }

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    private Color[] Render(GraphicsDevice device, byte[]? source, string label, float time = 3, float depth = 1, int emitters = 1, int particles = 1, byte backgroundAlpha = 255, CommonShaderParameters? cameraOverride = null)
    {
        using var preview = source == null ? null : new EffekseerVfxPreview(device);
        if (preview != null)
        {
            preview.Reload(new(source!, SourcePath, Read));
            Assert.That(preview.EmitterCount, Is.EqualTo(emitters), JsonSerializer.Serialize(preview.Diagnostics));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey is "Vfx.Preview.RuntimeUnavailable" or "Vfx.Preview.LayerTextureUnavailable"), Is.False);
            preview.Seek(time, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.EqualTo(particles));
        }
        using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, depth, 0);
        DrawBackground(device, backgroundAlpha);
        var camera = cameraOverride ?? Camera;
        preview?.Draw(camera);
        device.SetRenderTarget(null);
        var pixels = new Color[Size * Size];
        target.GetData(pixels);
        SaveEvidence(target, source, label, time, depth, backgroundAlpha, camera);
        return pixels;
    }

    private static void DrawBackground(GraphicsDevice device, byte alpha, bool horizontalStripes = false)
    {
        // Untextured scene geometry supplies contrast; every VFX image still comes from an official Pack.
        var vertices = new List<VertexPositionColor>();
        const int cells = 16;
        for (var y = 0; y < cells; y++)
            for (var x = 0; x < cells; x++)
            {
                var colour = ((horizontalStripes ? 0 : x) + y) % 2 == 0 ? new Color(30, 110, 180, (int)alpha) : new Color(210, 70, 35, (int)alpha);
                var left = x * 2f / cells - 1; var right = (x + 1) * 2f / cells - 1;
                var bottom = y * 2f / cells - 1; var top = (y + 1) * 2f / cells - 1;
                vertices.Add(new(new(left, bottom, 0), colour)); vertices.Add(new(new(right, bottom, 0), colour)); vertices.Add(new(new(left, top, 0), colour));
                vertices.Add(new(new(left, top, 0), colour)); vertices.Add(new(new(right, bottom, 0), colour)); vertices.Add(new(new(right, top, 0), colour));
            }
        using var effect = new BasicEffect(device) { VertexColorEnabled = true, World = Matrix.Identity, View = Matrix.Identity, Projection = Matrix.Identity };
        device.DepthStencilState = DepthStencilState.None;
        device.BlendState = BlendState.Opaque;
        device.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices.ToArray(), 0, vertices.Count / 3);
        }
        device.DepthStencilState = DepthStencilState.Default;
    }

    private static byte[] DiagnosticControl(byte[] original)
    {
        var document = VfxDocument.Read(original);
        foreach (var layer in document.Layers.Where(layer => layer.Kind == "Emitter" && layer.Name != Layer || layer.Kind == "Reference").ToArray()) document.RemoveLayer(layer).Apply();
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.ReplaceNodes(
            Scalar("InfiniteLife", "true"), Scalar("MaximumParticles", "1"),
            Curve("EmissionRateCurve", Constant("x", .5f)), Curve("ParticleLifeTime", Constant("x", 2)),
            Curve("ParticleScale", Vector(5, 5, 5)), Curve("ParticleInitialAlpha", Constant("x", 1)),
            new XElement("vector3_property", new XAttribute("name", "ParticleOrientationMin"), new XAttribute("x", "30"), new XAttribute("y", "45"), new XAttribute("z", "0")),
            new XElement("vector3_property", new XAttribute("name", "ParticleOrientationMax"), new XAttribute("x", "30"), new XAttribute("y", "45"), new XAttribute("z", "0")));
        var chain = document.Root.Descendants("behaviour_chain").Single();
        var quality = chain.Elements("quality").Single(q => (string?)q.Attribute("value") == "0");
        chain.Elements("quality").Where(q => q != quality).Remove();
        quality.Elements("modifier").Where(modifier => (string?)modifier.Attribute("fragment") is not ("ps_texture" or "ps_gradient" or "ps_soft_edge" or "vs_animated_texture" or "ps_emissive")).Remove();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_translation"), Scalar("restriction", "3")),
            new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"), Curve("curve_alpha", Constant("alpha", 1))));
        var bytes = document.Write();
        bytes = Edit(bytes, "ps_soft_edge", "soft_factor", "0", scalar: true);
        bytes = Edit(bytes, "vs_animated_texture", "randomize_cell", "false", scalar: true);
        bytes = Edit(bytes, "vs_animated_texture", "no_loops", "float2(1.0,1.0)", scalar: true);
        return bytes;
    }

    private static byte[] AddCrush(byte[] source, float progress)
    {
        var document = VfxDocument.Read(source);
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_alpha_crush"),
            Curve("curve_alpha_crush", Constant("progress", progress)), Scalar("sharp_transition", "false")));
        return document.Write();
    }

    private byte[] SpatialDiagnostic(Vector3 position, Vector3 orientation, float scale, bool uniformMask = false)
    {
        var source = Edit(_control, "vs_animated_texture", "no_loops", "float2(0,0)", scalar: true);
        if (uniformMask)
        {
            source = Edit(source, "ps_gradient", "alpha_mapping", "true", scalar: true);
            source = Edit(source, "ps_gradient", "curve_alpha", Constant("alpha", 1));
        }
        var document = VfxDocument.Read(source);
        SetEmitter(document, "EmitterPosition", Vector(position.X, position.Y, position.Z));
        SetEmitter(document, "ParticleScale", Vector(scale, scale, scale));
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
        {
            var node = document.Root.Descendants("emitter").Single().Elements().Single(property => (string?)property.Attribute("name") == name);
            node.SetAttributeValue("x", orientation.X.ToString(CultureInfo.InvariantCulture));
            node.SetAttributeValue("y", orientation.Y.ToString(CultureInfo.InvariantCulture));
            node.SetAttributeValue("z", orientation.Z.ToString(CultureInfo.InvariantCulture));
        }
        return document.Write();
    }

    private static void SetEmitter(VfxDocument document, string field, string value, bool scalar = false)
    {
        var emitter = document.Root.Descendants("emitter").Single();
        var property = emitter.Elements().SingleOrDefault(property => (string?)property.Attribute("name") == field);
        if (property == null) { property = new XElement("property", new XAttribute("name", field)); emitter.Add(property); }
        if (scalar) property.SetAttributeValue("value", value); else { property.Attribute("value")?.Remove(); property.Value = value; }
    }

    private static byte[] CombineDiagnostics(params (string Name, byte[] Source)[] parts)
    {
        var document = VfxDocument.Read(parts[0].Source);
        foreach (var layer in document.Layers.Where(layer => layer.Kind == "Emitter").ToArray()) document.RemoveLayer(layer).Apply();
        var effect = document.Root.Element("vfx")!;
        foreach (var part in parts)
        {
            var source = VfxDocument.Read(part.Source).Root.Element("vfx")!;
            foreach (var container in new[] { "emitters", "behaviour_chains", "effect_render_stacks" })
            {
                var clone = new XElement(source.Element(container)!.Elements().Single());
                clone.SetAttributeValue("id", part.Name);
                effect.Element(container)!.Add(clone);
            }
        }
        return document.Write();
    }

    private static byte[] Edit(byte[] source, string fragment, string field, string value, bool scalar = false)
    {
        var document = VfxDocument.Read(source);
        var property = document.Root.Descendants("modifier").Single(modifier => (string?)modifier.Attribute("fragment") == fragment)
            .Elements().Single(property => (string?)property.Attribute("name") == field);
        if (scalar) property.SetAttributeValue("value", value); else { property.Attribute("value")?.Remove(); property.Value = value; }
        return document.Write();
    }

    private byte[]? Read(string path)
    {
        if (!_official.TryGetValue(Key(path), out var entry)) return null;
        var bytes = entry.File.DataSource.ReadData();
        _origins[Key(path)] = new { Path = Key(path), Pack = entry.Pack, Sha256 = Hash(bytes), Bytes = bytes.Length };
        return bytes;
    }

    private void SaveEvidence(RenderTarget2D target, byte[]? source, string label, float time, float depth, byte backgroundAlpha, CommonShaderParameters camera)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var test = string.Concat(TestContext.CurrentContext.Test.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var directory = Path.Combine(output, "official-distortion", test);
        Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path.Combine(directory, label + ".png"))) target.SaveAsPng(stream, Size, Size);
        File.WriteAllBytes(Path.Combine(directory, "official-original.xml"), _source);
        if (source != null) File.WriteAllBytes(Path.Combine(directory, label + "-diagnostic.xml"), source);
        File.WriteAllText(Path.Combine(directory, label + "-evidence.json"), JsonSerializer.Serialize(new
        {
            SourcePath, Layer, OriginalSha256 = Hash(_source), ManifestSha256 = _manifestHash,
            DiagnosticSha256 = source == null ? null : Hash(source), Time = time, SurfaceDepth = depth, BackgroundAlpha = backgroundAlpha,
            Scope = "Diagnostic derived from one current official layer. Original VFX and all original DDS bytes retained. Not a full-effect or native visual acceptance.",
            BaselineChanges = new[] { "Remove other emitters/references and other quality branches", "Replace emitter properties: InfiniteLife=true, MaximumParticles=1, EmissionRateCurve=.5, ParticleLifeTime=2, ParticleScale=(5,5,5), ParticleInitialAlpha=1, fixed ParticleOrientationMin/Max=(30,45,0); remaining emitter fields omitted", "Keep original ps_texture, ps_gradient, ps_soft_edge, vs_animated_texture, ps_emissive; replace other modifiers with fixed vs_translation/restriction=3 and vs_vertex_alpha/curve_alpha=1", "ps_soft_edge/soft_factor=0; vs_animated_texture/randomize_cell=false; no_loops=float2(1,1)" },
            Comparison = label, Origins = _origins.Values,
            DiagnosticLayers = source == null ? null : VfxDocument.Read(source).Root.Element("vfx")!.Elements("emitters").Elements("emitter")
                .Select(emitter => new { Name = (string?)emitter.Attribute("id"), Properties = emitter.Elements().Select(property => property.ToString(SaveOptions.DisableFormatting)).ToArray() }).ToArray(),
            Camera = new { Eye = new[] { camera.CameraPosition.X, camera.CameraPosition.Y, camera.CameraPosition.Z }, Target = new[] { camera.CameraLookAt.X, camera.CameraLookAt.Y, camera.CameraLookAt.Z }, FovDegrees = 45, Near = .01f, Far = 100, Viewport = new[] { Size, Size }, Background = "Untextured vertex-colour checker" },
            NativeRawOpacityContract = TestContext.CurrentContext.Test.Name.StartsWith("NativeRawOpacity", StringComparison.Ordinal)
                ? "Official-layer diagnostic additionally freezes atlas, uses orientation (0,90,0), removes diffuse, keeps official gradient with alpha_mapping=false, replaces ParticleInitialAlpha, MaximumParticles, rate=1 and life=20, side camera eye=(10,0,0), time=4. Captured gyro PS23443 and resolve23458 mode1: A2 single T=-1 -> UV offset .08; A2 double T=1 -> zero; A3 double T=4 -> UV offset .18. Bridge packs alpha 255 with float alphaScale=A; CPU raw alpha is asserted before rendering. Not a complete gyro visual comparison." : null
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int ChangedPixels(Color[] left, Color[] right) => left.Zip(right).Count(pair => PixelDifference(pair.First, pair.Second) > 2);
    private static int MaximumDifference(Color[] left, Color[] right) => left.Zip(right).Max(pair => PixelDifference(pair.First, pair.Second));
    private static long TotalDifference(Color[] left, Color[] right) => left.Zip(right).Sum(pair => (long)PixelDifference(pair.First, pair.Second));
    private static int PixelDifference(Color left, Color right) => Math.Max(Math.Abs(left.R - right.R), Math.Max(Math.Abs(left.G - right.G), Math.Abs(left.B - right.B)));
    private static string Key(string path) => path.Replace('/', '\\').Trim().ToLowerInvariant();
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static XElement Scalar(string name, string value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static XElement Curve(string name, string value) => new("property", new XAttribute("name", name), value);
    private static string Constant(string axis, float value) => $"{axis},0,1;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:{axis},0;0,0";
    private static string Vector(float x, float y, float z) => $"{Constant("x", x).Split(':')[0]}|{Constant("y", y).Split(':')[0]}|{Constant("z", z).Split(':')[0]}:x,0;0,0|y,0;1,1|z,0;2,2";
    private static string Rgb(float r, float g, float b) => $"{Constant("r", r).Split(':')[0]}|{Constant("g", g).Split(':')[0]}|{Constant("b", b).Split(':')[0]}:r,0;0,0|g,0;1,1|b,0;2,2";
}
