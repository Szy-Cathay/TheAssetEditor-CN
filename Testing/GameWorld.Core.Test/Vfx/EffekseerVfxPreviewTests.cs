#nullable enable
using System.Security.Cryptography;
using System.Reflection;
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
            // Keep the full quad in view while making the official texture's refraction spatially resolvable.
            var flat = Render(device, null, width: 10, samples: samples, outputName: name + "-flat-baseline", background: background);
            var refracted = Render(device, preview, width: 10, samples: samples, outputName: name + "-flat", background: background);
            var maximumError = refracted.Zip(flat).Max(pair => Math.Max(Math.Abs(pair.First.R - pair.Second.R),
                Math.Max(Math.Abs(pair.First.G - pair.Second.G), Math.Abs(pair.First.B - pair.Second.B))));
            Assert.That(maximumError, Is.LessThanOrEqualTo(1),
                "Refraction of a uniform background must not expose the rectangular sprite by adding the captured background to itself.");
            var pattern = Render(device, null, width: 10, samples: samples, outputName: name + "-pattern-baseline");
            var warped = Render(device, preview, width: 10, samples: samples, outputName: name + "-pattern");
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
    [TestCase("wh2_dlc10_entangle_aoe.xml", "analytic-model-branch", 1, 1, "branch_3D", 1f, true)]
    [TestCase("wh3_dlc24_lib_ability_petrifying_gaze.xml", "analytic-sprite-whoosh", 0, 1, "BASE_speed_whooshes3")]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-complete", 3, 8, null, 3f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-vines", 3, 1, "vines_decal", 1.5f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-smoke", 0, 1, "smoke", 1.5f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-circle", 3, 1, "decal_circle_edge", 1.5f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-core", 3, 1, "decal_core", .4f)]
    [TestCase("wh2_dlc10_entangle_aoe.xml", "entangle-ring", 3, 1, "decal_ring", 2.5f)]
    public void RealGameSamples_RenderSupportedLayersAndReportRemainder(string name, string outputName, int family, int minimumMappedLayers,
        string? isolatedLayer = null, float previewSeconds = 1, bool authoredAlphaPlateau = false)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var definitions = loader.Load(Path.Combine(data!, "vfx_desc.pack"))!;
        var resourcePacks = new[] { "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "data.pack", "commontextures.pack", "models2e_dds.pack" };
        var manifest = File.ReadLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 2 && parts[0].EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        foreach (var pack in resourcePacks.Append("vfx_desc.pack"))
        {
            Assert.That(manifest.ContainsKey(pack), Is.True, $"The fixture must only load manifest-owned original packs: {pack}");
            Assert.That(new FileInfo(Path.Combine(data!, pack)).Length, Is.EqualTo(manifest[pack]), $"Original pack length: {pack}");
        }
        var resources = resourcePacks
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
        var sourceSeconds = isolatedLayer != null || family == 3 ? previewSeconds : family == 0 ? 0.5f : 0.2f;
        var plateau = authoredAlphaPlateau ? SourceAlphaPlateau(source, isolatedLayer!) : null;
        var stages = plateau == null ? new[] { sourceSeconds } : new[] { sourceSeconds, plateau.StageSeconds };
        string? originalParticleIdentities = null;
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(snapshot);
            Assert.That(preview.EmitterCount, Is.EqualTo(conversion.Emitters.Count(e => !e.IsEventTemplate)), JsonSerializer.Serialize(preview.Diagnostics));
            var simulation = new TotalWarVfxSimulation(conversion);
            foreach (var seconds in stages)
            {
                var stageOutputName = seconds == sourceSeconds ? outputName : outputName + "-alpha-plateau";
                preview.Seek(seconds, Matrix.Identity);
                simulation.Seek(seconds, Matrix.Identity);
                var frame = simulation.ProjectedFrame(SceneParameters(1, Vector3.Zero), applyCameraCulling: false);
                Assert.That(frame, Is.Not.Empty);
                var modelCorners = new Dictionary<(int Model, int Frame), Vector3[]>();
                var points = new List<Vector3>();
                foreach (var projected in frame)
                {
                    var particle = projected.Render;
                    foreach (var part in conversion.Emitters[particle.Layer].RenderParts(projected.ModelVariant))
                    {
                        Vector3[] corners;
                        if (part.ModelIndex >= 0)
                        {
                            var model = conversion.Models[part.ModelIndex];
                            var modelFrame = (int)MathF.Floor(Math.Max(0, particle.AppearancePhase) * model.AnimationCycles * model.AnimationFrameCount)
                                % model.AnimationFrameCount;
                            if (!modelCorners.TryGetValue((part.ModelIndex, modelFrame), out corners!))
                            {
                                var local = model.AnimationData?.Bounds(model.Vertices, modelFrame)
                                    ?? BoundingBox.CreateFromPoints((model.AnimationVertices ?? model.Vertices)
                                        .Skip(model.AnimationVertices == null ? 0 : modelFrame * model.Vertices.Length)
                                        .Take(model.Vertices.Length).Select(vertex => vertex.Position));
                                corners = local.GetCorners(); modelCorners.Add((part.ModelIndex, modelFrame), corners);
                            }
                        }
                        else
                        {
                            var pivot = part.Pivot;
                            corners = [new(-pivot.X,-pivot.Y,0),new(1-pivot.X,-pivot.Y,0),
                                new(-pivot.X,1-pivot.Y,0),new(1-pivot.X,1-pivot.Y,0)];
                        }
                        points.AddRange(corners.Select(point => Vector3.Transform(point, particle.Transform)));
                    }
                }
                Assert.That(points, Is.Not.Empty);
                var worldBounds = BoundingBox.CreateFromPoints(points);
                var centre = (worldBounds.Min + worldBounds.Max) * .5f;
                var orientation = SceneParameters(1, Vector3.Zero).View;
                var projectedBounds = BoundingBox.CreateFromPoints(points.Select(point => Vector3.Transform(point - centre, orientation)));
                var screenCentre = (projectedBounds.Min + projectedBounds.Max) * .5f;
                centre += Vector3.TransformNormal(new Vector3(screenCentre.X, screenCentre.Y, 0), Matrix.Invert(orientation));
                var width = Math.Max(.05f, Math.Max(projectedBounds.Max.X - projectedBounds.Min.X,
                    projectedBounds.Max.Y - projectedBounds.Min.Y) * 1.1f);
                var baseline = Render(device, null, width, centre, stageOutputName + "-baseline");
                var pixels = Render(device, preview, width, centre, stageOutputName, groundHeight: family == 3 ? 0 : null);
                var changed = pixels.Zip(baseline).Count(pair => pair.First != pair.Second);
                var rgba = pixels.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A }).ToArray();
                var pixelSha256 = Convert.ToHexStringLower(SHA256.HashData(rgba));
                var previewSimulation = (TotalWarVfxSimulation)typeof(EffekseerVfxPreview)
                    .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                var particleIdentities = plateau == null ? null : OriginalParticleIdentities(previewSimulation);
                if (plateau != null)
                {
                    Assert.That(particleIdentities, Is.EqualTo(OriginalParticleIdentities(simulation)), "The GPU submission and independent inspection must retain the same original births, random values and lifetimes.");
                    originalParticleIdentities ??= particleIdentities;
                    using var originalIdentities = JsonDocument.Parse(originalParticleIdentities);
                    using var currentIdentities = JsonDocument.Parse(particleIdentities!);
                    var original = originalIdentities.RootElement.EnumerateArray().Select(item => item.GetRawText()).ToArray();
                    var current = currentIdentities.RootElement.EnumerateArray().Select(item => item.GetRawText()).ToArray();
                    Assert.That(current.Distinct().Count(), Is.EqualTo(current.Length), "No original identity may be duplicated or respawned.");
                    Assert.That(original.Except(current), Is.Empty, "Every original birth, random value, lifetime and initial transform must remain exactly intact.");
                    foreach (var added in current.Except(original))
                    {
                        using var identity = JsonDocument.Parse(added);
                        var birth = identity.RootElement.GetProperty("Birth").GetDouble();
                        Assert.That(birth, Is.GreaterThan(plateau.AuthoredEmissionEnd), "Only the original finite emission interval's last partial tick may add a particle after the first stage.");
                        Assert.That(birth, Is.LessThanOrEqualTo(plateau.LastBirth), "A finite source must not emit beyond its original float interval plus one fixed tick.");
                    }
                }
                var coverage = plateau == null ? null : Native176NoiseFieldStatistics(conversion, frame);
                if (plateau != null)
                {
                    var submittedOpaque = (List<EffekseerParticle>)typeof(EffekseerVfxPreview)
                        .GetField("_opaqueParticles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                    Assert.That(submittedOpaque.Count, Is.EqualTo(frame.Length), "Every original branch packet must actually reach the opaque GPU route in both stages.");
                }
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var actualCamera = SceneParameters(width, centre) with { VfxGroundHeight = family == 3 ? 0 : null };
                    var projected = simulation.ProjectedFrame(actualCamera);
                    var rendered = (List<EffekseerParticle>)typeof(EffekseerVfxPreview)
                        .GetField("_renderedParticles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                    var opaque = (List<EffekseerParticle>)typeof(EffekseerVfxPreview)
                        .GetField("_opaqueParticles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                    var prepared = changed <= 20 && conversion.MeshMaterials.Any(material => material.Parameters.Length is 60 or 68)
                        ? VfxPreparedResources.Prepare(conversion) : null;
                    if (plateau != null) File.WriteAllBytes(Path.Combine(output, stageOutputName + ".rgba"), rgba);
                    File.WriteAllText(Path.Combine(output, stageOutputName + "-evidence.json"), JsonSerializer.Serialize(new
                    {
                        Source = path, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(source)), IsolatedLayer = isolatedLayer,
                        SelectedSnapshotSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshot.GameBytes)),
                        RgbaSha256 = pixelSha256, RgbaWidth = 256, RgbaHeight = 256,
                        StagePurpose = plateau == null ? "Original visibility stage" : seconds == sourceSeconds
                            ? "Original sparse growth control; not a meaningful visibility pass" : "First authored full-alpha plateau visibility stage",
                        AuthoredPlateau = plateau, OriginalParticleIdentities = particleIdentities, NativeNoiseFieldStatistics = coverage,
                        MappedLayers = preview.EmitterCount, TotalLayers = preview.TotalEmitterCount, DisabledLayers = conversion.DisabledEmitterCount, ChangedPixels = changed,
                        CameraPosition = actualCamera.CameraPosition.ToString(), CameraTarget = centre.ToString(),
                        ActualWorldBounds = worldBounds.ToString(), ViewSpaceGeometryBounds = projectedBounds.ToString(),
                        InspectionParticles = frame.Length, FormalProjectedParticles = projected.Length,
                        Frames = projected.Select(item => new { conversion.Emitters[item.Render.Layer].Name, item.ModelVariant,
                            item.Render.Layer, Position = item.Render.Transform.Translation.ToString(),
                            Transform = MatrixValues(item.Render.Transform),
                            CameraDistance = Vector3.Distance(actualCamera.CameraPosition, item.Render.Transform.Translation),
                            CullDistances = conversion.Emitters[item.Render.Layer].ParticleCullDistances?.ToString(),
                            Colour = item.Render.Colour.ToString(), item.Render.Time, item.Render.MaterialTime,
                            item.Render.AppearancePhase, UV = item.Render.UV.ToString(), InstanceUvPayload = item.Render.NormalRotation.ToString() }).ToArray(),
                        ActualColourSubmittedCount = rendered.Count, ActualOpaqueSubmittedCount = opaque.Count,
                        ActualSubmittedPackets = opaque.Concat(rendered).Select(item => new { item.Layer, Position = item.Transform.Translation.ToString(),
                            Transform = MatrixValues(item.Transform),
                            Colour = item.Colour.ToString(), item.Time, item.MaterialTime, item.AppearancePhase,
                            UV = item.UV.ToString(), InstanceUvPayload = item.NormalRotation.ToString() }).ToArray(),
                        MaterialInputs = conversion.MeshMaterials.Select((material,index) => new
                        {
                            Index = index, material.Parameters, material.TextureIndexes,
                            TexturePaths = material.TextureIndexes.Select(slot => conversion.TexturePaths[slot]).ToArray(),
                            MaskRectangles = prepared?.Masks[index]?.Atlas?.Rectangles,
                            SamplingInputs = prepared == null ? null : material.TextureIndexes.Select(slot => new
                            {
                                Index = slot, Path = conversion.TexturePaths[slot], Usage = conversion.TextureUsages[slot].ToString(),
                                Sha256 = Convert.ToHexStringLower(SHA256.HashData(conversion.Textures[slot])),
                                Width = prepared.Textures[slot].Image?.Width, Height = prepared.Textures[slot].Image?.Height,
                                Srgb = prepared.Textures[slot].Image?.Srgb, MipLevels = prepared.Textures[slot].Image?.MipLevels,
                                UnfilteredTopMipCentreRgba = prepared.Textures[slot].Image is { } image
                                    ? image.Pixels.AsSpan(((image.Height/2)*image.Width+image.Width/2)*4,4).ToArray().Select(value => (int)value).ToArray() : null,
                                DecodeFailure = prepared.Textures[slot].Failure?.Message,
                            }).ToArray(),
                        }).ToArray(),
                        DiagnosticModelGeometry = prepared == null ? null : conversion.Models.Select((model,index) => new
                        {
                            Index = index, model.AnimationFrameCount, HasCompactAnimation = model.AnimationData != null,
                            HasBakedAnimation = model.AnimationVertices != null,
                            PositionUv = model.Vertices.Select(vertex => new[] { vertex.Position.X, vertex.Position.Y, vertex.Position.Z, vertex.UV.X, vertex.UV.Y }).ToArray(),
                            model.Indices,
                        }).ToArray(),
                        DiagnosticScope = "Exact source stage and original parameters retained. Material atlas rectangles are reconstructed by the production CPU preparation, not a GPU constant-buffer capture. Geometry inspection is pre-cull; formal and submitted packets expose actual culling/alpha.",
                        TimeSeconds = seconds, Width = width, NativeGameComparison = false, Diagnostics = preview.Diagnostics,
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                if (plateau != null && seconds == sourceSeconds)
                {
                    Assert.That(frame.All(item => item.Render.AppearancePhase < plateau.PhaseStart && item.Render.Colour.W < 1), Is.True,
                        "Retain the original growth stage and its genuine low alpha; do not inflate it into the plateau.");
                    Assert.That(coverage!.FullyCoveredParticles, Is.Zero, "The native original red-noise field must remain sparse in this early stage.");
                }
                else
                {
                    Assert.That(changed, Is.GreaterThan(outputName == "entangle-core" ? 0 : 20),
                        "The original assets must produce pixels; the very faint native core must not be artificially brightened.");
                    if (plateau != null)
                    {
                        Assert.That(frame.All(item => item.Render.Colour.W == 1), Is.True, "The unmodified source curve has full alpha inside its first authored plateau.");
                        Assert.That(coverage!.FullyCoveredParticles, Is.EqualTo(frame.Length));
                    }
                }
                Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
                TestContext.Out.WriteLine($"{name} at {seconds}s: {preview.EmitterCount}/{preview.TotalEmitterCount} layers, {changed} changed pixels; RGBA {pixelSha256}.");

            }
        }
        finally { graphics.Release(); }
    }

    private sealed record AuthoredAlphaPlateau(double PhaseStart, double PhaseEnd, double Start, double AuthoredEmissionEnd,
        float FloatStart, float FloatDuration, double FloatEmissionEnd, double TickSeconds, double LastBirth,
        double MinimumLife, double MaximumLife, double TableInteriorMargin, double SafeStartSeconds, double SafeEndSeconds,
        float StageSeconds, string OriginalCurve);

    private static AuthoredAlphaPlateau SourceAlphaPlateau(byte[] source, string layer)
    {
        var root = VfxDocument.Read(source).Root.Element("vfx")!;
        var emitter = root.Elements("emitters").Elements("emitter").Single(node => (string?)node.Attribute("id") == layer);
        var properties = emitter.Elements().Where(node => (string?)node.Attribute("quality") == "0")
            .ToDictionary(node => (string)node.Attribute("name")!);
        double Bound(string name, string bound) => double.Parse((string)properties[name].Attribute(bound)!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(Bound("StartTime", "min"), Is.EqualTo(Bound("StartTime", "max")), "The source stage proof requires a fixed start.");
        Assert.That(Bound("LifeTime", "min"), Is.EqualTo(Bound("LifeTime", "max")), "The source stage proof requires a fixed emission interval.");
        Assert.That(VfxCurve.TryParse(properties["ParticleLifeTime"].Value, out var life), Is.True);
        Assert.That(VfxCurve.TryParse(properties["ParticleInitialAlpha"].Value, out var initialAlpha), Is.True);
        Assert.That(life!.Channels.All(channel => channel.Points.Count == 1), Is.True);
        Assert.That(initialAlpha!.Channels.All(channel => channel.Points.All(point => point.Value == 1)), Is.True);
        var alphaText = root.Elements("behaviour_chains").Elements("behaviour_chain")
            .Single(node => (string?)node.Attribute("id") == layer).Elements("quality")
            .Single(node => (string?)node.Attribute("value") == "0").Descendants("property")
            .Single(node => (string?)node.Attribute("name") == "curve_alpha").Value;
        Assert.That(VfxCurve.TryParse(alphaText, out var alpha), Is.True);
        var plateaus = alpha!.Channels.Select(channel => channel.Points.Zip(channel.Points.Skip(1))
            .First(pair => pair.First.Value == 1 && pair.Second.Value == 1)).ToArray();
        var phaseStart = plateaus.Max(pair => pair.First.Time);
        var phaseEnd = plateaus.Min(pair => pair.Second.Time);
        var start = Bound("StartTime", "min"); var duration = Bound("LifeTime", "max");
        var authoredEnd = start + duration;
        var floatStart = (float)start; var floatDuration = (float)duration;
        var floatEnd = (double)floatStart + floatDuration;
        const double tick = 1.0 / 120;
        var lastBirth = floatEnd + tick;
        var minimumLife = life.Channels.Min(channel => channel.Points[0].Value);
        var maximumLife = life.Channels.Max(channel => channel.Points[0].Value);
        const double margin = 1.0 / 127;
        var safeStart = lastBirth + maximumLife * (phaseStart + margin);
        var safeEnd = start + minimumLife * (phaseEnd - margin);
        var stage = (float)Math.Ceiling(safeStart);
        Assert.That(stage, Is.LessThan(safeEnd), "Choose the first whole second analytically inside the common original plateau; never search rendered frames for a passing time.");
        return new(phaseStart, phaseEnd, start, authoredEnd, floatStart, floatDuration, floatEnd, tick, lastBirth,
            minimumLife, maximumLife, margin, safeStart, safeEnd, stage, alphaText);
    }

    private static string OriginalParticleIdentities(TotalWarVfxSimulation simulation)
    {
        var particles = ((System.Collections.IEnumerable)typeof(TotalWarVfxSimulation)
            .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!).Cast<object>();
        return JsonSerializer.Serialize(particles.Select(particle =>
        {
            object Field(string name) => particle.GetType().GetField(name)!.GetValue(particle)!;
            return new { Birth = (double)Field("Birth"), Life = (float)Field("Life"),
                RandomAlpha = (float)Field("RandomAlpha"), ModelVariant = (int)Field("ModelVariant"),
                InitialAlpha = (float)Field("InitialAlpha"), InitialScale = Field("InitialScale").ToString(),
                RandomScale = Field("RandomScale").ToString(), RandomColour = Field("RandomColour").ToString(),
                Root = MatrixValues((Matrix)Field("Root")) };
        }).OrderBy(particle => particle.Birth).ThenBy(particle => particle.RandomAlpha));
    }

    private sealed record NoiseFieldRow(float Alpha, int NativeAlphaFloor, float Threshold, int NonnegativeTexels);
    private sealed record NoiseFieldStatistics(string Scope, string NoiseSha256, int Texels, int MinimumRed, int MaximumRed,
        int FullyCoveredParticles, NoiseFieldRow[] Particles);

    private static NoiseFieldStatistics Native176NoiseFieldStatistics(VfxConversion conversion, VfxPreviewParticle[] frame)
    {
        var material = conversion.MeshMaterials.Single();
        Assert.That(material.Parameters.Length, Is.EqualTo(60));
        Assert.That(material.Parameters[58], Is.EqualTo(1)); Assert.That(material.Parameters[59], Is.EqualTo(1));
        Assert.That(material.Parameters[43], Is.Zero, "The original manual dither term is disabled.");
        (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) Image(int slot)
            => VfxPreparedResources.DecodeTexture(conversion.Textures[material.TextureIndexes[slot]], VfxTextureUsage.MaterialValue);
        foreach (var slot in new[] { 1, 7 })
            Assert.That(Image(slot).Pixels.All(value => value == 255), Is.True, "The original dissolve and global masks are white.");
        var dither = Image(3);
        Assert.That(Enumerable.Range(0, dither.Width * dither.Height).All(index => dither.Pixels[index * 4] > 0), Is.True);
        var noise = Image(2);
        var red = Enumerable.Range(0, noise.Width * noise.Height).Select(index => noise.Pixels[index * 4] / 255f).Order().ToArray();
        var rows = frame.Select(particle =>
        {
            var alpha = particle.Render.Colour.W; var quantized = (int)MathF.Floor(alpha * 1000);
            var threshold = 1 - quantized * .001f;
            var low = 0; var high = red.Length;
            while (low < high) { var middle = (low + high) / 2; if (red[middle] < threshold) low = middle + 1; else high = middle; }
            return new NoiseFieldRow(alpha, quantized, threshold, red.Length - low);
        }).ToArray();
        return new("Original top-mip texel-centre UV-domain field statistics only; not a filtered raster or screen-pixel prediction. With original white masks, manual0 and positive dither.R, the native clip is noise.R >= 1-floor(alpha*1000)*.001.",
            Convert.ToHexStringLower(SHA256.HashData(conversion.Textures[material.TextureIndexes[2]])), red.Length,
            noise.Pixels.Where((_, index) => index % 4 == 0).Min(), noise.Pixels.Where((_, index) => index % 4 == 0).Max(),
            rows.Count(row => row.NonnegativeTexels == red.Length), rows);
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
            var outsideFade = (pixels[32 * 64 + 31] + pixels[32 * 64 + 32]) / 2;
            Assert.That(outsideFade.X, Is.EqualTo(colour.X).Within(.003),
                "The native depth bias moves soft view distance from 10 to 8, so the original 10.1 receiver must remain unsoftened.");
            // Authored factor 2, default strength 1 and clamped XY scale 5 give native bias 2.
            var nativeBias = (float)(Half)2;
            var receiverDistance = 10 - nativeBias + .05f * (nativeBias + .001f);
            var biasedContact = Vector4.Transform(new Vector4(0, 0, -receiverDistance, 1), parameters.Projection);
            device.SetRenderTarget(target);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, biasedContact.Z / biasedContact.W, 0);
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
