using System.IO;
using System.Text;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    private static IEnumerable<TestCaseData> TerrainContextCases()
    {
        foreach (var version in new[] { 11, 12 })
            for (var terrain = 0; terrain < 16; terrain++)
                yield return new TestCaseData(version, terrain);
    }

    [TestCaseSource(nameof(TerrainContextCases))]
    public void EffectTerrainContext_AllRawCategoriesReachTheCorrectBoundVfxDocument(int version, int terrain)
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("official-fixture");
        var keys = Enumerable.Range(0, 11).Select(index => "terrain_" + index).ToArray();
        fixture.SetTables(pack, "step", keys, keys);
        var files = keys.Select(key => fixture.AddVfx(pack, key, key)).ToArray();
        var source = TerrainEffect(version);
        var rawData = source.Data;
        var builder = fixture.CreateBuilder(out var factory);
        builder.EffectPreviewContext = new() { TerrainType = terrain };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            instance.Update(1);
            var marker = root.Children.Single();
            var particles = (VfxPreviewSceneNode)marker.Children.Single();
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
            var category = terrain switch { 11 => 5, 12 or 14 => 1, 13 => 2, 15 => 0, _ => terrain };
            Assert.Multiple(() =>
            {
                Assert.That(factory.Created.Single().Snapshot!.SourcePath, Is.EqualTo($@"vfx\{keys[category]}.xml"));
                Assert.That(factory.Created.Single().Snapshot!.GameBytes, Is.EqualTo(files[category].DataSource.ReadData()));
                Assert.That(result.Diagnostics.Any(value => value.ReasonKey.StartsWith("SuperView.Diagnostics.EffectTerrain", StringComparison.Ordinal)
                    || value.ReasonKey == "SuperView.Diagnostics.MissingEffect"), Is.False);
                Assert.That(((IEffectMeta)source).VfxName, Is.EqualTo("step_grass"));
                Assert.That(source.Data, Is.SameAs(rawData));
            });
            fixture.Files.Verify(value => value.FindFile(@"vfx\step_grass.xml", null), Times.Never);
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void EffectTerrainContext_BuilderTerrainSelectionTakesPriorityOverProvidedAbilityResources(int type)
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("fixture");
        fixture.SetTables(pack, "step", TerrainKeys(1, "ground"), ["ground"]);
        var ground = fixture.AddVfx(pack, "ground", "terrain");
        var aura = fixture.AddVfx(pack, "aura", "aura");
        var weapon = fixture.AddVfx(pack, "weapon", "weapon");
        var source = (Effect_v12)TerrainEffect(12);
        source.EffectType = type;
        var builder = fixture.CreateBuilder(out var factory);
        builder.EffectPreviewContext = new() { TerrainType = 1, AuraResource = aura, WeaponResource = weapon };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            result.Instances.Single().Update(1);
            var marker = root.Children.Single();
            Assert.That(((VfxPreviewSceneNode)marker.Children.Single()).PrepareFrame(marker.ModelMatrix), Is.True);
            Assert.That(factory.Created.Single().Snapshot!.GameBytes, Is.EqualTo(ground.DataSource.ReadData()));
            Assert.That(source.EffectType, Is.EqualTo(type));
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void EffectTerrainContext_MissingTerrainSlotKeepsOnlyTheMarkerEvenWhenBaseAndOverridesExist(int type)
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("fixture");
        fixture.SetTables(pack, "step", TerrainKeys(1, null), []);
        var fallback = fixture.AddVfx(pack, "step_grass", "authored");
        var source = (Effect_v12)TerrainEffect(12);
        source.EffectType = type;
        var builder = fixture.CreateBuilder(out var factory);
        builder.EffectPreviewContext = new() { TerrainType = 1, AuraResource = fallback, WeaponResource = fallback };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            result.Instances.Single().Update(1);
            Assert.Multiple(() =>
            {
                Assert.That(root.Children.Single().Children, Is.Empty);
                Assert.That(factory.Created, Is.Empty);
                Assert.That(result.Diagnostics.Any(value => value.ReasonKey == "SuperView.Diagnostics.MissingEffect"), Is.False);
            });
            fixture.Files.Verify(value => value.FindFile(@"vfx\step_grass.xml", null), Times.Never);
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [TestCase(-1)]
    [TestCase(16)]
    public void EffectTerrainResolver_UnknownTerrainSkipsDatabaseAndFallbackLookup(int terrain)
    {
        var fixture = new TerrainResourceFixture();
        var source = (Effect_v12)TerrainEffect(12);
        source.EffectType = 1;
        var diagnostics = new List<MetaDataBuildDiagnostic>();
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Persistent,
            new() { TerrainType = terrain, AuraResource = PackFile.CreateFromBytes("override.xml", []) }, diagnostics), Is.Null);
        Assert.That(diagnostics, Is.Empty);
        fixture.Files.Verify(value => value.GetAllPackfileContainers(), Times.Never);
        fixture.Files.Verify(value => value.FindFile(It.IsAny<string>(), null), Times.Never);
    }

    [TestCase("terrain-table", "EffectTerrainTableMissing")]
    [TestCase("particle-table", "EffectTerrainTableMissing")]
    [TestCase("terrain-key", "EffectTerrainKeyMissing")]
    [TestCase("particle-key", "EffectTerrainParticleMissing")]
    [TestCase("bad-terrain-table", "EffectTerrainTableInvalid")]
    [TestCase("bad-particle-table", "EffectTerrainTableInvalid")]
    public void EffectTerrainResolver_MissingOrInvalidDbReturnsAttributedDiagnosticWithoutFallback(string failure, string reason)
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("fixture");
        fixture.SetTables(pack, failure == "terrain-key" ? "unrelated" : "step", TerrainKeys(1, "ground"),
            failure == "particle-key" ? ["unrelated"] : ["ground"]);
        fixture.AddVfx(pack, "step_grass", "authored");
        fixture.AddVfx(pack, "ground", "ground");
        if (failure == "terrain-table") pack.FileList.Remove(EffectMetaResourceResolver.TerrainTablePath + "data__");
        if (failure == "particle-table") pack.FileList.Remove(EffectMetaResourceResolver.ParticleTablePath + "data__");
        if (failure == "bad-terrain-table") fixture.Put(pack, EffectMetaResourceResolver.TerrainTablePath + "data__", [1, 2, 3]);
        if (failure == "bad-particle-table") fixture.Put(pack, EffectMetaResourceResolver.ParticleTablePath + "data__", [1, 2, 3]);
        var source = (Effect_v12)TerrainEffect(12);
        var diagnostics = new List<MetaDataBuildDiagnostic>();
        var resolved = fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Persistent, new(), diagnostics);
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Null);
            Assert.That(diagnostics, Has.Count.EqualTo(1));
            Assert.That(diagnostics.Single().ReasonKey, Is.EqualTo("SuperView.Diagnostics." + reason));
            Assert.That(diagnostics.Single().Source, Is.SameAs(source));
            Assert.That(diagnostics.Single().Owner, Is.EqualTo(MetaDataDocumentOwner.Persistent));
            Assert.That(diagnostics.Single().ResourcePath, Does.StartWith(failure.Contains("particle", StringComparison.Ordinal)
                ? EffectMetaResourceResolver.ParticleTablePath : EffectMetaResourceResolver.TerrainTablePath));
        });
        fixture.Files.Verify(value => value.FindFile(@"vfx\step_grass.xml", null), Times.Never);
    }

    [Test]
    public void EffectTerrainResolver_LaterPackReplacesTheSameDbPathBeforeDecodeAndPreservesSelectedVfxIdentity()
    {
        var fixture = new TerrainResourceFixture();
        var first = fixture.AddPack("first");
        var last = fixture.AddPack("last");
        // The replaced invalid source must never be decoded or poison the effective table.
        fixture.Put(first, EffectMetaResourceResolver.TerrainTablePath + "data__", [1]);
        fixture.Put(first, EffectMetaResourceResolver.ParticleTablePath + "data__", [2]);
        fixture.SetTables(last, "step", TerrainKeys(1, "ground"), ["ground"]);
        var original = fixture.AddVfx(first, "ground", "first");
        var replacement = fixture.AddVfx(last, "ground", "last");
        var source = (Effect_v12)TerrainEffect(12);
        var diagnostics = new List<MetaDataBuildDiagnostic>();
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Animation, new(), diagnostics), Is.SameAs(replacement));
        Assert.That(replacement, Is.Not.SameAs(original));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void EffectTerrainResolver_LaterPackRowOverridesEarlierDbChunkRegardlessOfChunkName()
    {
        var fixture = new TerrainResourceFixture();
        var first = fixture.AddPack("first");
        var last = fixture.AddPack("last");
        fixture.SetTables(first, "step", TerrainKeys(1, "old"), ["old"], "z_chunk");
        fixture.SetTables(last, "step", TerrainKeys(1, "new"), ["new"], "a_chunk");
        fixture.AddVfx(first, "old", "old");
        var selected = fixture.AddVfx(last, "new", "new");
        var source = (Effect_v12)TerrainEffect(12);
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Animation, new(), []), Is.SameAs(selected));
    }

    [Test]
    public void EffectTerrainResolver_InvalidateReplacesCachedRowsAfterAResourceUpdate()
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("fixture");
        fixture.SetTables(pack, "step", TerrainKeys(1, "old"), ["old"]);
        var old = fixture.AddVfx(pack, "old", "old");
        var updated = fixture.AddVfx(pack, "new", "new");
        var source = (Effect_v12)TerrainEffect(12);
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Animation, new(), []), Is.SameAs(old));
        fixture.SetTables(pack, "step", TerrainKeys(1, "new"), ["new"]);
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Animation, new(), []), Is.SameAs(old));
        fixture.Resolver.Invalidate();
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Animation, new(), []), Is.SameAs(updated));
    }

    [Test]
    public void EffectTerrainContext_BuilderInvalidationRefreshesTheIncrementalPreviewDocument()
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("fixture");
        fixture.SetTables(pack, "step", TerrainKeys(1, "old"), ["old"]);
        fixture.AddVfx(pack, "old", "old");
        var updated = fixture.AddVfx(pack, "new", "new");
        var source = TerrainEffect(12);
        var builder = fixture.CreateBuilder(out var factory);
        builder.EffectPreviewContext = new() { TerrainType = 1 };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var first = builder.BuildPreview(source, MetaDataDocumentOwner.Animation, false, root, null!, new AnimationPlayer());
        try
        {
            first.Instance!.Update(1);
            var marker = root.Children.Single();
            Assert.That(((VfxPreviewSceneNode)marker.Children.Single()).PrepareFrame(marker.ModelMatrix), Is.True);
            fixture.SetTables(pack, "step", TerrainKeys(1, "new"), ["new"]);
            builder.InvalidateEffectPreviewResources();
            first.Instance.CleanUp();
            var replacement = builder.BuildPreview(source, MetaDataDocumentOwner.Animation, false, root, null!, new AnimationPlayer());
            try
            {
                replacement.Instance!.Update(1);
                marker = root.Children.Single();
                Assert.That(((VfxPreviewSceneNode)marker.Children.Single()).PrepareFrame(marker.ModelMatrix), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(factory.Created, Has.Count.EqualTo(2));
                    Assert.That(factory.Created[0].Disposed, Is.True);
                    Assert.That(factory.Created[1].Snapshot!.SourcePath, Is.EqualTo(@"vfx\new.xml"));
                    Assert.That(factory.Created[1].Snapshot!.GameBytes, Is.EqualTo(updated.DataSource.ReadData()));
                    Assert.That(((IEffectMeta)source).VfxName, Is.EqualTo("step_grass"));
                });
            }
            finally { replacement.Instance?.CleanUp(); }
        }
        finally { first.Instance?.CleanUp(); }
    }

    [Test]
    public void EffectTerrainResolver_MissingSelectedVfxIsReportedAtItsExactPathWithoutAuthoredFallback()
    {
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("fixture");
        fixture.SetTables(pack, "step", TerrainKeys(1, "missing_ground"), ["missing_ground"]);
        fixture.AddVfx(pack, "step_grass", "authored");
        var source = (Effect_v12)TerrainEffect(12);
        var diagnostics = new List<MetaDataBuildDiagnostic>();
        Assert.That(fixture.Resolver.Resolve(source, source, MetaDataDocumentOwner.Animation, new(), diagnostics), Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Single().ReasonKey, Is.EqualTo("SuperView.Diagnostics.MissingEffect"));
            Assert.That(diagnostics.Single().ResourcePath, Is.EqualTo(@"vfx\missing_ground.xml"));
        });
        fixture.Files.Verify(value => value.FindFile(@"vfx\step_grass.xml", null), Times.Never);
    }

    private static ParsedMetadataAttribute TerrainEffect(int version) => version switch
    {
        11 => new Effect_v11 { Name = "EFFECT", Version = 11, Data = [4, 5], VfxName = "step_grass", TerrainMutable = true,
            EndTime = 3, Scale = 1, NodeIndex = -1, Orientation = new Vector4(0, 0, 0, 1) },
        12 => new Effect_v12 { Name = "EFFECT", Version = 12, Data = [4, 5], VfxName = "step_grass", TerrainMutable = true,
            EndTime = 3, Scale = 1, NodeIndex = -1, Orientation = new Vector4(0, 0, 0, 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    private static string?[] TerrainKeys(int category, string? key)
    {
        var keys = new string?[11];
        keys[category] = key;
        return keys;
    }

    private sealed class TerrainResourceFixture
    {
        internal readonly Mock<IPackFileService> Files = new();
        internal readonly List<PackFileContainer> Containers = [];
        internal readonly EffectMetaResourceResolver Resolver;

        internal TerrainResourceFixture()
        {
            Files.Setup(value => value.GetAllPackfileContainers()).Returns(Containers);
            Files.Setup(value => value.GetFileEntriesSnapshot(It.IsAny<PackFileContainer>()))
                .Returns((PackFileContainer pack) => pack.FileList.ToArray());
            Files.Setup(value => value.FindFile(It.IsAny<string>(), It.IsAny<PackFileContainer?>()))
                .Returns((string path, PackFileContainer? pack) =>
                {
                    path = path.Replace('/', '\\');
                    return pack != null ? pack.FileList.GetValueOrDefault(path)
                        : Containers.AsEnumerable().Reverse().Select(container => container.FileList.GetValueOrDefault(path)).FirstOrDefault(file => file != null);
                });
            Files.Setup(value => value.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()))
                .Returns((PackFile file, PackFileContainer? _) => Containers.SelectMany(pack => pack.FileList)
                    .First(pair => ReferenceEquals(pair.Value, file)).Key);
            Files.Setup(value => value.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns((PackFile file) => Containers.LastOrDefault(pack => pack.FileList.Values.Any(value => ReferenceEquals(file, value))));
            Resolver = new(new MetaDataResourceResolver(Files.Object));
        }

        internal PackFileContainer AddPack(string name)
        {
            var pack = new PackFileContainer(name) { FileList = new(StringComparer.OrdinalIgnoreCase) };
            Containers.Add(pack);
            return pack;
        }

        internal PackFile Put(PackFileContainer pack, string path, byte[] bytes)
        {
            var file = PackFile.CreateFromBytes(path, bytes);
            pack.FileList[path] = file;
            return file;
        }

        internal PackFile AddVfx(PackFileContainer pack, string key, string fingerprint) =>
            Put(pack, $@"vfx\{key}.xml", Encoding.UTF8.GetBytes($"<vfx revision=\"2\"><emitters/></vfx><!--{fingerprint}-->"));

        internal MetaDataBuilder CreateBuilder(out CapturingFactory factory)
        {
            factory = new CapturingFactory();
            return new MetaDataBuilder(null!, null!, null!, Files.Object, null!, factory, Mock.Of<IDeviceResolver>(), new VfxPreviewDocumentStore());
        }

        internal void SetTables(PackFileContainer pack, string name, IReadOnlyList<string?> keys, IReadOnlyList<string> particles, string chunk = "data__")
        {
            Put(pack, EffectMetaResourceResolver.TerrainTablePath + chunk, Database(2, writer =>
            {
                foreach (var category in new[] { 6, 0, 1, 2 }) OptionalString(writer, keys[category]);
                SizedString(writer, name);
                foreach (var category in new[] { 5, 8, 3, 4, 7, 10, 9 }) OptionalString(writer, keys[category]);
                writer.Write(1f);
                OptionalString(writer, null);
            }, 1));
            Put(pack, EffectMetaResourceResolver.ParticleTablePath + chunk,
                Database(0, writer => { foreach (var key in particles) SizedString(writer, key); }, particles.Count));
        }

        private static byte[] Database(int version, Action<BinaryWriter> body, int count)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            if (version != 0)
            {
                writer.Write(new byte[] { 0xfc, 0xfd, 0xfe, 0xff });
                writer.Write(version);
            }
            writer.Write(true);
            writer.Write(count);
            body(writer);
            return stream.ToArray();
        }

        private static void OptionalString(BinaryWriter writer, string? value)
        {
            writer.Write(value != null);
            if (value != null) SizedString(writer, value);
        }

        private static void SizedString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }
}
