using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
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
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    private ManyOfficialResources? _manyOfficial;

    [TestCase(@"animations\battle\bird01\rebirth\bi1_rebirth_01.anm.meta", 41, 4.796655f)]
    [TestCase(@"animations\battle\bird04\attacks\bi04_attack_01.anm.meta", 16, 2.4923406f)]
    public void CompleteOfficialManyEffectMeta_PreservesEveryEntryAndLoadsMoreThanTwoAtItsAuthoredStage(string path, int expectedCount, float time)
    {
        var official = _manyOfficial ??= new ManyOfficialResources();
        official.Origins.Clear(); official.Missing.Clear();
        var original = official.Index[path];
        var bytes = original.File.DataSource.ReadData();
        var parsed = new MetaDataFileParser(new MetaDataDatabase()).ParseFile(bytes);
        var authored = parsed.Attributes.OfType<IEffectMeta>().ToArray();
        Assert.That(authored, Has.Length.EqualTo(expectedCount));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(graphics.GraphicsDevice);
            var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!, new EffekseerVfxPreviewFactory(), devices.Object, new());
            var root = new GroupNode("complete-official-metadata");
            var result = builder.Build(null, parsed, null, root, null!, new AnimationPlayer(), null);
            try
            {
                var effects = result.Instances.OfType<EffectMetaInstance>().ToArray();
                Assert.That(effects, Has.Length.EqualTo(expectedCount), "Every original EFFECT must have its own runtime, not just the original two example names.");
                Assert.That(effects.Select(effect => effect.Source).Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(expectedCount));
                Assert.That(effects.Select(effect => effect.Source), Is.EquivalentTo(authored.Cast<ParsedMetadataAttribute>()));
                var nodes = effects.Select(effect => (VfxPreviewSceneNode)typeof(EffectMetaInstance)
                    .GetField("_particles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(effect)!).ToArray();
                foreach (var effect in effects) effect.Update(time);
                var prepared = nodes.Select(node => node.Parent.IsVisible && node.PrepareFrame(node.Parent.ModelMatrix)).ToArray();
                foreach (var effect in effects) effect.Update(time);
                var active = nodes.Where((node, index) => prepared[index] && node.Parent.IsVisible).ToArray();
                var label = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
                var baseline = RenderManyOfficial(graphics.GraphicsDevice, [], label + "-baseline", new(0, 90, 150), 80);
                var pixels = RenderManyOfficial(graphics.GraphicsDevice, active, label + "-complete", new(0, 90, 150), 80);
                var state = effects.Select((effect, index) => new
                {
                    Index = index, ((IEffectMeta)effect.Source).VfxName,
                    Start = ((IEffectMeta)effect.Source).EffectStartTime, End = ((IEffectMeta)effect.Source).EffectEndTime,
                    ParentVisible = nodes[index].Parent.IsVisible, Prepared = prepared[index], nodes[index].ParticleCount,
                    nodes[index].TimeSeconds, Scale = nodes[index].ModelMatrix.Right.Length(),
                    Diagnostics = nodes[index].Diagnostics.Select(item => new { item.Layer, item.Field, item.ReasonKey }).ToArray()
                }).ToArray();
                ManyEvidence(label + "-complete.json", new { Path = path, original.Pack, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    OriginalCount = expectedCount, RuntimeCount = effects.Length, ActiveRuntimeCount = active.Length, Time = time,
                    Entries = state, Sources = official.Origins.Values, LookupMisses = official.Missing.ToArray(),
                    Scope = "Complete unmodified official META loaded through the real builder, original positions/timing/scales/VFX/dependencies preserved. Static attachment fallback without a skeleton/animation pose; no claim about native bone motion or visual equivalence. Separate 16-instance diagnostics test each visible contribution." });
                Assert.Multiple(() =>
                {
                    Assert.That(active.Length, Is.GreaterThan(2), "More than two real official entries must reach drawing at the authored overlapping stage.");
                    Assert.That(pixels.Zip(baseline).Count(pair => PixelDifference(pair.First, pair.Second) > 3), Is.GreaterThan(20));
                    Assert.That(active.SelectMany(node => node.Diagnostics).Any(item => item.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
                    Assert.That(active.SelectMany(node => node.Diagnostics).Any(item => item.ReasonKey == "Vfx.Preview.MaterialUnsupported"
                        && item.Field.Contains("wh_vfx_mesh_vertex_anim_battle", StringComparison.OrdinalIgnoreCase)), Is.False,
                        "An active runtime containing only lightning must not hide the missing primary vertex-animated Beam.");
                    Assert.That(original.File.DataSource.ReadData(), Is.EqualTo(bytes));
                });
            }
            finally { foreach (var instance in result.Instances) instance.CleanUp(); }
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SixteenOfficialEffectInstances_EachContributesBeyondTheFirstTwo(bool mixed)
    {
        var official = _manyOfficial ??= new ManyOfficialResources();
        official.Origins.Clear(); official.Missing.Clear();
        var names = new[] { "wh_dlc08_lib_pinning_icicles", "wh_main_cinematic_smoke_blast",
            "wh3_main_entity_storm_dragon_attack_impact", "wh3_main_entity_storm_mage_lightning_charge" };
        var ages = new[] { .2f, .2f, .3f, .8774f };
        var camera = new Vector3(0, 90, 150);
        var up = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(-camera), Vector3.Right));
        var entries = Enumerable.Range(0, 16).Select(index =>
        {
            var kind = mixed ? index % names.Length : 0;
            return new Effect_v11 { Name = "EFFECT", Version = 11, VfxName = names[kind],
                StartTime = 1 - ages[kind], EndTime = 2,
                Position = Vector3.Right * ((index % 4 - 1.5f) * 55) + up * ((1.5f - index / 4) * 55) };
        }).ToArray();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(graphics.GraphicsDevice);
            var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!, new EffekseerVfxPreviewFactory(), devices.Object, new());
            var root = new GroupNode("sixteen-official-effect-diagnostic");
            var result = builder.Build(new ParsedMetadataFile { Version = 10, Attributes = entries.Take(8).Cast<ParsedMetadataAttribute>().ToList() },
                new ParsedMetadataFile { Version = 10, Attributes = entries.Skip(8).Cast<ParsedMetadataAttribute>().ToList() },
                null, root, null!, new AnimationPlayer(), null);
            try
            {
                Assert.That(result.Instances, Has.Count.EqualTo(16));
                Assert.That(result.Instances.All(instance => instance is EffectMetaInstance), Is.True);
                foreach (var instance in result.Instances) instance.Update(1);
                var nodes = root.Children.Select(marker => (VfxPreviewSceneNode)marker.Children.Single()).ToArray();
                foreach (var node in nodes)
                    Assert.That(node.PrepareFrame(node.Parent.ModelMatrix), Is.True,
                        string.Join(',', node.Diagnostics.Select(item => item.ReasonKey)));
                var label = mixed ? "mixed-sixteen" : "same-sixteen";
                var baseline = RenderManyOfficial(graphics.GraphicsDevice, [], label + "-baseline", camera);
                var combined = RenderManyOfficial(graphics.GraphicsDevice, nodes, label + "-combined", camera);
                var reversed = RenderManyOfficial(graphics.GraphicsDevice, nodes.Reverse().ToArray(), label + "-reversed", camera);
                var signals = new List<object>();
                for (var index = 0; index < nodes.Length; ++index)
                {
                    var alone = RenderManyOfficial(graphics.GraphicsDevice, [nodes[index]], label + $"-alone-{index:D2}", camera);
                    var omitted = RenderManyOfficial(graphics.GraphicsDevice, nodes.Where((_, other) => other != index).ToArray(), label + $"-omit-{index:D2}", camera);
                    var soloMask = Enumerable.Range(0, alone.Length).Where(pixel => PixelDifference(alone[pixel], baseline[pixel]) > 3).ToArray();
                    var contribution = soloMask.Count(pixel => PixelDifference(combined[pixel], omitted[pixel]) > 3);
                    var reverseSignal = soloMask.Count(pixel => PixelDifference(reversed[pixel], baseline[pixel]) > 3);
                    Assert.Multiple(() =>
                    {
                        Assert.That(soloMask, Has.Length.GreaterThan(6), $"Official Effect {index} must be visible alone.");
                        Assert.That(contribution, Is.GreaterThan(6), $"Official Effect {index} must contribute with all 16 runtimes alive.");
                        Assert.That(reverseSignal, Is.GreaterThan(6), $"Official Effect {index} must remain visible when submission order is reversed.");
                        Assert.That(nodes[index].Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
                    });
                    signals.Add(new { Index = index, entries[index].VfxName, Solo = soloMask.Length, Contribution = contribution, Reversed = reverseSignal });
                }
                ManyEvidence(label + ".json", new { Scope = "DIAG: sixteen independent META instances across both owners, using complete original official VFX and dependencies. Only metadata positions and timing are arranged for distinct screen regions. Not an untouched official META or native visual acceptance.",
                    Signals = signals, Sources = official.Origins.Values, LookupMisses = official.Missing.ToArray() });
            }
            finally { foreach (var instance in result.Instances) instance.CleanUp(); }
        }
        finally { graphics.Release(); }
    }

    private static Color[] RenderManyOfficial(GraphicsDevice device, IReadOnlyList<VfxPreviewSceneNode> nodes, string label, Vector3 camera, float extent = 240)
    {
        const int size = 768;
        using var surface = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(surface); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkSlateGray, 1, 0);
        var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(extent, extent, .1f, 400), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        foreach (var node in nodes) ((IRenderItem)node).Draw(device, parameters, RenderingTechnique.Normal);
        device.SetRenderTarget(null);
        var pixels = new Color[size * size]; surface.GetData(pixels);
        var folder = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
            using var stream = new FileStream(Path.Combine(folder, label + ".png"), FileMode.CreateNew);
            surface.SaveAsPng(stream, size, size);
        }
        return pixels;
    }

    private static void ManyEvidence(string name, object value)
    {
        var folder = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        using var stream = new FileStream(Path.Combine(folder, name), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class ManyOfficialResources
    {
        public Mock<IPackFileService> Files { get; } = new();
        public Dictionary<string, object> Origins { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Missing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (PackFile File, string Pack)> Index { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ManyOfficialResources()
        {
            if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit offscreen opt-in required.");
            var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
            if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original game data path required.");
            var manifestPath = Path.Combine(data!, "manifest.txt");
            var manifest = File.ReadAllLines(manifestPath).Select(line => line.Split('\t')).Where(parts => parts.Length > 1
                && parts[0].EndsWith(".pack", StringComparison.OrdinalIgnoreCase)).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture));
            var loader = new PackFileContainerLoader(null!);
            var packs = manifest.Keys.Where(name => name.StartsWith("anim", StringComparison.OrdinalIgnoreCase)
                || new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" }.Contains(name))
                .Select(name =>
                {
                    Assert.That(Path.GetFileName(name), Is.EqualTo(name));
                    var path = Path.Combine(data!, name);
                    Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
                    return loader.Load(path)!;
                }).OrderBy(pack => pack.Header.LoadOrder).ThenBy(pack => pack.Name).ToArray();
            foreach (var pack in packs)
                foreach (var entry in pack.FileList) Index[entry.Key.Replace('/', '\\').ToLowerInvariant()] = (entry.Value, pack.SystemFilePath);
            Files.Setup(value => value.GetAllPackfileContainers()).Returns(packs.ToList());
            Files.Setup(value => value.FindFile(It.IsAny<string>(), null)).Returns((string key, PackFileContainer? _) =>
            {
                var normalized = key.Replace('/', '\\').ToLowerInvariant();
                if (!Index.TryGetValue(normalized, out var entry)) { Missing.Add(normalized); return null; }
                Origins[normalized] = new { Path = normalized, SourcePack = entry.Pack, Sha256 = Convert.ToHexString(SHA256.HashData(entry.File.DataSource.ReadData())) };
                return entry.File;
            });
            Files.Setup(value => value.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()))
                .Returns((PackFile file, PackFileContainer? _) => Index.Single(entry => ReferenceEquals(entry.Value.File, file)).Key);
            TestContext.Progress.WriteLine("Official manifest SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))));
        }
    }
}
