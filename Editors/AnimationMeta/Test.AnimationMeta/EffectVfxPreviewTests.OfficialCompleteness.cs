using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    private ManyOfficialResources? _completenessResources;

    [TestCase(@"animations\battle\bear01\attacks\be1_elemental_attack_04.anm.meta", 13,
        "b1f07b23ce4cf4014086ac6fbce023eeb191a0c29d9207034ac7741a54bd9360")]
    [TestCase(@"animations\battle\bird01\rebirth\bi1_rebirth_01.anm.meta", 41,
        "f6ac80a2b038bd1567c1f2cb3331c66b16f0839c772bc147da7b15a8c55153f9")]
    [TestCase(@"animations\battle\gyrocopter01\wm_gyrocopter01_fly_death_hit_02_fall.anm.meta", 3,
        "2900b63750d07ed5efb105f2308c7109ff88359fea2c66379723d1ffddc135b8")]
    public void CompleteOfficialMeta_EveryEffectKeepsItsResourceTimelineScaleAndBoneHistory(
        string path, int expectedEffects, string expectedHash)
    {
        var official = CompletenessResources();
        var entry = official.Index[path];
        var bytes = entry.File.DataSource.ReadData();
        Assert.That(Path.GetFileName(entry.Pack), Is.EqualTo("anim2.pack").IgnoreCase);
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(expectedHash));
        var parsed = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(bytes);
        var authored = parsed.Attributes.OfType<IEffectMeta>().ToArray();
        Assert.That(authored, Has.Length.EqualTo(expectedEffects));
        Assert.That(authored.All(effect => effect is Effect_v12), Is.True, "Keep the fresh official sample version boundary explicit.");
        var originalFields = EffectFields(authored);
        var player = new AnimationPlayer { IsEnabled = true };
        var duration = Math.Max(1, authored.Max(effect => effect.EffectEndTime) + 1);
        var skeleton = CompletenessSkeleton(player, authored.Max(effect => effect.NodeIndex) + 1, duration);
        var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        var factory = new CapturingFactory();
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("complete-official-meta")
        { ModelMatrix = Matrix.CreateRotationY(.4f) * Matrix.CreateTranslation(7, 0, -3) });
        var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!, factory,
            Mock.Of<IDeviceResolver>(), new());
        var result = builder.Build(null, parsed, null, root, provider.Object, player, null);
        var effects = result.Instances.OfType<EffectMetaInstance>().ToArray();
        var records = new List<object>();
        try
        {
            Assert.That(effects, Has.Length.EqualTo(expectedEffects));
            Assert.That(effects.Select(effect => effect.Source).Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(expectedEffects));
            Assert.That(effects.Select(effect => effect.Source), Is.EquivalentTo(authored.Cast<ParsedMetadataAttribute>()));
            foreach (var source in authored)
            {
                var instance = effects.Single(effect => ReferenceEquals(effect.Source, source));
                var node = EffectParticleNode(instance);
                var span = source.EffectEndTime - source.EffectStartTime;
                var age = span > .000001f ? Math.Min(.125f, span * .5f) : .125f;
                var time = source.EffectStartTime + age;
                player.SeekToTimeSeconds(time);
                instance.Update(time);
                var playerTime = player.CurrentTime;
                var frameChanges = 0;
                FrameChanged onFrameChanged = _ => frameChanges++;
                player.OnFrameChanged += onFrameChanged;
                try
                {
                    Assert.That(node.PrepareFrame(instance.WorldTransform), Is.True);
                    var preview = factory.Created.Last();
                    var rootPath = $@"vfx\{source.VfxName}.xml";
                    Assert.That(official.Index.ContainsKey(rootPath), Is.True, "Every original root resource must resolve independently.");
                    Assert.That(preview.Snapshot!.SourcePath, Is.EqualTo(rootPath).IgnoreCase);
                    Assert.That(preview.Snapshot.GameBytes, Is.EqualTo(official.Index[rootPath].File.DataSource.ReadData()));
                    Assert.That(preview.Snapshot.Quality, Is.EqualTo("0"));
                    Assert.That(preview.Snapshot.RootSelection, Is.Null, "An editor-local layer subset must not filter a META Effect.");
                    Assert.That(preview.Time, Is.EqualTo(age).Within(.00001f));
                    var authoredScale = ((Effect_v12)source).Scale;
                    var attachmentScale = preview.Attachment.Right.Length() / instance.WorldTransform.Right.Length();
                    Assert.That(attachmentScale, Is.EqualTo(authoredScale).Within(.00001f));
                    Assert.That(Vector3.Distance(preview.Attachment.Translation, instance.FocusPosition), Is.LessThan(.0001f));
                    Assert.That(preview.History, Is.Not.Null);
                    var birth = preview.History!(0);
                    var now = preview.History(age);
                    var expectedBirthRightLength = Matrix.CreateFromQuaternion(new Quaternion(source.Orientation)).Right.Length() * authoredScale;
                    Assert.That(birth.Right.Length(), Is.EqualTo(expectedBirthRightLength).Within(.00001f));
                    Assert.That(Vector3.Distance(now.Translation, preview.Attachment.Translation), Is.LessThan(.0001f));
                    if (source.Tracking)
                    {
                        Assert.That(instance.HighlightedBoneIndex, Is.EqualTo(source.NodeIndex));
                        Assert.That(Vector3.Distance(birth.Translation, now.Translation), Is.GreaterThan(.0001f),
                            "Each authored bone attachment must sample its own earlier pose.");
                    }
                    else Assert.That(Vector3.Distance(birth.Translation, now.Translation), Is.LessThan(.0001f));
                    Assert.That(player.CurrentTime, Is.EqualTo(playerTime));
                    Assert.That(frameChanges, Is.Zero, "History sampling must not seek the shared animation player.");
                    records.Add(new { source.VfxName, source.NodeIndex, source.Tracking,
                        Start = source.EffectStartTime, End = source.EffectEndTime, Time = time,
                        Elapsed = preview.Time, Root = preview.Snapshot.SourcePath,
                        Scale = attachmentScale, BasisRightLength = preview.Attachment.Right.Length(),
                        Birth = birth.Translation.ToString(), Now = now.Translation.ToString() });
                }
                finally { player.OnFrameChanged -= onFrameChanged; }
            }
            Assert.That(factory.Created, Has.Count.EqualTo(expectedEffects));
            Assert.That(EffectFields(authored), Is.EqualTo(originalFields));
            Assert.That(entry.File.DataSource.ReadData(), Is.EqualTo(bytes));
            ManyEvidence(Path.GetFileName(path.Replace('\\', '/')) + "-official-completeness.json", new
            {
                Path = path, Sha256 = expectedHash, OriginalEffects = expectedEffects,
                Instances = records, Diagnostics = result.Diagnostics, Sources = official.Origins.Values,
                Missing = official.Missing.ToArray(),
                Scope = "Complete unchanged official META and root resources. A controlled multi-bone animation checks attachment routing/history, not the original game pose or native visual equivalence. No metadata position, time, scale or source subset is changed."
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [Test]
    public void CompleteOfficialBearMeta_FrostTrailCreatesRealParticlesBeyondItsMarker()
    {
        var official = CompletenessResources();
        const string metadataPath = @"animations\battle\bear01\attacks\be1_elemental_attack_04.anm.meta";
        const string rootPath = @"vfx\wh3_main_entity_elemental_bear_frost_trail.xml";
        var entry = official.Index[metadataPath];
        var bytes = entry.File.DataSource.ReadData();
        var parsed = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(bytes);
        var authored = parsed.Attributes.OfType<IEffectMeta>().ToArray();
        Assert.That(authored, Has.Length.EqualTo(13));
        var originalFields = EffectFields(authored);
        var source = authored.Single(effect => effect.VfxName == "wh3_main_entity_elemental_bear_frost_trail");
        const string animationPath = @"animations\battle\bear01\attacks\be1_elemental_attack_04.anim";
        const string skeletonPath = @"animations\skeletons\bear01.anim";
        var animationFile = official.Index[animationPath].File;
        var skeletonFile = official.Index[skeletonPath].File;
        var animationBytes = animationFile.DataSource.ReadData();
        var skeletonBytes = skeletonFile.DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(animationBytes)),
            Is.EqualTo("2b8f9a2960cfd88cfdc080b7d21f42d30951b856b6f6abbb6c8fdd7440cb56b6"));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(skeletonBytes)),
            Is.EqualTo("99072698cd88b64ef00d41b9f4124629505ce810261b5b3e2ec623a1c39b07b8"));
        var rootFile = official.Index[rootPath].File;
        var rootBytes = rootFile.DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(rootBytes)),
            Is.EqualTo("afc310f423091e5a10763aac19a01985edd91c15f0c2b65fd92ab7728dfe5f61"));
        using var documentSource = new VfxPreviewDocumentSource(official.Files.Object, rootFile, new());
        var inspection = VfxPreviewInspector.Inspect(documentSource.Read()!);
        Assert.That(inspection.EmitterCount, Is.GreaterThan(0),
            "The complete official root enables frost_decal_trail; native outer atlas zero is a valid per-axis sentinel, not a reason to show only a marker.");
        Assert.That(inspection.Diagnostics.Any(diagnostic => diagnostic.Layer == "frost_decal_trail"
            && diagnostic.Field == "atlas_number_of_cells_x" && diagnostic.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.False);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(graphics.GraphicsDevice);
            var player = new AnimationPlayer { IsEnabled = true };
            var skeleton = new GameSkeleton(AnimationFile.Create(skeletonFile), player);
            var clip = new AnimationClip(AnimationFile.Create(animationFile), skeleton);
            player.SetAnimation(clip, skeleton); player.Pause();
            var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(skeleton);
            var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!,
                new EffekseerVfxPreviewFactory(), devices.Object, new());
            var root = new GroupNode("complete-official-bear");
            var result = builder.Build(null, parsed, null, root, provider.Object, player, null);
            try
            {
                var effects = result.Instances.OfType<EffectMetaInstance>().ToArray();
                Assert.That(effects, Has.Length.EqualTo(13));
                var time = source.EffectStartTime + .5f;
                player.SeekToTimeSeconds(time);
                foreach (var effect in effects) effect.Update(time);
                var instance = effects.Single(effect => ReferenceEquals(effect.Source, source));
                var node = EffectParticleNode(instance);
                Assert.That(node.Parent.IsVisible, Is.True);
                Assert.That(node.PrepareFrame(instance.WorldTransform), Is.True,
                    string.Join(',', node.Diagnostics.Select(diagnostic => diagnostic.ReasonKey)));
                Assert.That(node.ParticleCount, Is.GreaterThan(0), "A marker or successful root lookup is insufficient: the real shared runtime must contain particles.");
                Assert.That(node.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
                var focus = instance.FocusPosition;
                var eye = focus + new Vector3(0, 20, 30);
                var parameters = new CommonShaderParameters(Matrix.CreateLookAt(eye, focus, Vector3.Up),
                    Matrix.CreateOrthographic(60, 60, .1f, 400), eye, focus, 0, 0, 0, 1, Vector3.One, []);
                Assert.That(parameters.VfxGroundHeight, Is.Null, "Use the current SuperView draw contract, without an editor-only synthetic ground override.");
                var preview = typeof(VfxPreviewSceneNode).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node)!;
                var simulation = preview.GetType().GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                var frame = (IEnumerable)simulation.GetType().GetMethod("Frame")!.Invoke(simulation,
                    [parameters with { VfxGroundHeight = node.FlatGroundHeight }])!;
                var projected = frame.Cast<object>().ToArray();
                Assert.That(projected.Length, Is.GreaterThan(0), "The original tracked bone, scale and birth radius must reach actual projected particles on the scene receiver.");
                var device = graphics.GraphicsDevice;
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(Color.Transparent);
                ((IRenderItem)node).Draw(device, parameters, RenderingTechnique.Normal);
                device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels);
                var contributingPixels = pixels.Count(pixel => pixel.X > .00001f || pixel.Y > .00001f || pixel.Z > .00001f);
                Assert.That(contributingPixels, Is.GreaterThan(0), "The SuperView node must pass its receiver to the actual renderer; live particles alone are insufficient.");
                TestContext.Progress.WriteLine($"Official Bear: {node.ParticleCount} live, {projected.Length} projected particles, {contributingPixels} contributing pixels on the explicit world-Y0 receiver.");
                Assert.That(EffectFields(authored), Is.EqualTo(originalFields));
                Assert.That(rootFile.DataSource.ReadData(), Is.EqualTo(rootBytes));
                Assert.That(entry.File.DataSource.ReadData(), Is.EqualTo(bytes));
                Assert.That(animationFile.DataSource.ReadData(), Is.EqualTo(animationBytes));
                Assert.That(skeletonFile.DataSource.ReadData(), Is.EqualTo(skeletonBytes));
                ManyEvidence("bear-frost-complete-meta-particles.json", new
                {
                    Path = metadataPath, Root = rootPath, OriginalEffects = authored.Length,
                    RuntimeEffects = effects.Length, Time = time, node.TimeSeconds, node.ParticleCount,
                    ProjectedParticleCount = projected.Length, ContributingPixels = contributingPixels,
                    SceneGroundHeight = node.FlatGroundHeight, Animation = animationPath, Skeleton = skeletonPath,
                    Diagnostics = node.Diagnostics, Sources = official.Origins.Values, Missing = official.Missing.ToArray(),
                    Scope = "Complete original META, original animation/bear skeleton, VFX root and dependencies through the real shared preview. Authored position, orientation, scale and tracking40 are preserved. Actual node drawing supplies the explicit world-Y0 scene receiver. This proves visible contribution, not native visual equivalence."
                });
            }
            finally { foreach (var instance in result.Instances) instance.CleanUp(); }
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void BuilderEffect_DrawSuppliesSceneGroundAndKeepsAnExplicitCallerHeight()
    {
        var (files, file) = GroundContractFiles();
        var factory = new GroundCapturingFactory();
        var effect = new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = "ground_contract", EndTime = 2 };
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory,
            Mock.Of<IDeviceResolver>(), new());
        var root = new GroupNode("ground-contract");
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [effect] },
            null, root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            instance.Update(.5f);
            var node = EffectParticleNode(instance);
            Assert.That(node.PrepareFrame(instance.WorldTransform), Is.True);
            var parameters = GroundParameters();
            ((IRenderItem)node).Draw(null!, parameters, RenderingTechnique.Normal);
            ((IRenderItem)node).Draw(null!, parameters with { VfxGroundHeight = 7 }, RenderingTechnique.Normal);
            Assert.That(factory.Preview.Draws, Has.Count.EqualTo(2));
            Assert.That(factory.Preview.Draws[0].VfxGroundHeight, Is.EqualTo(0),
                "The SuperView builder explicitly supplies its world-Y0 scene grid, not an animated bone height.");
            Assert.That(factory.Preview.Draws[1].VfxGroundHeight, Is.EqualTo(7), "An explicit caller receiver remains authoritative.");
            Assert.That(factory.Preview.Draws.All(draw => draw.VfxWriteGroundDepth), Is.True,
                "The scene receiver must be present before the first VFX instance contributes colour.");
            Assert.That(parameters.VfxGroundHeight, Is.Null, "The shared caller parameters must remain immutable.");
            Assert.That(file.DataSource.ReadData(), Is.EqualTo(Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>")));
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [Test]
    public void DirectPreviewNode_WithoutAnExplicitSceneReceiverKeepsNullGround()
    {
        var (files, file) = GroundContractFiles();
        var factory = new GroundCapturingFactory();
        using var node = new VfxPreviewSceneNode(factory, Mock.Of<IDeviceResolver>(), new(files.Object, file, new()));
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        ((IRenderItem)node).Draw(null!, GroundParameters(), RenderingTechnique.Normal);
        Assert.That(factory.Preview.Draws.Single().VfxGroundHeight, Is.Null,
            "A direct shared node cannot invent a ground receiver for every caller.");
        Assert.That(factory.Preview.Draws.Single().VfxWriteGroundDepth, Is.False);
    }

    private static CommonShaderParameters GroundParameters() => new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    private static (Mock<IPackFileService> Files, PackFile File) GroundContractFiles()
    {
        var file = PackFile.CreateFromBytes("ground_contract.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>"));
        var files = new Mock<IPackFileService>();
        files.Setup(value => value.FindFile(@"vfx\ground_contract.xml", null)).Returns(file);
        files.Setup(value => value.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\ground_contract.xml");
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        return (files, file);
    }

    private sealed class GroundCapturingFactory : IVfxPreviewFactory
    {
        public GroundCapturingPreview Preview { get; } = new();
        public IVfxPreview Create(Microsoft.Xna.Framework.Graphics.GraphicsDevice device) => Preview;
    }

    private sealed class GroundCapturingPreview : IVfxPreview
    {
        public List<CommonShaderParameters> Draws { get; } = [];
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => 1;
        public void Reload(VfxPreviewSnapshot snapshot) { }
        public void Advance(float seconds, Matrix attachment) { }
        public void Seek(float seconds, Matrix attachment) { }
        public void Draw(CommonShaderParameters parameters) => Draws.Add(parameters);
        public void Dispose() { }
    }

    private ManyOfficialResources CompletenessResources()
    {
        if (_completenessResources != null) return _completenessResources;
        var official = new ManyOfficialResources();
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA")!;
        var manifest = File.ReadAllLines(Path.Combine(data, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        const string additionalPack = "warmachines_dds.pack";
        var path = Path.Combine(data, additionalPack);
        Assert.That(new FileInfo(path).Length, Is.EqualTo(long.Parse(manifest[additionalPack], CultureInfo.InvariantCulture)));
        var pack = new PackFileContainerLoader(null!).Load(path)!;
        foreach (var resource in pack.FileList)
            official.Index[resource.Key.Replace('/', '\\').ToLowerInvariant()] = (resource.Value, pack.SystemFilePath);
        _completenessResources = official;
        return official;
    }

    private static VfxPreviewSceneNode EffectParticleNode(EffectMetaInstance effect) =>
        (VfxPreviewSceneNode)typeof(EffectMetaInstance).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;

    private static string EffectFields(IEnumerable<IEffectMeta> effects) => JsonSerializer.Serialize(effects.Select(effect => new
    {
        effect.VfxName, effect.NodeIndex, effect.Tracking, Position = effect.Position.ToString(),
        Orientation = effect.Orientation.ToString(), effect.EffectStartTime, effect.EffectEndTime,
        Scale = ((Effect_v12)effect).Scale,
    }));

    private static GameSkeleton CompletenessSkeleton(AnimationPlayer player, int boneCount, float duration)
    {
        var file = new AnimationFile { Header = new() { SkeletonName = "controlled-official-meta-routing" },
            Bones = Enumerable.Range(0, boneCount).Select(index => new AnimationFile.BoneInfo
            { Id = index, Name = "bone" + index, ParentId = -1 }).ToArray() };
        var frame = new AnimationFile.Frame
        {
            Transforms = Enumerable.Range(0, boneCount).Select(index => new RmvVector3(new Vector3(index * .1f, 0, 0))).ToList(),
            Quaternion = Enumerable.Range(0, boneCount).Select(_ => new RmvVector4(0, 0, 0, 1)).ToList(),
        };
        var part = new AnimationFile.AnimationPart(); part.DynamicFrames.Add(frame); file.AnimationParts.Add(part);
        var skeleton = new GameSkeleton(file, player);
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(duration) };
        // Keep the held final sample beyond every authored Effect time.
        var frameCount = Math.Max(3, (int)Math.Ceiling(duration * 4));
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var x = 80f * frameIndex / (frameCount - 1);
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame
            {
                Position = Enumerable.Range(0, boneCount).Select(index => new Vector3(x + index * .1f, 0, 0)).ToList(),
                Rotation = Enumerable.Range(0, boneCount).Select(_ => Quaternion.Identity).ToList(),
                Scale = Enumerable.Range(0, boneCount).Select(_ => Vector3.One).ToList(),
            });
        }
        Assert.That(clip.Timebase!.GetSampleTime(frameCount - 1).TotalSeconds, Is.GreaterThan(duration - 1),
            "The controlled clip must keep moving throughout the complete official META timeline.");
        player.SetAnimation(clip, skeleton); player.Pause();
        return skeleton;
    }
}
