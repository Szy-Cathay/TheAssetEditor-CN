#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders;

[NonParallelizable]
public class FlagWindOfficialAssetTests
{
    private const string FlagModel = @"variantmeshes\_variantmodels\props\chinese\weighted\p_flag01_male01\h_square_flag_01.wsmodel";
    private const string SkeletonPath = @"animations\skeletons\p_flag01_male01.anim";
    private const string AnimationPath = @"animations\battle\character\male01\infantry\inf_flag\default\flag\flag_persistent_01.anim";
    private const int ImageSize = 512;

    [Test]
    public void CompleteOfficialFlag_PreservesOriginalAssetsAndRunsBothNativeLodMaterials()
    {
        var configured = Environment.GetEnvironmentVariable("AECN_FLAG_WIND_OFFICIAL_ROOT");
        if (string.IsNullOrWhiteSpace(configured))
            Assert.Ignore("Opt-in: set AECN_FLAG_WIND_OFFICIAL_ROOT to the verified flag-wind evidence directory.");

        var root = Path.GetFullPath(configured!);
        using var sourceReport = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "official-sample-provenance.json")));
        var originalRoot = Path.Combine(root, "official-originals");
        var sources = sourceReport.RootElement.GetProperty("resources").EnumerateArray().Select(row => new Source(
            row.GetProperty("path").GetString()!, row.GetProperty("sha256").GetString()!)).ToArray();
        var output = Path.Combine(root, "official-gpu-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        string? failure = null;

        try
        {
            var game = new WpfGameMock();
            var device = game.GraphicsDevice;
            var files = new PackFileService(null!) { EnforceGameFilesMustBeLoaded = false };
            var memory = files.CreateNewPackFileContainer("OfficialFlagReadonlyProbe", PackFileVersion.PFH5, PackFileCAType.MOD);
            var entries = sources.Select(source =>
            {
                var bytes = ReadVerified(source);
                return new NewPackFileEntry(Path.GetDirectoryName(source.Path)!,
                    PackFile.CreateFromBytes(Path.GetFileName(source.Path), bytes));
            }).ToList();
            files.AddFilesToPack(memory, entries);

            using var resources = new ProbeResources(files, game);
            using var lighting = new ViewportLightingResources(device);
            var environment = lighting.GetEnvironment(ViewportEnvironment.Studio);
            var shading = new ViewportShadingSettings
            {
                UseLocalLighting = true, Environment = ViewportEnvironment.Studio,
            };
            var materials = new CapabilityMaterialFactory(new ApplicationSettingsService(GameTypeEnum.ThreeKingdoms), resources);
            var contexts = new GeometryGraphicsContextFactory(Mock.Of<IDeviceResolver>(resolver => resolver.Device == device));
            var models = new Rmv2ModelNodeLoader(new MeshBuilderService(contexts), files, materials, resources.Dialogs);
            var loader = new ComplexMeshLoader(models, files);
            var player = new AnimationPlayer { IsEnabled = true, LoopAnimation = true };
            var skeleton = new GameSkeleton(AnimationFile.Create(files.FindFile(SkeletonPath)!), player);
            var animation = AnimationFile.Create(files.FindFile(AnimationPath)!);
            Assert.That(animation.Header.SkeletonName, Is.EqualTo("p_flag01_male01"));
            player.SetAnimation(new AnimationClip(animation, skeleton), skeleton);
            Assert.That(player.Duration.TotalSeconds, Is.GreaterThan(0));
            var loaded = loader.Load(files.FindFile(FlagModel)!, player, false, false);
            var model = SceneNodeHelper.GetChildrenOfType<Rmv2ModelNode>(loaded).Single();
            var stages = new[] { .4f, 1.3f };
            var winds = new[] { Vector2.Zero, new Vector2(.2f, 0), new Vector2(0, .2f) };

            try
            {
                foreach (var lod in new[] { 0, 2 })
                {
                    var mesh = model.GetMeshNodes(lod).Single();
                    var flag = mesh.Material.GetCapability<FlagWindCapability>();
                    Assert.Multiple(() =>
                    {
                        Assert.That(flag.Enabled, Is.True);
                        Assert.That(flag.WindInfluenceStrength, Is.EqualTo(.6f));
                        Assert.That(flag.VertexWindScale, Is.EqualTo(lod == 0 ? 1f : .1f));
                        Assert.That(mesh.Geometry.WeightCount, Is.EqualTo(lod == 0 ? 4 : 2));
                        Assert.That(mesh.Geometry.VertexArray.Any(vertex => vertex.VertexColour.A > 0), Is.True);
                    });
                    Assert.That(resources.LoadTexture(flag.ClothNormal1Path), Is.Not.Null);
                    Assert.That(resources.LoadTexture(flag.ClothNormal2Path), Is.Not.Null);

                    foreach (var yaw in new[] { 0f, 35f })
                    {
                        var world = Matrix.CreateRotationY(MathHelper.ToRadians(yaw));
                        var poses = stages.Select(stage => Pose(stage, mesh, world)).ToArray();
                        var bounds = BoundingBox.CreateFromPoints(poses.SelectMany((pose, stageIndex) =>
                            winds.SelectMany(wind => pose.WorldVertices.Select((position, index) => position
                                + NativeDisplacement(position, mesh.Geometry.VertexArray[index].VertexColour.ToVector4(),
                                    world, wind, stages[stageIndex], flag)))));
                        var center = (bounds.Min + bounds.Max) * .5f;
                        var radius = Math.Max(Vector3.Distance(bounds.Min, bounds.Max) * .5f, .1f);
                        // Choose the original cloth's front, rather than the editor's generic +Z view of its edge.
                        var viewDirection = ClothFront(mesh, poses);
                        var up = MathF.Abs(Vector3.Dot(viewDirection, Vector3.Up)) < .9f ? Vector3.Up : Vector3.UnitZ;
                        var eye = center + viewDirection * (radius * 2.8f + .2f);
                        var view = Matrix.CreateLookAt(eye, center, up);
                        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, radius * 20 + 20);

                        for (var stageIndex = 0; stageIndex < stages.Length; stageIndex++)
                        {
                            var stage = stages[stageIndex];
                            var pose = Pose(stage, mesh, world);
                            Frame? baseline = null;
                            for (var windIndex = 0; windIndex < winds.Length; windIndex++)
                            {
                                var wind = winds[windIndex];
                                var common = new CommonShaderParameters(view, projection, eye, center,
                                    MathHelper.ToRadians(shading.EnvironmentRotation), 0, 0, 1, Vector3.One,
                                    [Vector3.One, Vector3.One, Vector3.One], ImageSize, ImageSize, FactionColoursEnabled: false,
                                    ViewportShading: shading, ViewportDiffuse: environment.Diffuse, ViewportSpecular: environment.Specular,
                                    MaterialTimeSeconds: stage, WindVector: wind);
                                var frame = Render(mesh, world, common,
                                    $"lod{lod}-yaw{yaw:0}-stage{stageIndex}-wind{windIndex}.png");
                                baseline ??= frame;
                                var predictedDisplacement = pose.WorldVertices.Select((position, index) =>
                                    NativeDisplacement(position, mesh.Geometry.VertexArray[index].VertexColour.ToVector4(),
                                        world, wind, stage, flag)).Select(delta => delta.Length()).Max();
                                var depthDifference = frame.Depth.Zip(baseline.Depth,
                                    (a, b) => float.IsFinite(a.W) && float.IsFinite(b.W) ? Math.Abs(a.W - b.W) : 0).Max();
                                var changedContour = frame.Depth.Zip(baseline.Depth,
                                    (a, b) => (a.W > 0) != (b.W > 0)).Count(changed => changed);
                                var colourDifference = frame.Colour.Zip(baseline.Colour, (a, b) =>
                                    Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)).Sum();
                                var visibleDepths = frame.Depth.Where(pixel => pixel.W > 0 && float.IsFinite(pixel.W)).Select(pixel => pixel.W).ToArray();
                                var foreground = frame.Colour.Where((_, index) => frame.Depth[index].W > 0).ToArray();
                                var litForeground = foreground.Count(pixel => Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 12);
                                rows.Add(new
                                {
                                    Lod = lod, WorldYawDegrees = yaw, StageSeconds = stage,
                                    LocalAnimationSeconds = pose.LocalTime, WindX = wind.X, WindZ = wind.Y,
                                    LoopDurationSeconds = player.Duration.TotalSeconds, LoopAnimation = player.LoopAnimation,
                                    Skinning = "Shipping flag residual weights, unused-index fallback, normalized source NBT, homogeneous position W=1; AnimationSampler already includes inverse reference",
                                    FlagShaderVertexGain = flag.VertexWindScale, GeometryVertices = mesh.Geometry.VertexArray.Length,
                                    OriginalColours = mesh.Geometry.VertexArray.Select(vertex => vertex.VertexColour.PackedValue).Distinct().Count(),
                                    PredictedMaximumVertexDisplacement = predictedDisplacement,
                                    MaximumDepthDifferenceFromZeroWind = depthDifference, ChangedContourPixels = changedContour,
                                    ColourDifferenceFromZeroWind = colourDifference, NonFiniteDepthPixels = frame.NonFiniteDepthPixels,
                                    MinimumVisibleDepth = visibleDepths.Length == 0 ? 0 : visibleDepths.Min(),
                                    MaximumVisibleDepth = visibleDepths.Length == 0 ? 0 : visibleDepths.Max(),
                                    CameraPosition = new[] { eye.X, eye.Y, eye.Z }, CameraTarget = new[] { center.X, center.Y, center.Z },
                                    CameraFront = new[] { viewDirection.X, viewDirection.Y, viewDirection.Z },
                                    Lighting = "Unmodified standard editor Studio environment/EnvBRDFApprox; intensity 1; not native battle weather",
                                    ForegroundPixels = foreground.Length, LitForegroundPixels = litForeground,
                                    Image = frame.Path, SolidShapeImage = frame.SolidPath,
                                });
                                Assert.That(frame.NonFiniteDepthPixels, Is.Zero);
                                Assert.That(visibleDepths, Is.Not.Empty, "The full original flag must remain in the fixed comparison camera.");
                                Assert.That(litForeground, Is.GreaterThan(foreground.Length / 2),
                                    "The unchanged original cloth must be inspectable under the standard preview environment; black silhouettes are not a valid fixture.");
                                if (predictedDisplacement > .00001f)
                                    Assert.That(depthDifference, Is.GreaterThan(.00001f),
                                        "The official weighted geometry must actually deform when the native cross-product predicts motion.");
                                else
                                {
                                    Assert.That(depthDifference, Is.LessThan(.00001f),
                                        "Parallel/zero wind must not rotate or translate the entire official flag as a substitute for vertex wind.");
                                    if (wind != Vector2.Zero)
                                        Assert.That(colourDifference, Is.GreaterThan(0),
                                            "With parallel wind and unchanged geometry, the original cloth-normal textures must still affect surface shading.");
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (var mesh in model.GetLodNodes().SelectMany(lod => SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(lod)))
                    mesh.Geometry.Dispose();
            }

            foreach (var source in sources)
                _ = ReadVerified(source);

            PoseData Pose(float time, Rmv2MeshNode mesh, Matrix world)
            {
                var local = time % (float)player.Duration.TotalSeconds;
                player.SeekToTimeSeconds(local);
                var frame = player.GetCurrentAnimationFrame();
                var transforms = Enumerable.Repeat(Matrix.Identity, 256).ToArray();
                for (var index = 0; index < Math.Min(256, frame.BoneTransforms.Count); index++)
                    transforms[index] = frame.BoneTransforms[index].WorldTransform;
                var capability = mesh.Material.GetCapability<AnimationCapability>();
                capability.ApplyAnimation = true;
                capability.AnimationWeightCount = mesh.Geometry.WeightCount;
                capability.AnimationTransforms = transforms;
                var positions = mesh.Geometry.VertexArray.Select(vertex =>
                {
                    var position = Vector3.Zero;
                    var (weights, indices) = NativeSkinningInputs(vertex, mesh.Geometry.WeightCount);
                    for (var bone = 0; bone < mesh.Geometry.WeightCount; bone++)
                        position += Vector3.Transform(vertex.Position3(), transforms[indices[bone]]) * weights[bone];
                    return Vector3.Transform(position, world);
                }).ToArray();
                var normals = mesh.Geometry.VertexArray.Select(vertex =>
                {
                    var normal = Vector3.Zero;
                    var (weights, indices) = NativeSkinningInputs(vertex, mesh.Geometry.WeightCount);
                    for (var bone = 0; bone < mesh.Geometry.WeightCount; bone++)
                        normal += Vector3.TransformNormal(Vector3.Normalize(vertex.Normal), transforms[indices[bone]]) * weights[bone];
                    return Vector3.TransformNormal(normal, world);
                }).ToArray();
                // AnimationSampler already returns inverse-reference * animated-world matrices, as used by Rmv2MeshNode.
                return new PoseData(local, positions, normals);
            }

            Frame Render(Rmv2MeshNode mesh, Matrix world, CommonShaderParameters common, string name)
            {
                using var colourTarget = new RenderTarget2D(device, ImageSize, ImageSize, false, SurfaceFormat.Color, DepthFormat.Depth24);
                using var depthTarget = new RenderTarget2D(device, ImageSize, ImageSize, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                using var solidTarget = new RenderTarget2D(device, ImageSize, ImageSize, false, SurfaceFormat.Color, DepthFormat.Depth24);
                var previousTargets = device.GetRenderTargets();
                var previousViewport = device.Viewport;
                var previousBlend = device.BlendState;
                var previousDepth = device.DepthStencilState;
                var previousRaster = device.RasterizerState;
                var item = new GeometryRenderItem(mesh.Geometry, mesh.Material, world);
                try
                {
                    device.BlendState = BlendState.Opaque;
                    device.DepthStencilState = DepthStencilState.Default;
                    device.RasterizerState = RasterizerState.CullNone;
                    device.SetRenderTarget(colourTarget);
                    device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(36, 39, 44), 1, 0);
                    item.Draw(device, common, RenderingTechnique.Normal);
                    device.SetRenderTarget(depthTarget);
                    device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
                    item.Draw(device, common, RenderingTechnique.ViewportGeometry);
                    device.SetRenderTarget(solidTarget);
                    device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(36, 39, 44), 1, 0);
                    item.Draw(device, common, RenderingTechnique.Solid);
                }
                finally
                {
                    device.SetRenderTargets(previousTargets);
                    device.Viewport = previousViewport;
                    device.BlendState = previousBlend;
                    device.DepthStencilState = previousDepth;
                    device.RasterizerState = previousRaster;
                }
                var colours = new Color[ImageSize * ImageSize];
                colourTarget.GetData(colours);
                var depths = new Vector4[ImageSize * ImageSize];
                depthTarget.GetData(depths);
                var path = Path.Combine(output, name);
                using (var stream = File.Open(path, FileMode.CreateNew, FileAccess.Write))
                    colourTarget.SaveAsPng(stream, ImageSize, ImageSize);
                var solidPath = Path.Combine(output, Path.GetFileNameWithoutExtension(name) + "-solid.png");
                using (var stream = File.Open(solidPath, FileMode.CreateNew, FileAccess.Write))
                    solidTarget.SaveAsPng(stream, ImageSize, ImageSize);
                return new Frame(path, solidPath, colours, depths,
                    depths.Count(pixel => !float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y)
                        || !float.IsFinite(pixel.Z) || !float.IsFinite(pixel.W)));
            }
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
            throw;
        }
        finally
        {
            var report = new
            {
                Scope = "Complete unchanged official flag model/material/texture/loop-animation diagnostics with explicit preview wind. This is not native game/Terry visual acceptance or CPU wind-bone verification.",
                Fixture = "Fixed front view derived from the two original skinned cloth stages, standard editor Studio environment/EnvBRDFApprox at intensity 1, original DDS unchanged. Companion Solid images inspect geometry only.",
                SourceProof = Path.Combine(root, "official-sample-provenance.json"),
                SourceFiles = sources.Length, OriginalResourcesReadOnly = true,
                Passed = failure == null, Failure = failure, Frames = rows,
            };
            var reportPath = Path.Combine(output, "report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.Out.WriteLine(reportPath);
        }

        byte[] ReadVerified(Source source)
        {
            var path = Path.GetFullPath(Path.Combine(originalRoot, source.Path));
            Assert.That(path.StartsWith(originalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            var bytes = File.ReadAllBytes(path);
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(source.Sha256), source.Path);
            return bytes;
        }
    }

    private static Vector3 NativeDisplacement(Vector3 position, Vector4 colour, Matrix world, Vector2 wind, float time, FlagWindCapability flag)
    {
        if (wind == Vector2.Zero)
            return Vector3.Zero;
        var q = MathF.Sqrt(Math.Clamp(MathF.Abs(wind.X + .001f) + MathF.Abs(wind.Y), 0, .26f));
        var phase = -6 * q * (time - .78f * colour.Z);
        var a = .5f * (2 + MathF.Sin(.5f * (world.M41 + phase)));
        var b = .5f * (2 + MathF.Sin(position.Y + phase - .2f));
        var c = .5f + MathF.Sin(3 * (world.M43 + 2 * colour.Y + phase));
        var direction = Vector2.Normalize(wind);
        return Vector3.Cross(new(direction.X, 0, direction.Y), new(world.M11, world.M12, world.M13))
            * (flag.VertexWindScale * flag.WindInfluenceStrength * .48f * q * a * b * c * MathF.Pow(colour.W, 1.1f));
    }

    private sealed record Source(string Path, string Sha256);
    private static Vector3 ClothFront(Rmv2MeshNode mesh, PoseData[] poses)
    {
        var faces = new List<(Vector3 Area, Vector3 Normal)>();
        foreach (var pose in poses)
        for (var index = 0; index < mesh.Geometry.IndexArray.Length; index += 3)
        {
            var a = mesh.Geometry.IndexArray[index];
            var b = mesh.Geometry.IndexArray[index + 1];
            var c = mesh.Geometry.IndexArray[index + 2];
            if (mesh.Geometry.VertexArray[a].VertexColour.A == 0
                && mesh.Geometry.VertexArray[b].VertexColour.A == 0
                && mesh.Geometry.VertexArray[c].VertexColour.A == 0)
                continue;
            var area = Vector3.Cross(pose.WorldVertices[b] - pose.WorldVertices[a], pose.WorldVertices[c] - pose.WorldVertices[a]);
            if (area.LengthSquared() > .00000001f)
                faces.Add((area, pose.WorldNormals[a] + pose.WorldNormals[b] + pose.WorldNormals[c]));
        }
        Assert.That(faces, Is.Not.Empty, "Original weighted cloth triangles must not collapse in the sampled animation.");
        var largest = faces.MaxBy(face => face.Area.LengthSquared());
        var reference = Vector3.Dot(largest.Area, largest.Normal) < 0 ? -largest.Area : largest.Area;
        var front = Vector3.Normalize(faces.Aggregate(Vector3.Zero, (sum, face) =>
            sum + (Vector3.Dot(face.Area, reference) < 0 ? -face.Area : face.Area)));
        Assert.That(float.IsFinite(front.X) && float.IsFinite(front.Y) && float.IsFinite(front.Z), Is.True);
        return front;
    }

    private static (float[] Weights, int[] Indices) NativeSkinningInputs(VertexPositionNormalTextureCustom vertex, int count)
    {
        var weights = vertex.GetBoneWeights();
        var indices = vertex.GetBoneIndexs();
        if (count == 2)
            weights[1] = 1 - weights[0];
        else
        {
            var originalSum = weights[0] + weights[1] + weights[2];
            if (weights[1] <= 1f / 256f)
                indices[1] = indices[2] = indices[3] = indices[0];
            weights[1] = weights[1] > 1f / 256f ? weights[1] : 0;
            weights[2] = weights[2] > 1f / 256f ? weights[2] : 0;
            weights[3] = originalSum < 255f / 256f ? 1 - originalSum : 0;
        }
        return (weights, indices);
    }

    private sealed record PoseData(float LocalTime, Vector3[] WorldVertices, Vector3[] WorldNormals);
    private sealed record Frame(string Path, string SolidPath, Color[] Colour, Vector4[] Depth, int NonFiniteDepthPixels);

    private sealed class ProbeResources : IScopedResourceLibrary, IDisposable
    {
        private readonly ResourceLibrary _resources;
        private readonly ScopedResourceLibrary _scoped;
        private readonly Dictionary<ShaderTypes, Effect> _effects = [];
        public IStandardDialogs Dialogs { get; } = new Mock<IStandardDialogs>(MockBehavior.Strict).Object;

        public ProbeResources(IPackFileService files, WpfGameMock game)
        {
            _resources = new ResourceLibrary(files);
            _resources.Initialize(game.GraphicsDevice, game.Content);
            _scoped = new ScopedResourceLibrary(_resources, Mock.Of<IEventHub>(), Dialogs);
        }

        public Effect GetStaticEffect(ShaderTypes type)
        {
            if (!_effects.TryGetValue(type, out var effect))
            {
                _effects.Add(type, effect = _resources.GetStaticEffect(type).Clone());
            }
            return effect;
        }

        public Texture2D? LoadTexture(string fileName, bool forceRefreshTexture = false, bool fromFile = false) =>
            _scoped.LoadTexture(fileName, forceRefreshTexture, fromFile)
                ?? throw new FileNotFoundException("Official flag dependency did not load.", fileName);

        public Texture2D? ForceLoadImage(string imagePath, out ImageInformation imageInformation) =>
            _scoped.ForceLoadImage(imagePath, out imageInformation);

        public void Dispose()
        {
            foreach (var effect in _effects.Values)
                effect.Dispose();
            _scoped.Dispose();
        }
    }
}
