using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(@"animations\battle\bear01\attacks\be1_elemental_attack_04.anm.meta")]
    [TestCase(@"animations\battle\bird01\rebirth\bi1_rebirth_01.anm.meta")]
    [TestCase(@"animations\battle\humanoid01e\storm_dragon\attacks\hu1e_storm_dragon_attack_01.anm.meta")]
    [TestCase(@"animations\battle\humanoid01e\yuan_bo_human\attack\hu1e_dlc24_yuan_bo_sword_attack_06.anm.meta")]
    [TestCase(@"animations\battle\giant01c\saytang\attacks\gi01c_saytang_bow_attack_05.anm.meta")]
    [TestCase(@"animations\battle\humanoid01c\nurgle_sorcerer_staff_and_mace\stand\hu1c_nurgle_sorcerer_staff_and_mace_stand_idle_01.anm.meta")]
    [TestCase(@"animations\battle\giant01c\saytang\attacks\gi01c_saytang_bow_attack_05.anm.meta", 1)]
    [TestCase(@"animations\battle\humanoid01c\nurgle_sorcerer_staff_and_mace\stand\hu1c_nurgle_sorcerer_staff_and_mace_stand_idle_01.anm.meta", 2)]
    [TestCase(@"animations\battle\bear01\attacks\be1_elemental_attack_04.anm.meta", 1)]
    [TestCase(@"animations\battle\humanoid06i\skulltaker\attacks\hu06i_skulltaker_attack_01.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid06i\skulltaker\attacks\hu06i_skulltaker_attack_02.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid06i\skulltaker\attacks\hu06i_skulltaker_attack_03.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid06i\skulltaker\attacks\hu06i_skulltaker_attack_05.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid06i\skulltaker\attacks\hu06i_skulltaker_attack_06.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid06i\rider\heavyquadruper04\sword\attacks\hu06i_dlc26_rider_attack_01.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid06i\rider\heavyquadruper04\sword\attacks\hu06i_dlc26_rider_attack_04.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid16b\masque_of_slanesh\attacks\hu16b_masque_attack_01.anm.meta", 0, true)]
    [TestCase(@"animations\battle\humanoid16b\masque_of_slanesh\attacks\hu16b_masque_attack_02.anm.meta", 0, true)]
    public void OfficialAttachmentPixels_CompleteOriginalActionsMatchIndependentBoneTransforms(string path, int placement = 0, bool sampleEventStages = false)
    {
        var official = CompletenessResources();
        AddAttachmentPacks(official);
        official.Origins.Clear(); official.Missing.Clear();
        PackFile Read(string key) => official.Files.Object.FindFile(key) ?? throw new FileNotFoundException(key);
        var metaFile = Read(path);
        var metaBytes = metaFile.DataSource.ReadData();
        var parsed = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(metaBytes);
        var sources = parsed.Attributes.OfType<IEffectMeta>().ToArray();
        Assert.That(sources.Length, Is.GreaterThan(0));
        var fields = EffectFields(sources);
        var animationPath = path.Replace(".anm.meta", ".anim", StringComparison.Ordinal);
        var animationFile = Read(animationPath);
        var animation = AnimationFile.Create(animationFile);
        var skeletonPath = @"animations\skeletons\" + animation.Header.SkeletonName + ".anim";
        var skeletonFile = Read(skeletonPath);
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = new GameSkeleton(AnimationFile.Create(skeletonFile), player);
        var clip = new AnimationClip(animation, skeleton);
        player.SetAnimation(clip, skeleton); player.Pause();
        var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        var graphics = new GraphicsDeviceServiceMock();
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var actorWorld = placement switch
        {
            1 => Matrix.CreateScale(1.3f, .8f, 1.1f) * Matrix.CreateRotationY(.3f) * Matrix.CreateTranslation(2, 0, -1),
            2 => Matrix.CreateScale(-1.1f, 1.4f, .9f) * Matrix.CreateRotationX(.2f),
            _ => Matrix.Identity,
        };
        var root = scene.RootNode.AddObject(new GroupNode("official-action") { ModelMatrix = actorWorld });
        var references = new Dictionary<IEffectMeta, IVfxPreview>();
        MetaDataBuildResult? result = null;
        try
        {
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(graphics.GraphicsDevice);
            var store = new VfxPreviewDocumentStore();
            var factory = new EffekseerVfxPreviewFactory();
            var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!, factory, devices.Object, store);
            builder.EffectPreviewContext = new() { BypassTerrainSelection = true };
            result = builder.Build(null, parsed, null, root, provider.Object, player, null);
            var instances = result.Instances.OfType<EffectMetaInstance>().ToArray();
            Assert.That(instances.Length, Is.EqualTo(sources.Length));
            var stages = new List<object>();
            var renderedParentScale = false;
            var renderedEventPixels = false;
            var duration = (float)player.Duration.TotalSeconds;
            var times = new List<float> { duration * .25f, duration * .5f, duration * .8f };
            var scaledSource = sources.OfType<Effect_v12>().FirstOrDefault(effect => effect.ScalesWithParent);
            if (scaledSource != null)
            {
                var span = scaledSource.EffectEndTime - scaledSource.EffectStartTime;
                times.Add(Math.Clamp(scaledSource.EffectStartTime + (span > .000001f ? span * .5f : .05f), 0, duration));
            }
            if (sampleEventStages)
                foreach (var source in sources.Where(effect => effect.EffectStartTime == effect.EffectEndTime))
                    foreach (var age in new[] { .2f, .4f, .6f })
                        times.Add(Math.Clamp(source.EffectStartTime + age, 0, duration));
            foreach (var time in times.Distinct().Order())
            {
                player.SeekToTimeSeconds(time);
                foreach (var instance in instances) instance.Update(time);
                var active = new List<VfxPreviewSceneNode>();
                var expected = new List<IVfxPreview>();
                foreach (var instance in instances)
                {
                    var source = (IEffectMeta)instance.Source;
                    if (time < source.EffectStartTime) continue;
                    var node = EffectParticleNode(instance);
                    var ready = node.PrepareFrame(instance.WorldTransform);
                    if (!references.TryGetValue(source, out var reference))
                    {
                        var file = Read(@"vfx\" + source.VfxName + ".xml");
                        using var document = new VfxPreviewDocumentSource(official.Files.Object, file, store);
                        reference = factory.Create(graphics.GraphicsDevice);
                        references.Add(source, reference);
                        reference.Reload(document.Read()!);
                    }
                    var age = Math.Max(0, time - source.EffectStartTime);
                    var span = source.EffectEndTime - source.EffectStartTime;
                    reference.EmissionEndTimeSeconds = span > .000001f ? span : Math.Max(0, duration - source.EffectStartTime);
                    Matrix At(float seconds)
                    {
                        var sceneTime = source.Tracking ? seconds + source.EffectStartTime : source.EffectStartTime;
                        var bone = source.NodeIndex < 0
                            ? OriginalRootMotionAt(source, skeleton, clip, animation.Header.FrameRate)
                            : AnimationSampler.Sample(
                            Math.Clamp((float)(sceneTime / player.Duration.TotalSeconds), 0, 1), skeleton, clip, player.AnimationRules)
                            .GetSkeletonAnimatedWorld(skeleton, source.NodeIndex);
                        var scale = source switch { Effect_v12 v => v.Scale, Effect_v11 v => v.Scale, Effect_v7 v => v.Scale,
                            Effect_v3 v => v.Scale, _ => 1 };
                        var combined = Matrix.CreateFromQuaternion(new Quaternion(source.Orientation))
                            * Matrix.CreateTranslation(source.Position) * bone * actorWorld;
                        var z = combined.Backward;
                        var x = Vector3.Normalize(Vector3.Cross(combined.Up, z));
                        var y = Vector3.Normalize(Vector3.Cross(z, x));
                        z.Normalize();
                        var rotation = Matrix.Identity;
                        rotation.Right = x; rotation.Up = y; rotation.Backward = z;
                        rotation = Matrix.CreateFromQuaternion(Quaternion.CreateFromRotationMatrix(rotation));
                        rotation.Translation = combined.Translation;
                        if (source is Effect_v12 { ScalesWithParent: true }) scale *= actorWorld.Up.Length();
                        return Matrix.CreateScale(scale) * rotation;
                    }
                    reference.Seek(age, At(age), At, true);
                    if (ready) active.Add(node);
                    if (!reference.IsDrained) expected.Add(reference);
                }
                var camera = new Vector3(0, 35, 65);
                var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, new(0, 7, 0), Vector3.Up),
                    Matrix.CreateOrthographic(85, 60, .1f, 400), camera, new(0, 7, 0), 0, 0, 0, 1, Vector3.One, [])
                { VfxGroundHeight = 0, VfxWriteGroundDepth = true };
                var label = Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) + "-placement" + placement + "-" + time.ToString("F3", CultureInfo.InvariantCulture);
                var actualPixels = DrawAttachmentPixels(graphics.GraphicsDevice, parameters,
                    () => { foreach (var node in active) ((IRenderItem)node).Draw(graphics.GraphicsDevice, parameters, RenderingTechnique.Normal); }, label + "-actual");
                var expectedPixels = DrawAttachmentPixels(graphics.GraphicsDevice, parameters,
                    () => { foreach (var preview in expected) preview.Draw(parameters); }, label + "-reference");
                var maximum = actualPixels.Zip(expectedPixels).Max(pair => PixelDifference(pair.First, pair.Second));
                var changed = actualPixels.Count(pixel => PixelDifference(pixel, Color.DarkSlateGray) > 3);
                renderedEventPixels |= changed > 20;
                var scaledEffects = instances.Where(instance => instance.Source is Effect_v12 { ScalesWithParent: true }
                    && active.Contains(EffectParticleNode(instance))).ToArray();
                renderedParentScale |= scaledEffects.Any(instance => EffectParticleNode(instance).ParticleCount > 0);
                stages.Add(new { Time = time, ActiveEffects = active.Count, ActiveParentScaleEffects = scaledEffects.Length,
                    Particles = active.Sum(node => node.ParticleCount),
                    MaximumChannelDifference = maximum, ChangedPixels = changed,
                    Entries = instances.Select(instance => new { ((IEffectMeta)instance.Source).VfxName,
                        ((IEffectMeta)instance.Source).NodeIndex, ((IEffectMeta)instance.Source).Tracking,
                        Active = active.Contains(EffectParticleNode(instance)), EffectParticleNode(instance).ParticleCount }).ToArray(),
                    Diagnostics = active.SelectMany(node => node.Diagnostics).Distinct().ToArray() });
                Assert.That(maximum, Is.LessThanOrEqualTo(3), "Actual node pixels must match independently computed original bone attachments.");
            }
            Assert.That(EffectFields(sources), Is.EqualTo(fields));
            Assert.That(metaFile.DataSource.ReadData(), Is.EqualTo(metaBytes));
            if (scaledSource != null) Assert.That(renderedParentScale, Is.True,
                "Parent-scale originals must include an active particle stage in the GPU comparison.");
            if (sampleEventStages)
            {
                Assert.That(renderedEventPixels, Is.True, "Complete original actions must contribute actual GPU pixels during the sampled event stages.");
                Assert.That(official.Missing, Is.Empty, "Event action originals must resolve their complete original dependencies.");
            }
            if (path.Contains(@"\bear01\", StringComparison.Ordinal) || path.Contains(@"\bird01\", StringComparison.Ordinal)
                || path.Contains(@"\storm_dragon\", StringComparison.Ordinal) || path.Contains(@"\yuan_bo_human\", StringComparison.Ordinal))
                Assert.That(official.Missing, Is.Empty, "Original attachment controls must resolve their original model/material dependencies.");
            ManyEvidence(Path.GetFileName(path.Replace('\\', '/')) + "-placement" + placement + "-attachment.json", new
            {
                Path = path, Placement = placement, Sha256 = Convert.ToHexString(SHA256.HashData(metaBytes)), Animation = animationPath, Skeleton = skeletonPath,
                OriginalEffectCount = sources.Length, Stages = stages, Sources = official.Origins.Values, Missing = official.Missing.ToArray(),
                Scope = "Complete untouched official META, original animation/skeleton and VFX dependencies with the native standalone preview's explicit original-resource terrain bypass. Real shared-node GPU output versus independently sampled original bone matrices. No game/Terry visual-equivalence claim."
            });
        }
        finally
        {
            if (result != null) foreach (var instance in result.Instances) instance.CleanUp();
            foreach (var reference in references.Values) reference.Dispose();
            graphics.Release();
        }
    }

    private static void AddAttachmentPacks(ManyOfficialResources official)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA")!;
        var manifest = File.ReadAllLines(Path.Combine(data, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture));
        foreach (var name in new[] { "variants_dds3.pack", "models2.pack", "models2_dds.pack", "models4.pack", "models4_dds.pack" })
        {
            if (official.Index.Values.Any(entry => Path.GetFileName(entry.Pack).Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            var path = Path.Combine(data, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            var pack = new PackFileContainerLoader(null!).Load(path)!;
            foreach (var entry in pack.FileList) official.Index[entry.Key.Replace('/', '\\').ToLowerInvariant()] = (entry.Value, path);
        }
    }

    private static Matrix OriginalRootMotionAt(IEffectMeta effect, GameSkeleton skeleton, AnimationClip clip, float fps)
    {
        if (effect.Tracking) return Matrix.Identity;
        var sample = effect.EffectStartTime * fps;
        var index = (int)sample;
        var fraction = sample - index;
        var last = clip.DynamicFrames.Count - 1;
        var next = index + 1;
        if ((uint)index >= (uint)last) { index = next = last; fraction = 0; }
        else if (next == last)
        {
            var interval = (float)clip.Duration.TotalSeconds * fps - index;
            if (interval > 0) fraction /= interval;
        }
        var a = clip.DynamicFrames[index]; var b = clip.DynamicFrames[next];
        var reference = Matrix.Transpose(Matrix.CreateFromQuaternion(skeleton.Rotation[0]));
        var first = RootHeading(reference * Matrix.CreateFromQuaternion(a.Rotation[0]));
        var second = RootHeading(reference * Matrix.CreateFromQuaternion(b.Rotation[0]));
        var angle = unchecked((ushort)(first + (int)MathF.Round(SelectionSignedAngle(second - first) * fraction,
            MidpointRounding.AwayFromZero)));
        return Matrix.CreateTranslation(-Vector3.Lerp(a.Position[0], b.Position[0], fraction))
            * Matrix.CreateRotationY(-SelectionSignedAngle(angle) * 9.58738019107841e-05f);

        ushort RootHeading(Matrix matrix)
        {
            var forward = (int)MathF.Round(MathF.Atan2(matrix.M31, matrix.M33) * 10430.3779296875f, MidpointRounding.AwayFromZero);
            if (skeleton.BoneNames[0] == "animroot") return unchecked((ushort)forward);
            var alternate = (int)MathF.Round(MathF.Atan2(matrix.M21, matrix.M23) * 10430.3779296875f, MidpointRounding.AwayFromZero);
            if (matrix.M32 >= 0) alternate -= 32768;
            var blend = Math.Clamp((Math.Abs(matrix.M32) - .4999999701976776f) * 2.062673330307007f, 0, 1);
            return unchecked((ushort)(forward + (int)MathF.Round(SelectionSignedAngle(alternate - forward) * blend,
                MidpointRounding.AwayFromZero)));
        }
    }

    private static Color[] DrawAttachmentPixels(GraphicsDevice device, CommonShaderParameters parameters, Action draw, string label)
    {
        const int width = 768, height = 544;
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkSlateGray, 1, 0);
        draw();
        device.SetRenderTarget(null);
        var pixels = new Color[width * height]; target.GetData(pixels);
        var folder = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
            using var stream = new FileStream(Path.Combine(folder, label + ".png"), FileMode.CreateNew);
            target.SaveAsPng(stream, width, height);
        }
        return pixels;
    }
}
