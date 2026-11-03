using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common;
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
    private const string TerrainActionPath = @"animations\battle\bigcat02b\deaths\bc2b_stand_death_01.anm.meta";
    private const string TerrainActionHash = "a76c864a6454816a4655216ee9859447097eb5e2aa97726388e852fb0dc216cb";

    [Test]
    public void OfficialTerrainSelectionPixels_CompleteOriginalActionMatchesIndependentDbResourcesForFourContexts()
    {
        var official = ResourceSelectionOfficialFiles();
        var action = LoadSelectionAction(official, TerrainActionPath, TerrainActionHash);
        Assert.That(action.Effect.TerrainMutable, Is.True);
        var table = ReadTerrainPixelOracle(SelectionRead(official, @"db\battle_entity_effects_tables\data__").DataSource.ReadData());
        var particles = ReadParticlePixelOracle(SelectionRead(official, @"db\particle_effects_tables\data__").DataSource.ReadData());
        // This exact row is independently established by the official original and native suffix audit.
        var row = table["wh_main_terrain_unit_impact_large"];
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var rendered = new Dictionary<int, SelectionPixelResult>();
            foreach (var terrain in new[] { 1, 10, 6, 16 })
            {
                var key = terrain == 16 ? null : row[terrain];
                if (key != null) Assert.That(particles.Contains(key), Is.True);
                var expected = key == null ? null : SelectionRead(official, @"vfx\" + key + ".xml");
                rendered[terrain] = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                    new() { TerrainType = terrain }, expected, "terrain-" + terrain, [.12f, .3f, .6f, 1f, 1.5f]);
            }
            var crossContextDifferences = new[] { (1, 10), (1, 6), (10, 6), (1, 16), (10, 16), (6, 16) }
                .Select(pair => new
                {
                    First = pair.Item1, Second = pair.Item2,
                    ChangedPixels = rendered[pair.Item1].Pixels.Zip(rendered[pair.Item2].Pixels)
                        .Select(frames => frames.First.Zip(frames.Second).Count(pixel => PixelDifference(pixel.First, pixel.Second) > 6)).ToArray()
                }).ToArray();
            ManyEvidence("resource-selection-terrain-complete.json", new
            {
                Scope = "Complete untouched official META, original animation/skeleton and complete VFX resources. Only explicit host terrain context changes. Actual Builder output is compared pixel-for-pixel with a separate GPU preview loaded from an independent binary DB oracle. Flat world-Y0 preview receiver; no game terrain sampling or game/Terry visual-equivalence claim.",
                action.Path, action.ManifestSha256, MetaSha256 = TerrainActionHash, action.AnimationPath, action.SkeletonPath,
                Cases = rendered.Select(pair => new { Terrain = pair.Key, pair.Value.Stages }).ToArray(),
                CrossContextDifferences = crossContextDifferences,
                Sources = official.Origins.Values, Missing = official.Missing.ToArray()
            });
            foreach (var difference in crossContextDifferences)
                Assert.That(difference.ChangedPixels.Max(), Is.GreaterThan(24),
                    $"Terrain {difference.First}/{difference.Second} must produce visibly different pixels, not just different particle counts.");
            Assert.That(official.Missing, Is.Empty);
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OfficialDistanceCullingPixels_ActorLodFlagSuppressesAndOverrideRestoresTheCompleteOriginalAction()
    {
        var official = ResourceSelectionOfficialFiles();
        var action = LoadSelectionAction(official, TerrainActionPath, TerrainActionHash);
        Assert.That(action.Effect.DistanceCulled, Is.True);
        var row = ReadTerrainPixelOracle(SelectionRead(official, @"db\battle_entity_effects_tables\data__").DataSource.ReadData())
            ["wh_main_terrain_unit_impact_large"];
        var selected = SelectionRead(official, @"vfx\" + row[1] + ".xml");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var visible = RenderSelectionContext(official, graphics.GraphicsDevice, action, new(), selected, "lod-visible", [.2f]);
            var culled = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                new() { SuppressDistanceCulledEffects = true }, null, "lod-culled", [.2f]);
            var restored = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                new() { SuppressDistanceCulledEffects = true, DistanceCullingOverrideCount = 1 }, selected, "lod-override", [.2f]);
            Assert.That(visible.Pixels[0].Zip(culled.Pixels[0]).Count(pair => PixelDifference(pair.First, pair.Second) > 6), Is.GreaterThan(24));
            Assert.That(visible.Pixels[0].Zip(restored.Pixels[0]).Max(pair => PixelDifference(pair.First, pair.Second)), Is.LessThanOrEqualTo(3));
            ManyEvidence("resource-selection-distance-complete.json", new
            {
                Scope = "Complete untouched official distance-culled META/action. Host actor-LOD condition and native override count are explicit DIAG inputs, not an inferred camera-distance threshold.",
                action.Path, action.ManifestSha256, Visible = visible.Stages, Culled = culled.Stages, Restored = restored.Stages, Sources = official.Origins.Values, Missing = official.Missing.ToArray()
            });
            Assert.That(official.Missing, Is.Empty);
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OfficialIdSelectionPixels_MatchingAndDisabledFiltersRestoreTheExactOriginalActionPixels()
    {
        var official = ResourceSelectionOfficialFiles();
        var action = LoadSelectionAction(official, @"animations\battle\bigcat02b\attacks\bc2b_attack_01.anm.meta",
            "e99674c50346bc083b51dbb785e1544979797e52b0e2ae5d601ff1e38fcf11a2");
        Assert.That(action.Effect.Id, Is.EqualTo(1));
        var selected = SelectionRead(official, @"vfx\" + action.Effect.VfxName + ".xml");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var disabled = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                new() { ApplyIdFilter = false, CurrentEffectId = 2 }, selected, "id-disabled", [.12f]);
            var matching = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                new() { ApplyIdFilter = true, CurrentEffectId = 1 }, selected, "id-matching", [.12f]);
            var mismatch = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                new() { ApplyIdFilter = true, CurrentEffectId = 2 }, null, "id-mismatch", [.12f]);
            Assert.That(disabled.Pixels[0].Zip(matching.Pixels[0]).Max(pair => PixelDifference(pair.First, pair.Second)), Is.LessThanOrEqualTo(3));
            Assert.That(matching.Pixels[0].Zip(mismatch.Pixels[0]).Count(pair => PixelDifference(pair.First, pair.Second) > 6), Is.GreaterThan(24));
            ManyEvidence("resource-selection-id-complete.json", new
            {
                Scope = "Complete untouched official Id1 META/action and original VFX. Only explicit preview Id-filter inputs change; this is a host-input binding diagnostic.",
                action.Path, action.ManifestSha256, Disabled = disabled.Stages, Matching = matching.Stages, Mismatch = mismatch.Stages, Sources = official.Origins.Values, Missing = official.Missing.ToArray()
            });
            Assert.That(official.Missing, Is.Empty);
        }
        finally { graphics.Release(); }
    }

    [TestCase(@"animations\battle\bird04\spellcasting\bi04_spellcast_moving_long_01.anm.meta",
        "fecd4f9fe1f735439b7a6041c2098659b000641ecf2aa458d59a007dba7dda3f", 1)]
    [TestCase(@"animations\battle\humanoid06c\spear\attacks\hu6c_sp_jumping_attack_01.anm.meta",
        "9eb70793910ed91382bcd391edfb6feaabf0da761e73c0bed135d1658250a5b8", 2)]
    public void OfficialAbilityContextPixels_ExplicitPackOverridesReachTheNativeTypeSpecificResource(
        string path, string hash, int effectType)
    {
        var official = ResourceSelectionOfficialFiles();
        var action = LoadSelectionAction(official, path, hash);
        Assert.That(action.Effect.EffectType, Is.EqualTo(effectType));
        var aura = SelectionRead(official, @"vfx\wh_main_ability_melee_slash_circle.xml");
        var weapon = SelectionRead(official, @"vfx\wh_main_ability_melee_slash_arc90.xml");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var result = RenderSelectionContext(official, graphics.GraphicsDevice, action,
                new() { AuraResource = aura, WeaponResource = weapon }, effectType == 1 ? aura : weapon,
                "ability-type" + effectType, [.12f, .2f]);
            Assert.That(result.Pixels.Any(frame => frame.Count(pixel => PixelDifference(pixel, Color.DarkSlateGray) > 3) > 24), Is.True);
            ManyEvidence("resource-selection-type" + effectType + "-diagnostic.json", new
            {
                Scope = "DIAG: complete untouched official META, original animation/skeleton and complete official override VFX. Explicit Aura/Weapon PackFiles are preview inputs; no claim that these resources are the character's real active ability.",
                action.Path, action.ManifestSha256, Type = effectType, result.Stages, Sources = official.Origins.Values, Missing = official.Missing.ToArray()
            });
            Assert.That(official.Missing, Is.Empty);
        }
        finally { graphics.Release(); }
    }

    private ManyOfficialResources ResourceSelectionOfficialFiles()
    {
        var official = CompletenessResources();
        var containers = official.Files.Object.GetAllPackfileContainers();
        if (containers.All(pack => !Path.GetFileName(pack.SystemFilePath).Equals("db.pack", StringComparison.OrdinalIgnoreCase)))
        {
            var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA")!;
            var manifest = File.ReadAllLines(Path.Combine(data, "manifest.txt")).Select(line => line.Split('\t'))
                .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture));
            var path = Path.Combine(data, "db.pack");
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest["db.pack"]));
            var pack = new PackFileContainerLoader(null!).Load(path)!;
            containers.Add(pack);
            foreach (var pair in pack.FileList.Where(pair => pair.Key.StartsWith(@"db\battle_entity_effects_tables\", StringComparison.OrdinalIgnoreCase)
                || pair.Key.StartsWith(@"db\particle_effects_tables\", StringComparison.OrdinalIgnoreCase)))
                official.Index[pair.Key.Replace('/', '\\').ToLowerInvariant()] = (pair.Value, path);
        }
        official.Files.Setup(value => value.GetFileEntriesSnapshot(It.IsAny<PackFileContainer>()))
            .Returns((PackFileContainer pack) => pack.FileList.ToArray());
        official.Origins.Clear(); official.Missing.Clear();
        return official;
    }

    private static PackFile SelectionRead(ManyOfficialResources official, string path) =>
        official.Files.Object.FindFile(path) ?? throw new FileNotFoundException(path);

    private static SelectionAction LoadSelectionAction(ManyOfficialResources official, string path, string hash)
    {
        var file = SelectionRead(official, path);
        var bytes = file.DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(hash));
        var document = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(bytes);
        var effect = document.Attributes.OfType<Effect_v12>().Single();
        Assert.That(document.Attributes.OfType<IEffectMeta>().Count(), Is.EqualTo(1));
        var animationPath = path.Replace(".anm.meta", ".anim", StringComparison.Ordinal);
        var animation = AnimationFile.Create(SelectionRead(official, animationPath));
        var skeletonPath = @"animations\skeletons\" + animation.Header.SkeletonName + ".anim";
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = GameSkeleton.CreateFromAnimationFile(AnimationFile.Create(SelectionRead(official, skeletonPath)), player);
        var clip = new AnimationClip(animation, skeleton);
        player.SetAnimation(clip, skeleton); player.Pause();
        var manifestHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA")!, "manifest.txt"))));
        return new(path, manifestHash, bytes, document, effect, animationPath, skeletonPath, player, skeleton, clip, animation);
    }

    private static SelectionPixelResult RenderSelectionContext(ManyOfficialResources official, GraphicsDevice device,
        SelectionAction action, EffectPreviewContext context, PackFile? expectedResource, string label, float[] ages)
    {
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("complete-official-selection-action"));
        var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(device);
        var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(action.Skeleton);
        var factory = new EffekseerVfxPreviewFactory();
        var store = new VfxPreviewDocumentStore();
        var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!, factory, devices.Object, store)
        { EffectPreviewContext = context };
        var built = builder.Build(null, action.Document, null, root, provider.Object, action.Player, null);
        IVfxPreview? reference = null;
        var rendered = new SelectionPixelResult();
        try
        {
            var instance = built.Instances.Single(value => value is IMetaDataPreview preview && ReferenceEquals(preview.Source, action.Effect));
            var actual = instance as EffectMetaInstance;
            Assert.That(actual != null, Is.EqualTo(expectedResource != null), "Resource selection controls the particle child; the original marker remains either way.");
            if (expectedResource != null)
            {
                Assert.That(actual!.DependsOn(expectedResource), Is.True, "The real Builder must bind the exact selected PackFile identity.");
                using var source = new VfxPreviewDocumentSource(official.Files.Object, expectedResource, store);
                reference = factory.Create(device);
                reference.Reload(source.Read()!);
                var span = action.Effect.EndTime - action.Effect.StartTime;
                reference.EmissionEndTimeSeconds = span > .000001f ? span : Math.Max(0, (float)action.Player.Duration.TotalSeconds - action.Effect.StartTime);
            }
            foreach (var age in ages)
            {
                var time = action.Effect.StartTime + age;
                var sourceAge = Math.Max(0, time - action.Effect.StartTime);
                action.Player.SeekToTimeSeconds(time);
                foreach (var item in built.Instances) item.Update(time);
                var node = actual == null ? null : EffectParticleNode(actual);
                var prepared = node?.PrepareFrame(actual!.WorldTransform) == true;
                var attachment = SelectionReferenceAttachment(action, sourceAge, true);
                reference?.Seek(sourceAge, attachment, past => SelectionReferenceAttachment(action, past, false), true);
                var focus = attachment.Translation;
                var camera = focus + new Vector3(0, 15, 25);
                var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, focus, Vector3.Up),
                    Matrix.CreateOrthographic(32, 32, .1f, 200), camera, focus, 0, 0, 0, 1, Vector3.One, [])
                { VfxGroundHeight = 0, VfxWriteGroundDepth = true };
                var suffix = age.ToString("F3", CultureInfo.InvariantCulture);
                var pixels = DrawSelectionPixels(device, () =>
                {
                    if (prepared) ((IRenderItem)node!).Draw(device, parameters, RenderingTechnique.Normal);
                }, label + "-" + suffix + "-actual");
                var expected = DrawSelectionPixels(device, () => { if (reference is { IsDrained: false }) reference.Draw(parameters); }, label + "-" + suffix + "-reference");
                var maximum = pixels.Zip(expected).Max(pair => PixelDifference(pair.First, pair.Second));
                var changed = pixels.Count(pixel => PixelDifference(pixel, Color.DarkSlateGray) > 3);
                rendered.Pixels.Add(pixels);
                rendered.Stages.Add(new { RequestedAge = age, Age = sourceAge, Time = time, Prepared = prepared, ActualPath = expectedResource == null ? null : official.Files.Object.GetFullPath(expectedResource),
                    MaximumChannelDifference = maximum, ChangedPixels = changed, ParticleCount = node?.ParticleCount ?? 0,
                    PixelSha256 = Convert.ToHexString(SHA256.HashData(pixels.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A }).ToArray())),
                    Diagnostics = node?.Diagnostics.ToArray() ?? [], BuildDiagnostics = built.Diagnostics });
                Assert.That(maximum, Is.LessThanOrEqualTo(3), "Selected-resource pixels must match a separate GPU runtime driven directly by the independently parsed original DB key/context.");
                if (expectedResource == null) Assert.That(changed, Is.Zero);
                Assert.That(node?.Diagnostics.Any(value => value.ReasonKey == "Vfx.Preview.RuntimeUnavailable") ?? false, Is.False);
            }
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            Assert.That(parser.GenerateBytes(2, action.Document), Is.EqualTo(action.Bytes), "Preview context must not rewrite any part of the complete original META.");
            return rendered;
        }
        finally
        {
            reference?.Dispose();
            foreach (var instance in built.Instances) instance.CleanUp();
        }
    }

    private static Matrix SelectionReferenceAttachment(SelectionAction action, float age, bool current)
    {
        var effect = action.Effect;
        var duration = (float)action.Player.Duration.TotalSeconds;
        Matrix bone;
        if (effect.NodeIndex < 0)
            bone = SelectionReferenceRootCompensation(action);
        else if (current && effect.Tracking)
            bone = action.Skeleton.GetAnimatedWorldTranform(effect.NodeIndex);
        else
            bone = AnimationSampler.Sample(Math.Clamp((effect.StartTime + (effect.Tracking ? age : 0)) / duration, 0, 1),
                action.Skeleton, action.Clip, action.Player.AnimationRules).GetSkeletonAnimatedWorld(action.Skeleton, effect.NodeIndex);
        var combined = Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation)) * Matrix.CreateTranslation(effect.Position) * bone;
        var z = combined.Backward;
        var x = Vector3.Normalize(Vector3.Cross(combined.Up, z));
        var y = Vector3.Normalize(Vector3.Cross(z, x));
        z.Normalize();
        var pose = Matrix.Identity;
        pose.Right = x; pose.Up = y; pose.Backward = z;
        pose = Matrix.CreateFromQuaternion(Quaternion.CreateFromRotationMatrix(pose));
        pose.Translation = combined.Translation;
        return Matrix.CreateScale(effect.Scale) * pose;
    }

    private static Matrix SelectionReferenceRootCompensation(SelectionAction action)
    {
        // Tracking with a valid animation/bone array uses the direct actor path even for Node=-1.
        if (action.Effect.Tracking) return Matrix.Identity;
        Assert.That(action.RawAnimation.Header.FlagVariables, Is.Empty);
        Assert.That(Enumerable.Range(0, action.Skeleton.BoneCount).Where(index => action.Skeleton.GetParentBoneIndex(index) == -1),
            Is.EqualTo(new[] { 0 }), "This independent oracle is deliberately bounded to the audited single animroot originals.");
        var bind = action.Skeleton.Rotation[0];
        Assert.That(new Vector3(bind.X, bind.Y, bind.Z).Length(), Is.LessThan(.000001f));
        Assert.That(Math.Abs(bind.W), Is.EqualTo(1).Within(.000001f));
        var frames = action.Clip.DynamicFrames;
        var position = action.Effect.StartTime * action.RawAnimation.Header.FrameRate;
        var index = (int)MathF.Floor(position);
        Assert.That(index, Is.InRange(0, frames.Count - 2), "All selected META births lie inside an ordinary raw key interval, not the rescaled final partial interval.");
        var fraction = position - index;
        var first = frames[index];
        var second = frames[index + 1];
        var rootPosition = Vector3.Lerp(first.Position[0], second.Position[0], fraction);
        var firstHeading = SelectionRawHeading(first.Rotation[0]);
        var secondHeading = SelectionRawHeading(second.Rotation[0]);
        var heading = unchecked((ushort)(firstHeading + (int)MathF.Round(SelectionSignedAngle(secondHeading - firstHeading) * fraction,
            MidpointRounding.AwayFromZero)));
        // Ordinary raw-file flags are zero: neither bind position nor first-frame position is subtracted.
        return Matrix.CreateTranslation(-rootPosition) * Matrix.CreateRotationY(-SelectionSignedAngle(heading) * 9.58738019107841e-05f);
    }

    private static ushort SelectionRawHeading(Quaternion value)
    {
        const float angleUnits = 10430.3779296875f;
        var a = (int)MathF.Round(MathF.Atan2(2 * value.X * value.Z + 2 * value.W * value.Y,
            1 - 2 * value.X * value.X - 2 * value.Y * value.Y) * angleUnits, MidpointRounding.AwayFromZero);
        var b = (int)MathF.Round(MathF.Atan2(2 * value.X * value.Y - 2 * value.W * value.Z,
            2 * value.Y * value.Z + 2 * value.W * value.X) * angleUnits, MidpointRounding.AwayFromZero);
        var u = 2 * value.Y * value.Z - 2 * value.W * value.X;
        if (u >= 0) b -= 32768;
        var blend = Math.Clamp((Math.Abs(u) - .4999999701976776f) * 2.062673330307007f, 0, 1);
        return unchecked((ushort)(a + (int)MathF.Round(SelectionSignedAngle(b - a) * blend, MidpointRounding.AwayFromZero)));
    }

    private static int SelectionSignedAngle(int difference)
    {
        var wrapped = unchecked((ushort)difference);
        return wrapped <= 32768 ? wrapped : wrapped - 65536;
    }

    private static Color[] DrawSelectionPixels(GraphicsDevice device, Action draw, string label)
    {
        const int size = 448;
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkSlateGray, 1, 0);
        draw();
        device.SetRenderTarget(null);
        var pixels = new Color[size * size]; target.GetData(pixels);
        var folder = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
            using var stream = new FileStream(Path.Combine(folder, "resource-selection-" + label + ".png"), FileMode.CreateNew);
            target.SaveAsPng(stream, size, size);
        }
        return pixels;
    }

    private static Dictionary<string, string?[]> ReadTerrainPixelOracle(byte[] bytes)
    {
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo("d50900931e333578164c3c57fee9477aa82492d1d956d5292f44e35e88bd67b5"));
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        var count = ReadSelectionDbHeader(reader, 2);
        var result = new Dictionary<string, string?[]>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var deep = ReadSelectionOptionalString(reader); var forest = ReadSelectionOptionalString(reader);
            var grass = ReadSelectionOptionalString(reader); var mud = ReadSelectionOptionalString(reader);
            var key = ReadSelectionSizedString(reader);
            var rock = ReadSelectionOptionalString(reader); var road = ReadSelectionOptionalString(reader);
            var sand = ReadSelectionOptionalString(reader); var scrub = ReadSelectionOptionalString(reader);
            var shallow = ReadSelectionOptionalString(reader); var snow = ReadSelectionOptionalString(reader);
            var wood = ReadSelectionOptionalString(reader);
            reader.ReadSingle(); ReadSelectionOptionalString(reader);
            result.Add(key, [forest, grass, mud, sand, scrub, rock, deep, shallow, road, wood, snow]);
        }
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        return result;
    }

    private static HashSet<string> ReadParticlePixelOracle(byte[] bytes)
    {
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo("2f6a4499dd4f1f10c8b2dbdbb3d1f7b2780b862f0b5d070d7b9e1cabb88ec47b"));
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        var count = ReadSelectionDbHeader(reader, 0);
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++) result.Add(ReadSelectionSizedString(reader));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        return result;
    }

    private static uint ReadSelectionDbHeader(BinaryReader reader, int version)
    {
        var marker = reader.ReadUInt32();
        if (marker == 0xfffcfefd)
        {
            reader.ReadBytes(reader.ReadUInt16() * 2);
            marker = reader.ReadUInt32();
        }
        if (marker == 0xfffefdfc)
            Assert.That(reader.ReadInt32(), Is.EqualTo(version));
        else
        {
            reader.BaseStream.Position -= 4;
            Assert.That(version, Is.Zero);
        }
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        return reader.ReadUInt32();
    }

    private static string? ReadSelectionOptionalString(BinaryReader reader) => reader.ReadBoolean() ? ReadSelectionSizedString(reader) : null;
    private static string ReadSelectionSizedString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
    private sealed record SelectionAction(string Path, string ManifestSha256, byte[] Bytes, ParsedMetadataFile Document, Effect_v12 Effect,
        string AnimationPath, string SkeletonPath, AnimationPlayer Player, GameSkeleton Skeleton, AnimationClip Clip, AnimationFile RawAnimation);
    private sealed class SelectionPixelResult
    {
        internal List<Color[]> Pixels { get; } = [];
        internal List<object> Stages { get; } = [];
    }
}
