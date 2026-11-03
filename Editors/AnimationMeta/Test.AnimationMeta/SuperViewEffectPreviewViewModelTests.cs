using System.Text;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class SuperViewEffectPreviewViewModelTests
{
    [SetUp]
    public void RequireOffscreenDeviceOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 for the hidden-device production SuperView host. No window or input is used.");
    }

    [Test]
    public void CancelledResourceSelection_PreservesExistingContextAndEveryPreview()
    {
        using var fixture = new PreviewFixture();
        fixture.SelectAbilityResource(aura: true);
        var before = fixture.Previews();
        var context = fixture.Builder.EffectPreviewContext;
        var auraPath = fixture.Editor.PreviewAuraResource;
        BrowseDialogFilter? filter = null;
        fixture.Runner.Dialogs.Setup(dialogs => dialogs.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<BrowseDialogFilter>()))
            .Callback<List<string>, BrowseDialogFilter>((extensions, value) =>
            {
                Assert.That(extensions, Is.EqualTo(new[] { ".xml" }));
                filter = value;
            })
            .Returns(new BrowseDialogResultFile(false, fixture.WeaponFile));

        fixture.Editor.BrowseAuraResourceCommand.Execute(null);
        fixture.Editor.BrowseWeaponResourceCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter!.Matches(fixture.AuraFile), Is.True);
            Assert.That(filter.Matches(fixture.WeaponFile), Is.True);
            Assert.That(filter.Matches(fixture.UnrelatedFile), Is.False);
            Assert.That(fixture.Editor.PreviewAuraResource, Is.EqualTo(auraPath));
            Assert.That(fixture.Editor.PreviewWeaponResource, Is.Empty);
            Assert.That(fixture.Builder.EffectPreviewContext, Is.SameAs(context));
        });
        fixture.AssertOnlyRebuilt(before);
        fixture.AssertDocumentAndClockUnchanged();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SelectAndClearAbilityResource_RebuildsOnlyItsNonTerrainType(bool aura)
    {
        using var fixture = new PreviewFixture();
        var affected = aura ? fixture.Aura : fixture.Weapon;
        var before = fixture.Previews();
        Assert.That(fixture.Preview(affected), Is.TypeOf<DrawableMetaInstance>());
        fixture.SelectAbilityResource(aura);

        fixture.AssertOnlyRebuilt(before, affected);
        Assert.That(fixture.Preview(affected), Is.TypeOf<EffectMetaInstance>());
        Assert.That(aura ? fixture.Builder.EffectPreviewContext.AuraResource : fixture.Builder.EffectPreviewContext.WeaponResource,
            Is.SameAs(aura ? fixture.AuraFile : fixture.WeaponFile));
        Assert.That(aura ? fixture.Editor.PreviewAuraResource : fixture.Editor.PreviewWeaponResource,
            Is.EqualTo(fixture.Runner.PackFileService.GetFullPath(aura ? fixture.AuraFile : fixture.WeaponFile)));
        fixture.AssertDocumentAndClockUnchanged();

        before = fixture.Previews();
        var clear = aura ? fixture.Editor.ClearAuraResourceCommand : fixture.Editor.ClearWeaponResourceCommand;
        clear.Execute(null);
        fixture.AssertOnlyRebuilt(before, affected);
        Assert.That(fixture.Preview(affected), Is.TypeOf<DrawableMetaInstance>());
        Assert.That(aura ? fixture.Builder.EffectPreviewContext.AuraResource : fixture.Builder.EffectPreviewContext.WeaponResource, Is.Null);
        Assert.That(aura ? fixture.Editor.PreviewAuraResource : fixture.Editor.PreviewWeaponResource, Is.Empty);
        before = fixture.Previews();
        var context = fixture.Builder.EffectPreviewContext;
        clear.Execute(null);
        fixture.AssertOnlyRebuilt(before);
        Assert.That(fixture.Builder.EffectPreviewContext, Is.SameAs(context), "Clearing an empty slot must not reset playback resources.");
        fixture.AssertDocumentAndClockUnchanged();
    }

    [Test]
    public void TerrainChange_RebuildsTheMarkerOnlyMissingResourceEntry()
    {
        using var fixture = new PreviewFixture();
        var before = fixture.Previews();
        Assert.That(fixture.Preview(fixture.Terrain), Is.TypeOf<DrawableMetaInstance>());
        Assert.That(fixture.Editor.MetaDataDiagnostics.Any(diagnostic => ReferenceEquals(diagnostic.Source, fixture.Terrain)
            && diagnostic.ReasonKey == "SuperView.Diagnostics.EffectTerrainTableMissing"), Is.True);

        fixture.Editor.PreviewTerrainType = 6;

        Assert.That(fixture.Builder.EffectPreviewContext.TerrainType, Is.EqualTo(6));
        fixture.AssertOnlyRebuilt(before, fixture.Terrain);
        Assert.That(fixture.Preview(fixture.Terrain), Is.TypeOf<DrawableMetaInstance>(),
            "A missing terrain resource still has an editable marker that must be reconsidered when context changes.");
        before = fixture.Previews();
        fixture.Editor.PreviewTerrainType = -1;
        fixture.AssertOnlyRebuilt(before, fixture.Terrain);
        Assert.That(fixture.Builder.EffectPreviewContext.BypassTerrainSelection, Is.True);
        Assert.That(fixture.Preview(fixture.Terrain), Is.TypeOf<EffectMetaInstance>(),
            "The authored-resource option must recover the original VfxName without requiring terrain tables.");
        before = fixture.Previews();
        fixture.Editor.PreviewTerrainType = 1;
        fixture.AssertOnlyRebuilt(before, fixture.Terrain);
        Assert.That(fixture.Builder.EffectPreviewContext.BypassTerrainSelection, Is.False);
        Assert.That(fixture.Builder.EffectPreviewContext.TerrainType, Is.EqualTo(1));
        Assert.That(fixture.Preview(fixture.Terrain), Is.TypeOf<DrawableMetaInstance>());
        fixture.AssertDocumentAndClockUnchanged();
    }

    [Test]
    public void DistanceAndIdContext_RebuildTheRequiredSourcesAndRestoreEligibleEffects()
    {
        using var fixture = new PreviewFixture();
        fixture.SelectAbilityResource(aura: true);
        var before = fixture.Previews();
        fixture.Editor.PreviewDistanceCulling = true;
        fixture.AssertOnlyRebuilt(before, fixture.Culled);
        Assert.That(fixture.Builder.EffectPreviewContext.SuppressDistanceCulledEffects, Is.True);
        Assert.That(fixture.Preview(fixture.Culled), Is.TypeOf<DrawableMetaInstance>());
        before = fixture.Previews();
        fixture.Editor.PreviewDistanceCulling = false;
        fixture.AssertOnlyRebuilt(before, fixture.Culled);
        Assert.That(fixture.Preview(fixture.Culled), Is.TypeOf<EffectMetaInstance>());

        before = fixture.Previews();
        fixture.Editor.PreviewEffectId = fixture.Culled.Id;
        fixture.AssertOnlyRebuilt(before);
        Assert.That(fixture.Builder.EffectPreviewContext.CurrentEffectId, Is.EqualTo(fixture.Culled.Id));
        Assert.That(fixture.Builder.EffectPreviewContext.ApplyIdFilter, Is.False);
        fixture.Editor.PreviewEffectIdFilter = true;
        fixture.AssertOnlyRebuilt(before, fixture.Effects);
        Assert.That(fixture.Builder.EffectPreviewContext.ApplyIdFilter, Is.True);
        Assert.That(fixture.Preview(fixture.Culled), Is.TypeOf<EffectMetaInstance>());
        Assert.That(fixture.Preview(fixture.Aura), Is.TypeOf<DrawableMetaInstance>());
        before = fixture.Previews();
        fixture.Editor.PreviewEffectId = fixture.Aura.Id;
        fixture.AssertOnlyRebuilt(before, fixture.Effects);
        Assert.That(fixture.Preview(fixture.Culled), Is.TypeOf<DrawableMetaInstance>());
        Assert.That(fixture.Preview(fixture.Aura), Is.TypeOf<EffectMetaInstance>());
        Assert.That(fixture.Preview(fixture.Normal), Is.TypeOf<EffectMetaInstance>(), "ID zero remains unconditional.");
        before = fixture.Previews();
        fixture.Editor.PreviewEffectIdFilter = false;
        fixture.AssertOnlyRebuilt(before, fixture.Effects);
        Assert.That(fixture.Preview(fixture.Culled), Is.TypeOf<EffectMetaInstance>());
        fixture.AssertDocumentAndClockUnchanged();
    }

    [Test]
    public void UnrelatedPackChanges_DoNotRebuildAndCloseStopsRelevantPackCallbacks()
    {
        using var fixture = new PreviewFixture();
        fixture.SelectAbilityResource(aura: true);
        var before = fixture.Previews();
        var context = fixture.Builder.EffectPreviewContext;
        fixture.Events.Publish(new PackFileSavedEvent(fixture.UnrelatedFile));
        fixture.Events.Publish<PackFileContainerManipulationEvent>(new PackFileContainerFilesUpdatedEvent(fixture.Output, [fixture.UnrelatedFile]));
        fixture.Events.Publish<PackFileContainerManipulationEvent>(new PackFileContainerFilesAddedEvent(fixture.Output, [fixture.UnrelatedFile]));
        fixture.AssertOnlyRebuilt(before);
        Assert.That(fixture.Builder.EffectPreviewContext, Is.SameAs(context));
        fixture.AssertDocumentAndClockUnchanged();

        var auraPath = fixture.Editor.PreviewAuraResource;
        fixture.Close();
        fixture.Events.Publish(new PackFileSavedEvent(fixture.AuraFile));
        fixture.Events.Publish<PackFileContainerManipulationEvent>(new PackFileContainerFilesRemovedEvent(fixture.Output, [fixture.AuraFile]));
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Editor.GameWorld, Is.Null);
            Assert.That(fixture.Editor.PreviewAuraResource, Is.EqualTo(auraPath));
            Assert.That(fixture.Builder.EffectPreviewContext, Is.SameAs(context));
        });
        fixture.AssertOnlyRebuilt(before);
        fixture.AssertDocumentAndClockUnchanged();
    }

    private sealed class PreviewFixture : IDisposable
    {
        private const string PersistentPath = @"animations\battle\codex\effect_context_persistent.meta";
        private const string AnimationPath = @"animations\battle\codex\effect_context.anm.meta";
        private readonly MetaDataFileParser _parser;
        private readonly ParsedMetadataFile _persistent;
        private readonly ParsedMetadataFile _animation;
        private readonly ParsedMetadataAttribute[] _persistentSources;
        private readonly ParsedMetadataAttribute[] _animationSources;
        private readonly List<ParsedMetadataAttribute> _persistentSourceList;
        private readonly List<ParsedMetadataAttribute> _animationSourceList;
        private readonly byte[][] _sourceData;
        private readonly byte[][] _sourceBytes;
        private readonly PackFile _persistentFile;
        private readonly PackFile _animationFile;
        private readonly byte[] _persistentBytes;
        private readonly byte[] _animationBytes;
        private readonly byte[] _serializedPersistent;
        private readonly byte[] _serializedAnimation;
        private readonly AnimationPlayer _player;
        private readonly AnimationPlaybackTime _playback;
        private readonly object _playerViewModel;
        private readonly object _timeline;
        private readonly object _problems;
        private bool _closed;
        public AssetEditorTestRunner Runner { get; } = new();
        public SuperViewViewModel Editor { get; }
        public SceneObject Scene { get; }
        public IMetaDataBuilder Builder { get; }
        public IEventHub Events { get; }
        public PackFileContainer Output { get; }
        public PackFile AuraFile { get; }
        public PackFile WeaponFile { get; }
        public PackFile UnrelatedFile { get; }
        public Effect_v11 Normal { get; }
        public Effect_v11 Aura { get; }
        public Effect_v11 Weapon { get; }
        public Effect_v11 Terrain { get; }
        public Effect_v11 Culled { get; }
        public Effect_v11[] Effects => [Normal, Aura, Weapon, Terrain, Culled];

        public PreviewFixture()
        {
            // The existing mock owns a hidden graphics-device handle and has no render loop.
            Output = Runner.CreateOutputPack();
            Editor = (SuperViewViewModel)Runner.ServiceProvider.GetRequiredService<IEditorCreator>().Create(EditorEnums.SuperView_Editor);
            Assert.That(Editor.GameWorld, Is.TypeOf<WpfGameMock>());
            Assert.That(Editor.PreviewTerrainType, Is.EqualTo(-1));
            Assert.That(Editor.EffectTerrainChoices.Select(choice => choice.Value), Is.EqualTo(Enumerable.Range(-1, 18)));
            Editor.PreviewTerrainType = 1;
            Builder = Runner.GetRequiredServiceInCurrentEditorScope<IMetaDataBuilder>();
            Events = Runner.GetRequiredServiceInCurrentEditorScope<IEventHub>();
            _parser = Runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var saves = Runner.GetRequiredServiceInCurrentEditorScope<IFileSaveService>();
            var xml = Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>");
            saves.Save(@"vfx\diagnostic_normal.xml", xml, false);
            saves.Save(@"vfx\diagnostic_ground_grass.xml", xml, false);
            AuraFile = saves.Save(@"vfx\diagnostic_aura.xml", xml, false)!;
            WeaponFile = saves.Save(@"vfx\diagnostic_weapon.xml", xml, false)!;
            UnrelatedFile = saves.Save(@"materials\diagnostic_unrelated.xml", xml, false)!;
            _persistentFile = saves.Save(PersistentPath, _parser.GenerateBytes(2, new ParsedMetadataFile
            {
                Version = 2, Attributes = [Effect("diagnostic_normal", 0, 0), Effect("diagnostic_authored_aura", 1, 11)],
            }), false)!;
            _animationFile = saves.Save(AnimationPath, _parser.GenerateBytes(2, new ParsedMetadataFile
            {
                Version = 2,
                Attributes = [Effect("diagnostic_authored_weapon", 2, 22), Effect("diagnostic_ground_grass", 1, 33, terrain: true),
                    Effect("diagnostic_normal", 0, 44, culled: true), new ImpactPosition_v10 { Name = "IMPACT_POS", Version = 10, EndTime = 3 }],
            }), false)!;
            var scene = Editor.SceneObjects.Single();
            Scene = scene.Data;
            scene.FragAndSlotSelection.MetaDataPersistName = PersistentPath;
            scene.FragAndSlotSelection.MetaDataName = AnimationPath;
            Runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>().SetMetaFile(Scene, _animationFile, _persistentFile);
            _persistent = Editor.PersistentMetaEditor.ParsedFile!;
            _animation = Editor.MetaEditor.ParsedFile!;
            _persistentSources = _persistent.Attributes.ToArray();
            _animationSources = _animation.Attributes.ToArray();
            _persistentSourceList = _persistent.Attributes;
            _animationSourceList = _animation.Attributes;
            _sourceData = _persistentSources.Concat(_animationSources).Select(source => source.Data).ToArray();
            _sourceBytes = _sourceData.Select(bytes => bytes.ToArray()).ToArray();
            Normal = (Effect_v11)_persistentSources[0];
            Aura = (Effect_v11)_persistentSources[1];
            Weapon = (Effect_v11)_animationSources[0];
            Terrain = (Effect_v11)_animationSources[1];
            Culled = (Effect_v11)_animationSources[2];
            Assert.That(Scene.MetaDataItems, Has.Count.EqualTo(6));
            _persistentBytes = _persistentFile.DataSource.ReadData().ToArray();
            _animationBytes = _animationFile.DataSource.ReadData().ToArray();
            _serializedPersistent = _parser.GenerateBytes(2, _persistent);
            _serializedAnimation = _parser.GenerateBytes(2, _animation);
            _player = Scene.Player;
            _player.QueueSeekToTimeSeconds(0.625f);
            _playback = _player.PlaybackTime;
            _playerViewModel = Editor.Player;
            _timeline = Editor.MetaDataTimeline;
            _problems = Editor.MetaDataProblems;
            AssertDocumentAndClockUnchanged();
        }

        public IMetaDataInstance Preview(ParsedMetadataAttribute source) =>
            Scene.MetaDataItems.Single(instance => instance is IMetaDataPreview preview && ReferenceEquals(preview.Source, source));

        public Dictionary<ParsedMetadataAttribute, IMetaDataInstance> Previews() => Scene.MetaDataItems
            .ToDictionary(instance => ((IMetaDataPreview)instance).Source);

        public void SelectAbilityResource(bool aura)
        {
            Runner.Dialogs.Setup(dialogs => dialogs.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<BrowseDialogFilter>()))
                .Returns(new BrowseDialogResultFile(true, aura ? AuraFile : WeaponFile));
            var command = aura ? Editor.BrowseAuraResourceCommand : Editor.BrowseWeaponResourceCommand;
            Assert.That(command.CanExecute(null), Is.True);
            command.Execute(null);
        }

        public void AssertOnlyRebuilt(Dictionary<ParsedMetadataAttribute, IMetaDataInstance> before, params ParsedMetadataAttribute[] affected)
        {
            Assert.That(Scene.MetaDataItems, Has.Count.EqualTo(before.Count));
            foreach (var pair in before)
                Assert.That(ReferenceEquals(Preview(pair.Key), pair.Value), Is.EqualTo(!affected.Contains(pair.Key)),
                    $"Unexpected rebuild scope for {pair.Key.Name}: {(pair.Key as Effect_v11)?.VfxName}.");
        }

        public void AssertDocumentAndClockUnchanged()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Editor.PersistentMetaEditor.ParsedFile, Is.SameAs(_persistent));
                Assert.That(Editor.MetaEditor.ParsedFile, Is.SameAs(_animation));
                Assert.That(_persistent.Attributes, Is.SameAs(_persistentSourceList));
                Assert.That(_animation.Attributes, Is.SameAs(_animationSourceList));
                Assert.That(_persistent.Attributes, Is.EqualTo(_persistentSources));
                Assert.That(_animation.Attributes, Is.EqualTo(_animationSources));
                var sources = _persistentSources.Concat(_animationSources).ToArray();
                for (var index = 0; index < sources.Length; index++)
                {
                    Assert.That(sources[index].Data, Is.SameAs(_sourceData[index]));
                    Assert.That(sources[index].Data, Is.EqualTo(_sourceBytes[index]));
                }
                Assert.That(_parser.GenerateBytes(2, _persistent), Is.EqualTo(_serializedPersistent));
                Assert.That(_parser.GenerateBytes(2, _animation), Is.EqualTo(_serializedAnimation));
                Assert.That(_persistentFile.DataSource.ReadData(), Is.EqualTo(_persistentBytes));
                Assert.That(_animationFile.DataSource.ReadData(), Is.EqualTo(_animationBytes));
                Assert.That(Editor.HasUnsavedChanges, Is.False);
                Assert.That(Editor.PersistentMetaEditor.HasUnsavedChanges, Is.False);
                Assert.That(Editor.MetaEditor.HasUnsavedChanges, Is.False);
                Assert.That(Scene.Player, Is.SameAs(_player));
                Assert.That(_player.PlaybackTime, Is.EqualTo(_playback));
                Assert.That(Editor.Player, Is.SameAs(_playerViewModel));
                Assert.That(Editor.MetaDataTimeline, Is.SameAs(_timeline));
                Assert.That(Editor.MetaDataProblems, Is.SameAs(_problems));
            });
        }

        public void Close()
        {
            if (_closed) return;
            Editor.Close();
            _closed = true;
        }

        public void Dispose()
        {
            Close();
            foreach (var preview in Scene.MetaDataItems) preview.CleanUp();
            (Runner.ServiceProvider as IDisposable)?.Dispose();
        }

        private static Effect_v11 Effect(string name, int type, int id, bool terrain = false, bool culled = false) => new()
        {
            Name = "EFFECT", Version = 11, VfxName = name, EffectType = type, Id = id, TerrainMutable = terrain,
            DistanceCulled = culled, EndTime = 3, NodeIndex = -1, Orientation = new Vector4(0, 0, 0, 1),
        };
    }
}
