#nullable enable
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
public class EffekseerVfxPreviewTests
{
    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
    }

    [TestCase("shockwave_normal.dds", 0, 0)]
    [TestCase("shockwave_normal.dds", 1, 0)]
    [TestCase("shockwave_normal.dds", 1, 4)]
    [TestCase("distortion_v1_normal.dds", 0, 0)]
    [TestCase("distortion_v1_normal.dds", 1, 0)]
    [TestCase("distortion_v1_normal.dds", 1, 4)]
    public void Distortion_RefractsTheBackgroundWithoutAddingItsColour(string textureName, int additive, int samples)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var resources = new PackFileContainerLoader(null!).Load(Path.Combine(data!, "vfx.pack"))!;
        var normal = resources.FileList[@"vfx\textures\" + textureName].DataSource.ReadData();
        var document = VfxDocument.Read(VfxTranslationOrientationTests.Source(0, Vector3.Zero, true));
        document.Root.Descendants("property").Single(x => (string?)x.Attribute("name") == "additive_blend_mul")
            .SetAttributeValue("value", additive);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(document.Write(), "vfx/distortion-compositing.xml", _ => normal));
            preview.Seek(3, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            var name = $"distortion-compositing-{textureName}-{additive}-{samples}";
            var background = new Color(40, 80, 120);
            var flat = Render(device, null, width: 20, samples: samples, outputName: name + "-flat-baseline", background: background);
            var refracted = Render(device, preview, width: 20, samples: samples, outputName: name + "-flat", background: background);
            var maximumError = refracted.Zip(flat).Max(pair => Math.Max(Math.Abs(pair.First.R - pair.Second.R),
                Math.Max(Math.Abs(pair.First.G - pair.Second.G), Math.Abs(pair.First.B - pair.Second.B))));
            Assert.That(maximumError, Is.LessThanOrEqualTo(1),
                "Refraction of a uniform background must not expose the rectangular sprite by adding the captured background to itself.");
            var pattern = Render(device, null, width: 20, samples: samples, outputName: name + "-pattern-baseline");
            var warped = Render(device, preview, width: 20, samples: samples, outputName: name + "-pattern");
            Assert.That(warped.Zip(pattern).Count(pair => pair.First != pair.Second), Is.GreaterThan(50),
                "Keep actual normal-map refraction; hiding the distortion layer must not satisfy the uniform-background regression.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void Distortion_BackgroundIncludesParticlesAlreadyDrawnInTheSameFrame(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [255, 255, 255, 255], 4, 1, 1, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [128, 128, 0, 255], 4, 1, 1, 0));
            var colourLayer = EffekseerLayer.Create(); colourLayer.Blend = 1;
            var distortionLayer = EffekseerLayer.Create(); distortionLayer.TextureIndex = 1; distortionLayer.Blend = 1; distortionLayer.Distortion = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [colourLayer, distortionLayer], 2));
            var colour = new EffekseerParticle { Transform = Matrix.CreateScale(8) * Matrix.CreateTranslation(0, 0, -1),
                Colour = new(.2f, .4f, .8f, 1), UV = new(0, 0, 1, 1) };
            var distortion = new EffekseerParticle { Layer = 1, Transform = Matrix.CreateScale(4), Colour = Vector4.One, UV = new(0, 0, 1, 1) };
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
            Vector4 Centre(EffekseerParticle[] particles)
            {
                device.SetRenderTarget(target); device.Clear(new Color(40, 50, 60)); device.DepthStencilState = DepthStencilState.Default;
                EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, MatrixValues(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)),
                    MatrixValues(Matrix.CreateOrthographic(20, 20, .1f, 50)), particles, particles.Length));
                device.SetRenderTarget(null);
                var pixels = new Vector4[64 * 64]; target.GetData(pixels);
                return pixels[32 * 64 + 32];
            }
            var baseline = Centre([colour]);
            var refracted = Centre([colour, distortion]);
            Assert.That(Vector3.Distance(new(baseline.X, baseline.Y, baseline.Z), new(refracted.X, refracted.Y, refracted.Z)),
                Is.LessThan(.002f), "A near-neutral normal must preserve the uniform particle behind it instead of cutting a rectangle back to the empty scene.");
        }
        finally { graphics.Release(); }
    }

    private static float[] MatrixValues(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    [TestCase(0)]
    [TestCase(4)]
    public void OfficialDistortionSample_RendersOffscreenWithoutChangingGameBytes(int samples)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var definitions = loader.Load(Path.Combine(data!, "vfx_desc.pack"))!;
        var resources = loader.Load(Path.Combine(data!, "vfx.pack"))!;
        var source = definitions.FileList[@"vfx\_warscape_test_distortion.xml"].DataSource.ReadData();
        var document = VfxDocument.Read(source);
        var snapshot = new VfxPreviewSnapshot(document.Write(), @"vfx\_warscape_test_distortion.xml",
            path => resources.FileList.GetValueOrDefault(path.ToLowerInvariant())?.DataSource.ReadData());
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(snapshot);
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            preview.Advance(0.25f, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.GreaterThan(0));
            var baseline = Render(device, null, samples: samples);
            var pixels = Render(device, preview, samples: samples);
            var changed = pixels.Zip(baseline).Count(pair => pair.First != pair.Second);
            Assert.That(changed, Is.GreaterThan(50), "The real normal texture must visibly distort the scene.");
            var occluded = Render(device, preview, outputName: "depth-occluded-" + samples, depth: 0, samples: samples);
            Assert.That(occluded, Is.EqualTo(baseline), "Particles must respect the scene's bound depth buffer.");
            preview.Advance(0, Matrix.CreateTranslation(10, 0, 0));
            var attached = Render(device, preview, outputName: "attachment-" + samples, samples: samples);
            Assert.That(attached.SequenceEqual(pixels), Is.True, "Existing default world-space particles must stay put when the attachment moves while paused.");
            var local = VfxDocument.Read(source);
            local.Layers[0].Element.Add(new XElement("property", new XAttribute("name", "ParticleSimulationSpace"),
                new XAttribute("value", "LOCAL_SPACE_POSITION_ONLY")));
            preview.Reload(snapshot with { GameBytes = local.Write() });
            preview.Seek(.25f, Matrix.Identity);
            preview.Advance(0, Matrix.CreateTranslation(10, 0, 0));
            var localAttached = Render(device, preview, outputName: "local-attachment-" + samples, samples: samples);
            Assert.That(localAttached.SequenceEqual(pixels), Is.False, "Explicit local-space particles must follow the same paused attachment update.");
            preview.Reload(snapshot);
            preview.Seek(0, Matrix.Identity);
            preview.Seek(0.25f, Matrix.Identity);
            var replay = Render(device, preview, outputName: "replay-" + samples, samples: samples);
            Assert.That(replay, Is.EqualTo(pixels), "Seeking must rebuild deterministically with the same seed.");
            var edited = VfxDocument.Read(source);
            var textureProperty = edited.Root.Descendants("texture_property").First(x => (string?)x.Attribute("name") == "normal");
            textureProperty.SetAttributeValue("future_attribute", "preserved");
            var emission = edited.Layers[0].Fields.Single(x => x.Name == "EmissionRateCurve");
            emission.Element.Value = emission.Element.Value.Replace("0.0,1.0,", "0.0,4.0,", StringComparison.Ordinal);
            preview.Reload(snapshot with { GameBytes = edited.Write() });
            preview.Seek(0.5f, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.GreaterThan(1), "Reload must increase actual live particles using unsaved emission edits, without counting a synthetic root instance.");
            Assert.That(VfxDocument.Read(edited.Write()).Root.Descendants("texture_property").First(x => (string?)x.Attribute("name") == "normal").Attribute("future_attribute")?.Value, Is.EqualTo("preserved"));
            Assert.That(document.Write(), Is.EqualTo(source));
            var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "distortion-evidence.json"), JsonSerializer.Serialize(new
                {
                    Source = snapshot.SourcePath, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(source)),
                    Runtime = "Effekseer 170e / 92fdeb349c0c7eab9dfaf152047f22dec2fa102d", ChangedPixels = changed,
                    Samples = samples, SceneDepthOcclusion = true, WorldAndLocalAttachmentWhilePaused = true, DeterministicSeek = true,
                    UnsavedReload = true, NativeGameComparison = false, Diagnostics = preview.Diagnostics,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { graphics.Release(); }
    }

    [TestCase("wh_dlc08_lib_warshrine.xml", "particle", 0, 8)]
    [TestCase("wh2_dlc17_lib_entity_spit.xml", "mesh", 1, 2)]
    [TestCase("wh2_dlc12_lib_lizard_acid_explosion.xml", "distortion-production", 2, 5)]
    [TestCase("wh_dlc08_lib_pinning_icicles.xml", "pinning-icicles", 0, 3)]
    [TestCase("cine_embers.xml", "cinematic-embers", 0, 2)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "analytic-model-branch", 1, 1, "branch_3D")]
    [TestCase("wh3_dlc24_lib_ability_petrifying_gaze.xml", "analytic-sprite-whoosh", 0, 1, "BASE_speed_whooshes3")]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-complete", 3, 8, null, 3f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-vines", 3, 1, "vines_decal", 1.5f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-smoke", 0, 1, "smoke", 1.5f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-circle", 3, 1, "decal_circle_edge", 1.5f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-core", 3, 1, "decal_core", .4f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-ring", 3, 1, "decal_ring", 2.5f)]
    public void RealGameSamples_RenderSupportedLayersAndReportRemainder(string name, string outputName, int family, int minimumMappedLayers,
        string? isolatedLayer = null, float previewSeconds = 1)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var definitions = loader.Load(Path.Combine(data!, "vfx_desc.pack"))!;
        var resources = new[] { "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "data.pack" }
            .Select(pack => loader.Load(Path.Combine(data!, pack))!).Append(definitions).ToArray();
        var path = "vfx\\" + name;
        var source = definitions.FileList[path].DataSource.ReadData();
        var snapshot = new VfxPreviewSnapshot(source, path, key => resources.Select(x => x.FileList.GetValueOrDefault(key.ToLowerInvariant()))
            .FirstOrDefault(x => x != null)?.DataSource.ReadData());
        if (isolatedLayer != null)
        {
            var isolated = VfxDocument.Read(source);
            isolated.Root.Elements("vfx_references").Remove();
            foreach (var layer in isolated.Layers.Where(x => x.Kind == "Emitter" && x.Name != isolatedLayer).ToArray()) layer.Element.Remove();
            snapshot = snapshot with { GameBytes = isolated.Write() };
        }
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters.Count, Is.GreaterThanOrEqualTo(minimumMappedLayers), JsonSerializer.Serialize(conversion.Diagnostics));
        Assert.That(conversion.Emitters.Any(x => (x.Distortion != 0 ? 2 : x.Kind == 2 ? 3 : x.Kind) == family), Is.True);
        if (isolatedLayer != null)
        {
            Assert.That(conversion.Emitters.Single().Name, Is.EqualTo(isolatedLayer));
            Assert.That(conversion.Diagnostics.Any(x => x.ReasonKey == (family == 3 ? "Vfx.Preview.ProjectedDecalCandidate"
                : isolatedLayer == "smoke" ? "Vfx.Preview.FixedSpeedAtlasCandidate" : "Vfx.Preview.AnalyticCurveCalibrated")), Is.True);
        }
        if (outputName == "cinematic-embers")
            Assert.That(conversion.Emitters.Count + conversion.DisabledEmitterCount, Is.EqualTo(conversion.TotalEmitterCount),
                "Every enabled authored layer of this typical effect must be represented; disabled layers stay disabled.");
        if (outputName == "pinning-icicles")
            Assert.That(conversion.Diagnostics.Any(x => x.Field == "shaders/vfx_waterfall.xml.shader"
                && x.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True,
                "A custom three-mask scrolling material must not be reported as a missing ordinary diffuse texture or replaced by one.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(snapshot);
            Assert.That(preview.EmitterCount, Is.EqualTo(conversion.Emitters.Count(e => !e.IsEventTemplate)), JsonSerializer.Serialize(preview.Diagnostics));
            var seconds = isolatedLayer != null || family == 3 ? previewSeconds : family == 0 ? 0.5f : 0.2f;
            preview.Seek(seconds, Matrix.Identity);
            var simulation = new TotalWarVfxSimulation(conversion);
            simulation.Seek(seconds, Matrix.Identity);
            var frame = simulation.Frame(new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 100), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            Assert.That(frame, Is.Not.Empty);
            var meshRadius = conversion.Models.SelectMany(x => x.Vertices).Select(x => x.Position.Length()).DefaultIfEmpty(1).Max();
            var minimum = new Vector3(float.MaxValue); var maximum = new Vector3(float.MinValue);
            foreach (var particle in frame)
            {
                var radius = Math.Max(particle.Transform.Right.Length(), particle.Transform.Up.Length()) * Math.Max(1, meshRadius);
                minimum = Vector3.Min(minimum, particle.Transform.Translation - new Vector3(radius));
                maximum = Vector3.Max(maximum, particle.Transform.Translation + new Vector3(radius));
            }
            var extent = maximum - minimum;
            var width = Math.Max(1, Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) * 1.1f);
            var centre = (maximum + minimum) / 2;
            var baseline = Render(device, null, width, centre, outputName + "-baseline");
            var pixels = Render(device, preview, width, centre, outputName, groundHeight: family == 3 ? 0 : null);
            var changed = pixels.Zip(baseline).Count(pair => pair.First != pair.Second);
            Assert.That(changed, Is.GreaterThan(outputName == "entangle-core" ? 0 : 20),
                "The original assets must produce pixels; the very faint native core must not be artificially brightened.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
                File.WriteAllText(Path.Combine(output, outputName + "-evidence.json"), JsonSerializer.Serialize(new
                {
                    Source = path, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(source)), IsolatedLayer = isolatedLayer,
                    MappedLayers = preview.EmitterCount, TotalLayers = preview.TotalEmitterCount, DisabledLayers = conversion.DisabledEmitterCount, ChangedPixels = changed,
                    TimeSeconds = seconds, Width = width, NativeGameComparison = false, Diagnostics = preview.Diagnostics,
                }, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.Out.WriteLine($"{name}: {preview.EmitterCount}/{preview.TotalEmitterCount} layers, {changed} changed pixels.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void SoftEdges_UseActualSceneDepthIncludingMultisampledTargets(int samples)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var definitions = loader.Load(Path.Combine(data!, "vfx_desc.pack"))!;
        var resources = loader.Load(Path.Combine(data!, "vfx.pack"))!;
        var document = VfxDocument.Read(definitions.FileList[@"vfx\_warscape_test_distortion.xml"].DataSource.ReadData());
        foreach (var factor in document.Root.Descendants().Where(x => (string?)x.Attribute("name") == "soft_factor"))
            factor.SetAttributeValue("value", "2.0");
        var snapshot = new VfxPreviewSnapshot(document.Write(), @"vfx\_warscape_test_distortion.xml",
            path => resources.FileList.GetValueOrDefault(path.ToLowerInvariant())?.DataSource.ReadData());
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(snapshot); preview.Seek(0.25f, Matrix.Identity);
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            var baseline = Render(device, null, samples: samples);
            var full = Render(device, preview, outputName: "soft-full-" + samples, samples: samples);
            var parameters = SceneParameters(40, Vector3.Zero);
            var nearDepth = Vector4.Transform(new Vector4(0, 0, -parameters.CameraPosition.Length() - 0.1f, 1), parameters.Projection);
            var softened = Render(device, preview, outputName: "soft-contact-" + samples, depth: nearDepth.Z / nearDepth.W, samples: samples);
            long Difference(Color[] pixels) => pixels.Zip(baseline).Sum(pair => (long)Math.Abs(pair.First.R - pair.Second.R)
                + Math.Abs(pair.First.G - pair.Second.G) + Math.Abs(pair.First.B - pair.Second.B));
            Assert.Multiple(() =>
            {
                Assert.That(Difference(softened), Is.GreaterThan(0));
                Assert.That(Difference(softened), Is.LessThan(Difference(full) * 0.8), "Depth contact must fade the effect before ordinary depth testing rejects it.");
                Assert.That(preview.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.SceneDepthUnavailable"), Is.False);
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeCalibratedGameInterpolation_RecoversThePreviouslyBlockedWhooshWithoutChangingSource()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var definitions = loader.Load(Path.Combine(data!, "vfx_desc.pack"))!;
        var textures = loader.Load(Path.Combine(data!, "vfx.pack"))!;
        const string path = @"vfx\wh3_dlc24_lib_ability_petrifying_gaze.xml";
        var bytes = definitions.FileList[path].DataSource.ReadData();
        var result = TotalWarVfxConverter.Convert(new(bytes, path, key => textures.FileList.GetValueOrDefault(key.ToLowerInvariant())?.DataSource.ReadData()));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(2));
            Assert.That(result.Diagnostics.Any(x => x.Layer == "BASE_speed_whooshes3" && x.Field == "ParticleScale"
                && x.ReasonKey == "Vfx.Preview.AnalyticCurveCalibrated"), Is.True);
            Assert.That(result.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.InterpolationUnsupported"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, 4)]
    public void PremultipliedGameMaterial_SeparatesCoverageAndHdrEmissionWithoutChangingTheSource(bool filteredEdge, int samples)
    {
        var source = System.Text.Encoding.UTF8.GetBytes("""
            <vfx revision="2"><emitters><emitter id="test" type="continuous_mesh"><property name="InfiniteLife" value="true"/>
            <property name="EmissionRateCurve">x,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0</property>
            <property name="ParticleLifeTime">x,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0</property>
            <property name="ParticleScale">x,0,0;0,5,linear,linear,0,0,0,0,1|y,0,0;0,5,linear,linear,0,0,0,0,1|z,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="test"><quality value="0">
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/control.tga"/>
            <property name="pre_multiplied_alpha" value="true"/></modifier>
            <modifier fragment="ps_lighting"><property name="additive_blend_mul" value="2"/></modifier>
            <modifier fragment="ps_soft_edge"><property name="soft_factor" value="2"/></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 0, 26, 204, 128];
        if (filteredEdge)
            texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 1, 0, 32, 40, 0, 26, 204, 128, 204, 26, 0, 128];
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(source, "vfx/control.xml", _ => texture));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            preview.Seek(0, Matrix.Identity);
            Assert.That(preview.ParticleCount, Is.EqualTo(1), "Coverage and emission must not double the simulated particle count.");
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24, samples, RenderTargetUsage.DiscardContents);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(20, 20, 0.1f, 50),
                new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            preview.Draw(parameters);
            device.SetRenderTarget(null);
            var pixels = new Vector4[64 * 64]; target.GetData(pixels);
            var colour = (pixels[32 * 64 + 31] + pixels[32 * 64 + 32]) / 2;
            var sampled = filteredEdge ? new Vector3(.301914f, .010330f, .301914f) : new Vector3(.603827f, .010330f, 0);
            var covered = Vector3.Min(sampled, new Vector3(.215861f));
            var emission = Vector3.Max(sampled - new Vector3(.215861f), Vector3.Zero);
            var emitted = emission * 2 * Vector3.Dot(emission, new(.2126f, .7152f, .0722f));
            var expected = covered + emitted;
            Assert.Multiple(() =>
            {
                Assert.That(colour.X, Is.EqualTo(expected.X).Within(.004),
                    "Split the filtered sRGB texture, then apply the independent emission-luminance shape.");
                Assert.That(colour.Y, Is.EqualTo(0.01033).Within(0.005), "The covered colour must not be incorrectly added or multiplied by alpha twice.");
                Assert.That(colour.Z, Is.EqualTo(expected.Z).Within(.004));
                Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            });
            var contact = Vector4.Transform(new Vector4(0, 0, -10.1f, 1), parameters.Projection);
            device.SetRenderTarget(target);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, contact.Z / contact.W, 0);
            preview.Draw(parameters); device.SetRenderTarget(null); target.GetData(pixels);
            var softened = (pixels[32 * 64 + 31] + pixels[32 * 64 + 32]) / 2;
            Assert.That(softened.X, Is.EqualTo(covered.X * .05f + emitted.X * .05f * .05f).Within(.003),
                "Native default-pass depth fade affects both the emitted colour and its shape, including MSAA depth.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeRenderer_PreservesHdrColourBeforeTheSceneOutputClampsIt()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            Assert.That(handle.IsInvalid, Is.False);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [255, 255, 255, 255], 4, 1, 1, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [EffekseerLayer.Create()], 1));
            var particle = new EffekseerParticle { Transform = Matrix.CreateScale(10), Colour = new(3, 2, 1, 1), UV = new(0, 0, 1, 1) };
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)),
                Values(Matrix.CreateOrthographic(20, 20, 0.1f, 50)), [particle], 1));
            device.SetRenderTarget(null);
            var pixels = new Vector4[64 * 64]; target.GetData(pixels);
            var centre = pixels[32 * 64 + 32];
            Assert.Multiple(() =>
            {
                Assert.That(centre.X, Is.EqualTo(3).Within(0.03));
                Assert.That(centre.Y, Is.EqualTo(2).Within(0.03));
                Assert.That(centre.Z, Is.EqualTo(1).Within(0.03));
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public void NativeRenderer_BlendsAdjacentAtlasCellsAndRespectsTheLastFrame(bool loop, bool customMaterial)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            var texture = new byte[16 * 8 * 4];
            for (var y = 0; y < 8; ++y)
                for (var x = 0; x < 16; ++x)
                {
                    var offset = (y * 16 + x) * 4;
                    texture[offset + (x < 8 ? 0 : 2)] = 255;
                    texture[offset + 3] = 255;
                }
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, texture, texture.Length, 16, 8, 0));
            var layer = EffekseerLayer.Create();
            layer.MaterialMode = customMaterial ? 1 : 0;
            layer.AtlasColumns = 2; layer.AtlasRows = 1; layer.AtlasLoop = loop ? 1 : 0; layer.AtlasBlend = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
            foreach (var frame in new[] { 0.5f, 1.5f })
            {
                var particle = new EffekseerParticle { Transform = Matrix.CreateScale(10), Colour = Vector4.One,
                    UV = new(frame < 1 ? 0 : 0.5f, 0, 0.5f, 1), Flipbook = frame };
                device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
                EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up)),
                    Values(Matrix.CreateOrthographic(20, 20, 0.1f, 50)), [particle], 1));
                device.SetRenderTarget(null);
                var pixels = new Vector4[64 * 64]; target.GetData(pixels);
                var expected = !loop && frame > 1 ? new Vector4(0, 0, 1, 1) : new Vector4(0.5f, 0, 0.5f, 1);
                Assert.That(Vector4.Distance(pixels[32 * 64 + 32], expected), Is.LessThan(0.04),
                    "The renderer must blend texture samples, including the wrap or clamp at the last authored atlas frame.");
            }
        }
        finally { graphics.Release(); }
    }

    private static CommonShaderParameters SceneParameters(float width, Vector3 centre)
    {
        var pitch = MathHelper.ToRadians(24); var yaw = MathHelper.ToRadians(26);
        var camera = centre + new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw)) * Math.Max(50, width * 2);
        return new(Matrix.CreateLookAt(camera, centre, Vector3.Up), Matrix.CreateOrthographic(width, width, 0.1f, Math.Max(100, width * 4)),
            camera, centre, 0, 0, 0, 1, Vector3.One, []);
    }

    private static Color[] Render(GraphicsDevice device, IVfxPreview? preview, float width = 40, Vector3 centre = default, string? outputName = null,
        float depth = 1, int samples = 0, float? groundHeight = null, Color? background = null)
    {
        const int size = 256;
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        using var white = new Texture2D(device, 1, 1);
        white.SetData([Color.White]);
        using var batch = new SpriteBatch(device);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, depth, 0);
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
        for (var y = 0; y < size; y += 16)
            for (var x = 0; x < size; x += 16)
                batch.Draw(white, new Rectangle(x, y, 16, 16), background ?? ((x / 16 + y / 16) % 2 == 0 ? new Color(50, 80, 110) : new Color(180, 150, 100)));
        batch.End();
        device.DepthStencilState = DepthStencilState.Default;
        preview?.Draw(SceneParameters(width, centre) with { VfxGroundHeight = groundHeight });
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
        batch.Draw(white, new Rectangle(0, 0, 8, 8), Color.Cyan);
        batch.End();
        device.SetRenderTarget(null);
        var pixels = new Color[size * size];
        target.GetData(pixels);
        Assert.That(pixels[4 * size + 4], Is.EqualTo(Color.Cyan), "MonoGame must continue drawing correctly after the native renderer restores its state.");
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var stream = File.Create(Path.Combine(output, (outputName ?? (preview == null ? "scene-baseline" : "distortion")) + ".png"));
            target.SaveAsPng(stream, size, size);
        }
        return pixels;
    }
}
