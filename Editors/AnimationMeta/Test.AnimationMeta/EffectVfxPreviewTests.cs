using System.IO;
using System.Text;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public partial class EffectVfxPreviewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MultipleEffects_KeepSeparateRuntimeIdentityAndIncrementalReplacementAcrossBothOwners(bool sameResource)
    {
        var files = new Mock<IPackFileService>();
        var resources = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase)
        {
            [@"vfx\test.xml"] = PackFile.CreateFromBytes("test.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>")),
            [@"vfx\other.xml"] = PackFile.CreateFromBytes("other.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>")),
        };
        files.Setup(value => value.FindFile(It.IsAny<string>(), null)).Returns((string path, PackFileContainer? _) => resources.GetValueOrDefault(path.Replace('/', '\\')));
        files.Setup(value => value.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()))
            .Returns((PackFile file, PackFileContainer? _) => resources.Single(entry => ReferenceEquals(entry.Value, file)).Key);
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        var first = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = "test", EndTime = 3, Position = new(-3, 0, 0) };
        var second = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = sameResource ? "test" : "other", StartTime = .5f, EndTime = 3, Position = new(4, 0, 0) };
        var third = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = "test", StartTime = .25f, EndTime = 3, Position = new(0, 2, 0) };
        var sources = new[] { first, second, third };
        var factory = new CapturingFactory(); var live = new VfxPreviewDocumentStore();
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory, Mock.Of<IDeviceResolver>(), live);
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(new ParsedMetadataFile { Version = 10, Attributes = [first] },
            new ParsedMetadataFile { Version = 10, Attributes = [second, third] }, null, root, null!, new AnimationPlayer(), null);
        var instances = result.Instances.Cast<EffectMetaInstance>().ToArray();
        try
        {
            Assert.That(instances, Has.Length.EqualTo(3));
            var renderer = (RenderEngineComponent)RuntimeHelpers.GetUninitializedObject(typeof(RenderEngineComponent));
            var queue = new Dictionary<RenderBuckedId, List<IRenderItem>> { [RenderBuckedId.Normal] = [] };
            typeof(RenderEngineComponent).GetField("_renderItems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(renderer, queue);
            for (var index = 0; index < instances.Length; index++)
            {
                Assert.That(instances[index].Source, Is.SameAs(sources[index]));
                instances[index].Update(1.2f);
                var marker = root.Children[index];
                ((VfxPreviewSceneNode)marker.Children.Single()).Render(renderer, marker.ModelMatrix);
            }
            Assert.That(queue[RenderBuckedId.Normal], Has.Count.EqualTo(3));
            Assert.That(queue[RenderBuckedId.Normal].Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(3));
            foreach (var item in queue[RenderBuckedId.Normal])
                item.Draw(null!, new(Matrix.Identity, Matrix.Identity, Vector3.Zero, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []), RenderingTechnique.Normal);
            Assert.That(factory.Created, Has.Count.EqualTo(3));
            for (var index = 0; index < instances.Length; index++)
            {
                Assert.That(factory.Created[index].Time, Is.EqualTo(1.2f - new[] { 0, .5f, .25f }[index]).Within(.00001f));
                Assert.That(factory.Created[index].Attachment.Translation, Is.EqualTo(sources[index].Position));
                Assert.That(factory.Created[index].Draws, Is.EqualTo(1), "Every queued Effect must reach its own preview draw call.");
            }
            var untouched = root.Children.Skip(1).ToArray();
            first.Position = new(9, 1, 0);
            var replacement = builder.BuildPreview(first, MetaDataDocumentOwner.Persistent, true, root, null!, new AnimationPlayer());
            Assert.That(replacement.IsSupported, Is.True);
            Assert.That(replacement.Instance, Is.TypeOf<EffectMetaInstance>());
            instances[0].CleanUp(); instances[0] = (EffectMetaInstance)replacement.Instance!;
            instances[0].Update(1.2f);
            var replacementMarker = root.Children.Single(marker => !untouched.Contains(marker));
            Assert.That(((VfxPreviewSceneNode)replacementMarker.Children.Single()).PrepareFrame(replacementMarker.ModelMatrix), Is.True);
            Assert.That(root.Children, Has.Count.EqualTo(3));
            Assert.That(root.Children, Does.Contain(untouched[0]).And.Contain(untouched[1]));
            Assert.That(factory.Created[0].Disposed, Is.True);
            Assert.That(factory.Created[1].Disposed, Is.False); Assert.That(factory.Created[2].Disposed, Is.False);
            Assert.That(factory.Created[1].Reloads, Is.EqualTo(1)); Assert.That(factory.Created[2].Reloads, Is.EqualTo(1));
            Assert.That(factory.Created[3].Attachment.Translation, Is.EqualTo(first.Position));
        }
        finally { foreach (var instance in instances) instance.CleanUp(); }
        Assert.That(root.Children, Is.Empty);
        Assert.That(factory.Created.All(preview => preview.Disposed), Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MultipleOfficialEffects_ContributeInTheSameFrameInEitherDrawOrder(int scenario)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 for official multi-instance offscreen rendering.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var packNames = new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" };
        var packs = packNames.Select(name => loader.Load(Path.Combine(data!, name))!).ToArray();
        var names = scenario == 2 ? new[] { "wh3_main_entity_storm_dragon_attack_impact", "wh3_main_entity_storm_mage_lightning_charge" }
            : new[] { "wh_dlc08_lib_pinning_icicles", scenario == 1 ? "wh_main_cinematic_smoke_blast" : "wh_dlc08_lib_pinning_icicles" };
        var paths = names.Select(name => @"vfx\" + name + ".xml").ToArray();
        var originalFiles = paths.Select(path => packs[0].FileList[path]).ToArray();
        var originals = originalFiles.Select(file => file.DataSource.ReadData()).ToArray();
        var sceneTime = scenario == 2 ? 1.35f : .2f;
        IEffectMeta first = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = names[0], EndTime = 2, Position = new(-20, 0, 0) };
        IEffectMeta second = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = names[1], EndTime = 2, Position = new(20, 0, 0) };
        PackFile? metaFile = null;
        byte[]? metadataBytes = null;
        object? metadataEvidence = null;
        if (scenario == 2)
        {
            const string metadataPath = @"animations\battle\humanoid01e\storm_dragon\attacks\hu1e_storm_dragon_attack_01.anm.meta";
            var metadataPack = loader.Load(Path.Combine(data!, "anim2.pack"))!;
            metaFile = metadataPack.FileList[metadataPath];
            metadataBytes = metaFile.DataSource.ReadData();
            var parsed = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(metadataBytes);
            var authored = names.Select(name => parsed.Attributes.OfType<Effect_v12>().Single(effect => effect.VfxName == name)).ToArray();
            metadataEvidence = new
            {
                Pack = Path.GetFullPath(Path.Combine(data!, "anim2.pack")), Path = metadataPath,
                Sha256 = Convert.ToHexString(SHA256.HashData(metadataBytes)),
                Effects = authored.Select(effect => new
                {
                    effect.VfxName, effect.StartTime, effect.EndTime, effect.Scale, effect.Tracking, effect.NodeIndex,
                    Position = effect.Position.ToString(), Orientation = effect.Orientation.ToString(),
                    effect.EffectType, effect.TerrainMutable, effect.DistanceCulled, effect.ScalesWithParent,
                }).ToArray(),
                DiagnosticAdjustments = new[]
                {
                    "Only the two named EFFECT entries are selected from the complete official META.",
                    "Positions are offset by -20/+20 world X solely to separate the two screen regions.",
                    "No skeleton pose is supplied: bone 34 tracking and distance-emission layers are not validated by this static composition check.",
                },
            };
            first = authored[0]; second = authored[1];
            first.Position += new Vector3(-20, 0, 0); second.Position += new Vector3(20, 0, 0);
        }
        var resolved = new Dictionary<string, (string Pack, string Sha256)>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new Mock<IPackFileService>();
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        files.Setup(value => value.FindFile(It.IsAny<string>(), null)).Returns((string key, PackFileContainer? _) =>
        {
            var path = key.Replace('/', '\\').ToLowerInvariant();
            for (var index = 0; index < packs.Length; index++)
            {
                if (!packs[index].FileList.TryGetValue(path, out var resource)) continue;
                if (!resolved.ContainsKey(path)) resolved.Add(path, (packNames[index], Convert.ToHexString(SHA256.HashData(resource.DataSource.ReadData()))));
                return resource;
            }
            missing.Add(path);
            return null;
        });
        files.Setup(value => value.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()))
            .Returns((PackFile file, PackFileContainer? _) => paths[Array.FindIndex(originalFiles, candidate => ReferenceEquals(candidate, file))]);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(graphics.GraphicsDevice);
            var live = new VfxPreviewDocumentStore();
            var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, new EffekseerVfxPreviewFactory(), devices.Object, live);
            var root = new GroupNode("actor");
            var result = scenario == 2
                ? builder.Build(null, new ParsedMetadataFile { Version = 2, Attributes = [(ParsedMetadataAttribute)first, (ParsedMetadataAttribute)second] }, null, root, null!, new AnimationPlayer(), null)
                : builder.Build(new ParsedMetadataFile { Version = 10, Attributes = [(ParsedMetadataAttribute)first] },
                    new ParsedMetadataFile { Version = 10, Attributes = [(ParsedMetadataAttribute)second] }, null, root, null!, new AnimationPlayer(), null);
            try
            {
                Assert.That(result.Instances, Has.Count.EqualTo(2));
                foreach (var instance in result.Instances) instance.Update(sceneTime);
                var nodes = root.Children.Select(marker =>
                {
                    var node = (VfxPreviewSceneNode)marker.Children.Single();
                    Assert.That(node.PrepareFrame(marker.ModelMatrix), Is.True, string.Join(',', node.Diagnostics.Select(diagnostic => diagnostic.ReasonKey)));
                    return node;
                }).ToArray();
                foreach (var instance in result.Instances) instance.Update(sceneTime);
                Assert.That(root.Children.All(marker => marker.IsVisible), Is.True, "Both authored META entries must remain visible at the shared playback stage, including the instantaneous impact's particle tail.");
                if (scenario == 2)
                {
                    Assert.That(nodes[0].TimeSeconds, Is.EqualTo(sceneTime - first.EffectStartTime).Within(.00001f));
                    Assert.That(nodes[1].TimeSeconds, Is.EqualTo(sceneTime - second.EffectStartTime).Within(.00001f));
                    Assert.That(nodes[0].ModelMatrix.Right.Length(), Is.EqualTo(((Effect_v12)first).Scale).Within(.00001f));
                    Assert.That(nodes[1].ModelMatrix.Right.Length(), Is.EqualTo(((Effect_v12)second).Scale).Within(.00001f));
                }
                var label = scenario == 2 ? "storm-dragon-pair" : scenario == 1 ? "mixed-official" : "same-official";
                var baseline = RenderMultiple(graphics.GraphicsDevice, [], label + "-baseline");
                var alone = new[] { RenderMultiple(graphics.GraphicsDevice, [nodes[0]], label + "-first"), RenderMultiple(graphics.GraphicsDevice, [nodes[1]], label + "-second") };
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var manifest = new
                    {
                        OfficialData = data,
                        Packs = packNames.Select(name => new { Name = name, Path = Path.GetFullPath(Path.Combine(data!, name)) }).ToArray(),
                        Effects = paths.Select((path, index) => new { Path = path, Sha256 = Convert.ToHexString(SHA256.HashData(originals[index])) }).ToArray(),
                        Dependencies = resolved.Select(item => new { Path = item.Key, item.Value.Pack, item.Value.Sha256 }).ToArray(),
                        Missing = missing.ToArray(),
                        SceneTime = sceneTime,
                        Positions = new[] { first.Position.ToString(), second.Position.ToString() },
                        Metadata = metadataEvidence,
                    };
                    File.WriteAllText(Path.Combine(output, "superview-multiple-" + label + "-sources.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                }
                var masks = new[] { 0, 1 }.Select(side => Enumerable.Range(0, baseline.Length)
                    .Where(index => (index % 512 < 256) == (side == 0) && PixelDifference(alone[side][index], baseline[index]) > 3).ToArray()).ToArray();
                foreach (var mask in masks) Assert.That(mask.Length, Is.GreaterThan(20), "Each complete official effect must be visible alone in its own region before comparing simultaneous draws.");
                foreach (var reverse in new[] { false, true })
                {
                    var combined = RenderMultiple(graphics.GraphicsDevice, reverse ? [nodes[1], nodes[0]] : nodes, label + (reverse ? "-second-first" : "-first-second"));
                    for (var side = 0; side < 2; side++)
                    {
                        var contributed = masks[side].Count(index => PixelDifference(combined[index], alone[1 - side][index]) > 3);
                        Assert.That(contributed, Is.GreaterThan(masks[side].Length * .8f),
                            $"Official Effect {side} must retain its contribution when both independent instances draw in order reverse={reverse}.");
                    }
                }
                for (var index = 0; index < originalFiles.Length; index++) Assert.That(originalFiles[index].DataSource.ReadData(), Is.EqualTo(originals[index]));
                if (metaFile != null) Assert.That(metaFile.DataSource.ReadData(), Is.EqualTo(metadataBytes));
            }
            finally { foreach (var instance in result.Instances) instance.CleanUp(); }
        }
        finally { graphics.Release(); }
    }

    private static int PixelDifference(Color first, Color second) => Math.Max(Math.Abs(first.R - second.R), Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));

    private static Color[] RenderMultiple(GraphicsDevice device, IReadOnlyList<VfxPreviewSceneNode> nodes, string label)
    {
        const int width = 512, height = 320;
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.DarkSlateGray);
        var camera = new Vector3(0, 8, 50);
        var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, new(0, 2, 0), Vector3.Up),
            Matrix.CreateOrthographic(80, 50, .1f, 150), camera, new(0, 2, 0), 0, 0, 0, 1, Vector3.One, []);
        foreach (var node in nodes) ((IRenderItem)node).Draw(device, parameters, RenderingTechnique.Normal);
        device.SetRenderTarget(null);
        var pixels = new Color[width * height]; target.GetData(pixels);
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var stream = File.Open(Path.Combine(output, "superview-multiple-" + label + ".png"), FileMode.CreateNew);
            target.SaveAsPng(stream, width, height);
        }
        return pixels;
    }

    [Test]
    public void Effect_UsesRealPreviewNodeWithMetadataTimeScaleAndReadOnlyBoneHistory()
    {
        var files = new Mock<IPackFileService>();
        var file = PackFile.CreateFromBytes("test.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>"));
        files.Setup(x => x.FindFile(@"vfx\test.xml", null)).Returns(file);
        files.Setup(x => x.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\test.xml");
        files.Setup(x => x.GetAllPackfileContainers()).Returns([]);
        var live = new VfxPreviewDocumentStore();
        var factory = new CapturingFactory();
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = Skeleton(player);
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(4) };
        foreach (var x in new[] { 0, 10, 20 })
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame { Position = [new(x, 0, 0)], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
        player.SetAnimation(clip, skeleton); player.Pause(); player.SeekToTimeSeconds(1.5f);
        var currentTime = player.CurrentTime; var framesChanged = 0;
        player.OnFrameChanged += _ => framesChanged++;
        var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(x => x.Skeleton).Returns(skeleton);
        var effect = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = "test", StartTime = 0.5f, EndTime = 2,
            Tracking = true, NodeIndex = 0, Position = new(3, 0, 0), Scale = 2 };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor")
        { ModelMatrix = Matrix.CreateRotationY(0.4f) * Matrix.CreateTranslation(7, 0, -3) });
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory, Mock.Of<IDeviceResolver>(), live);
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [effect] }, null, root, provider.Object, player, null);
        var instance = (EffectMetaInstance)result.Instances.Single();
        instance.Update(1.5f);
        var marker = root.Children.Single();
        var node = (VfxPreviewSceneNode)marker.Children.Single();
        Assert.That(node.PrepareFrame(marker.ModelMatrix), Is.True);
        var preview = factory.Created.Single();
        Assert.Multiple(() =>
        {
            Assert.That(preview.Time, Is.EqualTo(1));
            Assert.That(preview.Attachment.Right.Length(), Is.EqualTo(2).Within(0.00001));
            Assert.That(Vector3.Distance(preview.Attachment.Translation, instance.FocusPosition), Is.LessThan(0.0001f),
                "The particle root must match the marker/Gizmo even under a rotated and translated parent.");
            Assert.That(preview.History!(0).Translation.X, Is.LessThan(preview.Attachment.Translation.X - 1));
            Assert.That(player.CurrentTime, Is.EqualTo(currentTime));
            Assert.That(framesChanged, Is.EqualTo(0));
            Assert.That(node.IsSelectable, Is.False);
            Assert.That(node.IsEditable, Is.False);
        });
        instance.Update(0.25f); Assert.That(marker.IsVisible, Is.False);
        instance.ShowForEntireAnimation = true; Assert.That(marker.IsVisible, Is.True);
        instance.CleanUp();
        Assert.That(preview.Disposed, Is.True);
        Assert.That(root.Children, Is.Empty);
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.False);
    }

    [Test]
    public void UnsavedVfxChanges_ReloadOnlyTheirDependentPreview_AndInvalidValuesStopThenRecover()
    {
        var files = new Mock<IPackFileService>();
        var original = Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>");
        var file = PackFile.CreateFromBytes("test.xml", original);
        files.Setup(x => x.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\test.xml");
        files.Setup(x => x.GetAllPackfileContainers()).Returns([]);
        var live = new VfxPreviewDocumentStore(); var factory = new CapturingFactory();
        using var source = new VfxPreviewDocumentSource(files.Object, file, live);
        using var node = new VfxPreviewSceneNode(factory, Mock.Of<IDeviceResolver>(), source) { TimeSeconds = 0.4f };
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        var preview = factory.Created.Single();
        live.Publish(new object(), new object(), new([9], "other.xml", _ => null));
        node.PrepareFrame(Matrix.Identity); Assert.That(preview.Reloads, Is.EqualTo(1));
        var owner = new object();
        live.Publish(owner, file, new([1, 2, 3], @"vfx\test.xml", _ => null));
        node.PrepareFrame(Matrix.Identity);
        Assert.That(preview.Snapshot!.GameBytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(preview.Time, Is.EqualTo(0.4f));
        live.Publish(owner, file, null);
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.False); Assert.That(preview.Disposed, Is.True);
        Assert.That(node.Diagnostics.Single().ReasonKey, Is.EqualTo("Vfx.Preview.LiveDocumentInvalid"));
        live.Publish(owner, file, new(original, @"vfx\test.xml", _ => null));
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        Assert.That(factory.Created.Count, Is.EqualTo(2));
        Assert.That(file.DataSource.ReadData(), Is.EqualTo(original));
    }

    [Test]
    public void SuperViewEffect_RendersRealGameParticlesOffscreen_AndAcceptsUnsavedEdits()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var packs = new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" }
            .Select(name => loader.Load(Path.Combine(data!, name))!).ToArray();
        const string path = @"vfx\wh_dlc08_lib_pinning_icicles.xml";
        var file = packs[0].FileList[path]; var original = file.DataSource.ReadData();
        var files = new Mock<IPackFileService>();
        files.Setup(x => x.GetAllPackfileContainers()).Returns([]);
        files.Setup(x => x.FindFile(It.IsAny<string>(), null)).Returns((string key, PackFileContainer? _) =>
            packs.Select(pack => pack.FileList.GetValueOrDefault(key.Replace('/', '\\').ToLowerInvariant())).FirstOrDefault(value => value != null));
        files.Setup(x => x.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(path);
        var live = new VfxPreviewDocumentStore();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(x => x.Device).Returns(graphics.GraphicsDevice);
            var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, new EffekseerVfxPreviewFactory(), devices.Object, live);
            var root = new GroupNode("actor");
            var effect = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = "wh_dlc08_lib_pinning_icicles", EndTime = 2 };
            var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [effect] }, null, root, null!, new AnimationPlayer(), null);
            var instance = (EffectMetaInstance)result.Instances.Single();
            try
            {
                instance.Update(0.2f);
                var marker = root.Children.Single(); var node = (VfxPreviewSceneNode)marker.Children.Single();
                Assert.That(node.PrepareFrame(marker.ModelMatrix), Is.True, string.Join(',', node.Diagnostics.Select(x => x.ReasonKey)));
                Assert.That(node.ParticleCount, Is.GreaterThan(0));
                var baseline = Render(graphics.GraphicsDevice, null);
                var pixels = Render(graphics.GraphicsDevice, node);
                Assert.That(pixels.Zip(baseline).Count(pair => pair.First != pair.Second), Is.GreaterThan(30));
                var document = VfxDocument.Read(original);
                foreach (var emitter in document.Root.Element("vfx")!.Element("emitters")!.Elements("emitter"))
                    emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "Enable"),
                        new System.Xml.Linq.XAttribute("quality", "0"), new System.Xml.Linq.XAttribute("value", "false")));
                live.Publish(this, file, new(document.Write(), path, _ => null));
                Assert.That(node.PrepareFrame(marker.ModelMatrix), Is.False);
                Assert.That(Render(graphics.GraphicsDevice, node), Is.EqualTo(baseline));
                Assert.That(file.DataSource.ReadData(), Is.EqualTo(original));
                Assert.That(effect.Position, Is.EqualTo(Vector3.Zero));
            }
            finally { instance.CleanUp(); live.Remove(this); }
        }
        finally { graphics.Release(); }
    }

    private static Color[] Render(GraphicsDevice device, VfxPreviewSceneNode? node)
    {
        const int size = 256;
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.DarkSlateGray);
        var camera = new Vector3(12, 12, 36);
        ((IRenderItem?)node)?.Draw(device, new(Matrix.CreateLookAt(camera, new(0, 2, 0), Vector3.Up),
            Matrix.CreateOrthographic(30, 30, 0.1f, 100), camera, new(0, 2, 0), 0, 0, 0, 1, Vector3.One, []), RenderingTechnique.Normal);
        device.SetRenderTarget(null);
        var pixels = new Color[size * size]; target.GetData(pixels);
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (node != null && node.ParticleCount > 0 && !string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var stream = File.Create(Path.Combine(output, "superview-real-particles.png")); target.SaveAsPng(stream, size, size);
        }
        return pixels;
    }

    private static GameSkeleton Skeleton(AnimationPlayer player)
    {
        var file = new AnimationFile { Header = new() { SkeletonName = "test" }, Bones = [new() { Name = "root", ParentId = -1 }] };
        var frame = new AnimationFile.Frame { Transforms = [new RmvVector3(Vector3.Zero)], Quaternion = [new RmvVector4(0, 0, 0, 1)] };
        var part = new AnimationFile.AnimationPart(); part.DynamicFrames.Add(frame); file.AnimationParts.Add(part);
        return new(file, player);
    }
    private sealed class CapturingFactory : IVfxPreviewFactory
    {
        public List<CapturingPreview> Created { get; } = [];
        public IVfxPreview Create(GraphicsDevice device) { var preview = new CapturingPreview(); Created.Add(preview); return preview; }
    }
    private sealed class CapturingPreview : IVfxPreview
    {
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => 1;
        public int Reloads;
        public int Draws;
        public VfxPreviewSnapshot? Snapshot;
        public bool Disposed;
        public float Time;
        public Matrix Attachment;
        public Func<float, Matrix>? History;
        public void Reload(VfxPreviewSnapshot snapshot) { Snapshot = snapshot; Reloads++; }
        public void Advance(float seconds, Matrix attachment) => Seek(Time + seconds, attachment);
        public void Seek(float seconds, Matrix attachment) { Time = seconds; Attachment = attachment; }
        public void Seek(float seconds, Matrix attachment, Func<float, Matrix> history) { Seek(seconds, attachment); History = history; }
        public void Draw(CommonShaderParameters parameters) { Draws++; }
        public void Dispose() => Disposed = true;
    }
}
