#nullable enable
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxOfficialSharedEventPixelTests
{
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private static readonly MethodInfo ReadOfficial = typeof(VfxOfficialDistortionPixelTests)
        .GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [OneTimeSetUp]
    public void RequireManifestOwnedOriginals() => _official.LoadCurrentOfficialResourcesOnly();

    private static IEnumerable<TestCaseData> Originals()
    {
        foreach (var path in new[]
        {
            @"vfx\wh3_dlc26_entity_lord_skulltaker_slash.xml",
            @"vfx\wh3_dlc26_entity_lord_skulltaker_stab.xml",
            @"vfx\wh3_dlc26_entity_lord_skulltaker_punch.xml",
            @"vfx\wh3_dlc27_lord_masque_attack_stab.xml",
            @"vfx\wh2_dlc12_deliverance_of_itza_level1.xml",
        })
            foreach (var quality in new[] { "0", "1", "2" })
                yield return new TestCaseData(path, quality);
    }

    private static readonly Dictionary<string, OfficialRoot> AffectedRoots = ReadAffectedRoots();
    private sealed record OfficialRoot(string Hash, Dictionary<string,string[]> SourceFailures, bool LateChildStages);

    private static Dictionary<string, OfficialRoot> ReadAffectedRoots()
    {
        using var stream = typeof(VfxOfficialSharedEventPixelTests).Assembly.GetManifestResourceStream("Test.GameWorld.Core.Vfx.Fixtures.OfficialAffectedVfxRoots.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("roots").EnumerateArray().ToDictionary(row => row.GetProperty("path").GetString()!,
            row => new OfficialRoot(row.GetProperty("sha256").GetString()!,
                row.GetProperty("expectedSourceFailures").EnumerateObject().ToDictionary(item => item.Name,
                    item => item.Value.EnumerateArray().Select(value => value.GetString()!).ToArray()),
                row.GetProperty("lateChildStages").GetBoolean()), StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<TestCaseData> AffectedOriginals()
    {
        var controls = Originals().Select(test => (string)test.Arguments[0]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in AffectedRoots.Keys.Where(path => !controls.Contains(path)))
            foreach (var quality in new[] { "0", "1", "2" }) yield return new TestCaseData(path, quality);
    }

    [TestCaseSource(nameof(AffectedOriginals))]
    public void CompleteAffectedRoots_RecordOriginalGpuReplayAndExactSourceFailures(string path, string quality)
        => CompleteOriginalEvents_ReplayTheSameGpuPixelsAtEveryQuality(path, quality);

    [TestCaseSource(nameof(Originals))]
    public void CompleteOriginalEvents_ReplayTheSameGpuPixelsAtEveryQuality(string path, string quality)
    {
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        byte[]? Read(string key)
        {
            var bytes = (byte[]?)ReadOfficial.Invoke(_official, [key]);
            if (bytes == null) missing.Add(key);
            else sources[key] = new { Path = key, Bytes = bytes.Length, Sha256 = Hash(bytes) };
            return bytes;
        }
        var original = Read(path) ?? throw new FileNotFoundException(path);
        var originalHash = Hash(original);
        var expected = AffectedRoots[path];
        Assert.That(originalHash, Is.EqualTo(expected.Hash), "Use the complete manifest-backed original, not a modified validation asset.");
        var expectedSourceFailures = expected.SourceFailures[quality];
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(original, path, Read, quality));
            Assert.That(preview.EmitterCount, Is.GreaterThan(0), JsonSerializer.Serialize(preview.Diagnostics));
            var bounds = preview.PreviewBounds;
            var attachment = Matrix.CreateRotationY(.35f) * Matrix.CreateTranslation(1, 0, -1);
            var focus = bounds.HasValue ? Vector3.Transform((bounds.Value.Min + bounds.Value.Max) * .5f, attachment) : new Vector3(0, 5, 0);
            var width = bounds.HasValue ? Math.Clamp((bounds.Value.Max - bounds.Value.Min).Length() * 1.2f, 4, 80) : 36;
            var eye = focus + Vector3.Normalize(new Vector3(16, 16, 28)) * 8;
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, focus, Vector3.Up),
                Matrix.CreateOrthographic(width, width * .75f, .1f, 300), eye, focus, 0, 0, 0, 1, Vector3.One, [],
                VfxGroundHeight: 0, VfxWriteGroundDepth: true);
            var inspectionCamera = camera;
            var initialWidth = width;
            var conversion = (VfxConversion)typeof(EffekseerVfxPreview)
                .GetField("_conversion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            var simulation = (TotalWarVfxSimulation)typeof(EffekseerVfxPreview)
                .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            var modelCorners = new Dictionary<(int Model, int Frame), Vector3[]>();
            object? cameraSelection = null;
            var stages = new List<object>();
            var visible = 0;
            Color[]? checkpoint = null;
            var sampleTimes = new[] { 0f, .05f, .1f, .2f, .4f, .6f, 1.2f, 2.5f };
            if (expected.LateChildStages) sampleTimes = [.. sampleTimes, 5.1f, 5.5f, 6.1f];
            foreach (var time in sampleTimes)
            {
                var pixels = Render(time, time.ToString("F3", CultureInfo.InvariantCulture));
                if (time == .4f) checkpoint = pixels;
                var changed = pixels.Count(pixel => Difference(pixel, Color.DarkSlateGray) > 3);
                visible += changed;
                var projected = (List<VfxPreviewParticle>)typeof(EffekseerVfxPreview)
                    .GetField("_projectedParticles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                var submitted = (List<EffekseerParticle>)typeof(EffekseerVfxPreview)
                    .GetField("_renderedParticles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                stages.Add(new { Time = time, preview.ParticleCount, ChangedPixels = changed,
                    CameraFocus = focus.ToString(), CameraPosition = eye.ToString(), OrthographicWidth = width,
                    CameraSelection = cameraSelection,
                    ProjectedParticles = projected.Count, SubmittedParticles = submitted.Count,
                    MaximumAlpha = projected.Select(particle => particle.Render.Colour.W).DefaultIfEmpty().Max(),
                    SampleParticles = projected.Take(3).Select(particle => new { particle.Render.Layer,
                        Position = particle.Render.Transform.Translation.ToString(), Size = particle.Render.Transform.Up.Length(),
                        Colour = particle.Render.Colour.ToString() }).ToArray(),
                    Diagnostics = preview.Diagnostics.Select(item => new { item.Layer, item.Field, item.ReasonKey }).ToArray() });
                Assert.That(preview.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            }
            var replay = Render(.4f, "rewind-0.400");
            var maximum = replay.Zip(checkpoint!).Max(pair => Difference(pair.First, pair.Second));
            WriteEvidence("json", JsonSerializer.Serialize(new
            {
                Path = path, Quality = quality, RootSha256 = originalHash,
                RuntimeSha256 = Hash(File.ReadAllBytes(typeof(EffekseerVfxPreview).Assembly.Location)),
                CameraFocus = focus.ToString(), OrthographicWidth = width, PreviewBounds = bounds?.ToString(),
                ManifestSha256 = typeof(VfxOfficialDistortionPixelTests).GetField("_manifestHash", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_official),
                Stages = stages, RewindMaximumChannelDifference = maximum,
                ResourceClosureComplete = missing.Count == 0, ExpectedOfficialSourceFailures = expectedSourceFailures,
                Sources = sources.Keys.Select(key => ((Dictionary<string, object>)typeof(VfxOfficialDistortionPixelTests)
                    .GetField("_origins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_official)!)[key.Replace('/', '\\').ToLowerInvariant()]).ToArray(), Missing = missing,
                Scope = "Complete unmodified manifest-owned original VFX and dependencies, real normal Debug GPU upload/render/replay. Shared event CPU/native-contract tests independently constrain position and rotation. Exact known official source failures are reported separately from complete resource closure. This is not game/Terry visual equivalence."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Assert.Multiple(() =>
            {
                Assert.That(maximum, Is.LessThanOrEqualTo(3), "Backward seek must reproduce complete original GPU RGBA, not just particle counts.");
                Assert.That(visible, Is.GreaterThan(20), "At least one original stage must actually contribute GPU pixels.");
                Assert.That(missing, Is.EquivalentTo(expectedSourceFailures),
                    "Unexpected missing dependencies fail. Exact known absent official paths remain visible diagnostics, never substitute textures/models or claim complete closure.");
                Assert.That(Hash(original), Is.EqualTo(originalHash));
            });

            Color[] Render(float time, string label)
            {
                preview.Seek(time, attachment);
                camera = inspectionCamera; focus = camera.CameraLookAt; eye = camera.CameraPosition; width = initialWidth;
                var current = new List<VfxPreviewParticle>();
                simulation.FillProjectedFrame(inspectionCamera, current, applyCameraCulling: false);
                var attempts = new List<object>();
                cameraSelection = new { Selected = false, Attempts = attempts };
                // Bound the camera search by actual live definitions, without editing or selecting source layers.
                var candidates = current.Where(item => item.Render.Colour.W > .00001f
                        || conversion.Emitters[item.Render.Layer].RenderParts(item.ModelVariant).Any(part => part.OpaqueModel != 0))
                    .GroupBy(item => item.Render.Layer).SelectMany(group => group.OrderByDescending(item => item.Render.Colour.W)
                        .ThenBy(item => item.Render.Transform.Translation.X).ThenBy(item => item.Render.Transform.Translation.Y)
                        .ThenBy(item => item.Render.Transform.Translation.Z).Take(2))
                    .OrderByDescending(item => HasColour(item)).ThenByDescending(item => item.Render.Colour.W)
                    .ThenBy(item => item.Render.Layer).ThenBy(item => item.Render.Transform.Translation.X)
                    .ThenBy(item => item.Render.Transform.Translation.Y).ThenBy(item => item.Render.Transform.Translation.Z).Take(16);
                foreach (var candidate in candidates)
                {
                    var world = Geometry(candidate);
                    if (world is not { } actual || actual.Max.Y < 0) continue;
                    actual.Min.Y = Math.Max(0, actual.Min.Y);
                    var points = actual.GetCorners();
                    var origin = candidate.Render.Transform.Translation;
                    var radius = points.Max(point => Vector3.Distance(origin, point));
                    var preferred = Math.Max(8, radius * 1.2f + .5f);
                    var definition = conversion.Emitters[candidate.Render.Layer];
                    var distances = new List<float>();
                    if (definition.ParticleCullDistances is { } authored)
                    {
                        // Sprite distances also include inherited emitter scale. Confirm guesses through the real projection.
                        foreach (var scale in new[] { 1f, .5f, 2f, .25f, 4f, 8f })
                        {
                            var low = Math.Max(.1f, authored.Y * scale);
                            var high = Math.Max(low, authored.Z * scale);
                            distances.Add(Math.Clamp(preferred, low, high));
                        }
                    }
                    else distances.Add(preferred);
                    var selected = false;
                    foreach (var distance in distances.Distinct())
                    {
                        var proposedFocus = (actual.Min + actual.Max) * .5f;
                        var proposedEye = origin + Vector3.Normalize(new Vector3(16,16,28)) * distance;
                        var proposed = Fit(proposedEye, proposedFocus, points);
                        var projected = new List<VfxPreviewParticle>();
                        simulation.FillProjectedFrame(proposed.Camera, projected);
                        var retained = projected.Where(item => item.Render.Layer == candidate.Render.Layer
                                && item.ModelVariant == candidate.ModelVariant
                                && Vector3.DistanceSquared(item.Render.Transform.Translation, origin) < .00000001f)
                            .Select(item => (VfxPreviewParticle?)item).FirstOrDefault();
                        var survives = retained is { } visibleCandidate && (visibleCandidate.Render.Colour.W > .00001f
                            || definition.RenderParts(candidate.ModelVariant).Any(part => part.OpaqueModel != 0));
                        attempts.Add(new { Layer = candidate.Render.Layer, definition.Name, candidate.ModelVariant,
                            ParticleOrigin = origin.ToString(), WorldGeometry = actual.ToString(), CameraDistance = distance,
                            AuthoredCull = definition.ParticleCullDistances?.ToString(), FormalProjectedCount = projected.Count,
                            CandidateSurvives = survives, RetainedAlpha = retained?.Render.Colour.W });
                        if (!survives) continue;
                        // Camera-facing sprite geometry must be measured again under the final viewing direction.
                        if (Geometry(retained!.Value) is { } finalGeometry && finalGeometry.Max.Y >= 0)
                        {
                            finalGeometry.Min.Y = Math.Max(0, finalGeometry.Min.Y);
                            proposed = Fit(proposedEye, (finalGeometry.Min + finalGeometry.Max) * .5f, finalGeometry.GetCorners());
                        }
                        camera = proposed.Camera; width = proposed.Width;
                        focus = camera.CameraLookAt; eye = camera.CameraPosition;
                        cameraSelection = new { Selected = true, Layer = candidate.Render.Layer, definition.Name,
                            candidate.ModelVariant, LocalModelFramesCached = modelCorners.Count, Attempts = attempts };
                        selected = true; break;
                    }
                    if (selected) break;
                }
                using var target = new RenderTarget2D(graphics.GraphicsDevice, 512, 384, false, SurfaceFormat.Color, DepthFormat.Depth24);
                graphics.GraphicsDevice.SetRenderTarget(target);
                graphics.GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkSlateGray, 1, 0);
                preview.Draw(camera);
                graphics.GraphicsDevice.SetRenderTarget(null);
                var pixels = new Color[512 * 384]; target.GetData(pixels);
                var folder = Output();
                if (folder != null)
                {
                    using var stream = new FileStream(Path.Combine(folder, Label() + "-" + label + ".png"), FileMode.CreateNew);
                    target.SaveAsPng(stream, 512, 384);
                }
                return pixels;
            }

            bool HasColour(VfxPreviewParticle particle) => conversion.Emitters[particle.Render.Layer].RenderParts(particle.ModelVariant)
                .Any(part => part.Distortion == 0 && !(part.MeshMaterialIndex >= 0
                    && conversion.MeshMaterials[part.MeshMaterialIndex].Parameters.Length == VfxMeshDistortionMaterial.ParameterCount));

            BoundingBox? Geometry(VfxPreviewParticle projected)
            {
                var particle = projected.Render;
                var emitter = conversion.Emitters[particle.Layer];
                BoundingBox? result = null;
                foreach (var part in emitter.RenderParts(projected.ModelVariant))
                {
                    Vector3[] corners;
                    if (part.Kind != 0 && part.ModelIndex >= 0)
                    {
                        var model = conversion.Models[part.ModelIndex];
                        var frame = (int)MathF.Floor(Math.Max(0, particle.AppearancePhase) * model.AnimationCycles * model.AnimationFrameCount)
                            % model.AnimationFrameCount;
                        if (!modelCorners.TryGetValue((part.ModelIndex, frame), out corners!))
                        {
                            var local = model.AnimationData?.Bounds(model.Vertices, frame)
                                ?? BoundingBox.CreateFromPoints((model.AnimationVertices ?? model.Vertices)
                                    .Skip(model.AnimationVertices == null ? 0 : frame * model.Vertices.Length)
                                    .Take(model.Vertices.Length).Select(vertex => vertex.Position));
                            corners = local.GetCorners(); modelCorners.Add((part.ModelIndex, frame), corners);
                        }
                    }
                    else
                    {
                        var pivot = part.Pivot;
                        corners = [new(-pivot.X,-pivot.Y,0),new(1-pivot.X,-pivot.Y,0),
                            new(-pivot.X,1-pivot.Y,0),new(1-pivot.X,1-pivot.Y,0)];
                    }
                    var world = BoundingBox.CreateFromPoints(corners.Select(point => Vector3.Transform(point, particle.Transform)));
                    result = result is { } previous ? BoundingBox.CreateMerged(previous, world) : world;
                }
                return result;
            }

            (CommonShaderParameters Camera, float Width) Fit(Vector3 position, Vector3 target, Vector3[] corners)
            {
                if (Vector3.DistanceSquared(position, target) < .00000001f)
                    target = position - Vector3.Normalize(new Vector3(16,16,28));
                var view = Matrix.CreateLookAt(position, target, Vector3.Up);
                var local = BoundingBox.CreateFromPoints(corners.Select(point => Vector3.Transform(point, view)));
                var fitWidth = Math.Max(.05f, Math.Max(local.Max.X - local.Min.X, (local.Max.Y - local.Min.Y) / .75f) * 1.2f);
                var far = Math.Max(300, -local.Min.Z * 1.2f + 1);
                return (inspectionCamera with { View = view, Projection = Matrix.CreateOrthographic(fitWidth, fitWidth*.75f, .1f, far),
                    CameraPosition = position, CameraLookAt = target }, fitWidth);
            }
        }
        finally { graphics.Release(); }

        string Label() => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) + "-quality" + quality;
        void WriteEvidence(string extension, string text)
        {
            var folder = Output();
            if (folder != null) File.WriteAllText(Path.Combine(folder, Label() + "." + extension), text);
            TestContext.Progress.WriteLine(text);
        }
    }

    private static string? Output()
    {
        var folder = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(folder)) return null;
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static int Difference(Color a, Color b) => new[] { Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B), Math.Abs(a.A - b.A) }.Max();
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
