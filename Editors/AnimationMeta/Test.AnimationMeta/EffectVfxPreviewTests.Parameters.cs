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
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(7)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectParameters_AllDecodedVersionsPassPositionOrientationAndAuthoredScale(int version)
    {
        var source = ParameterEffect(version);
        var effect = (IEffectMeta)source;
        var expectedScale = version is 3 or 5 or 7 or 11 or 12 ? 2.5f : 1;
        effect.Position = new(1.5f, -2, 3);
        effect.Orientation = Quaternion.CreateFromYawPitchRoll(.4f, -.2f, .7f).ToVector4();
        effect.NodeIndex = -1;
        var originalData = source.Data;
        var builder = ParameterBuilder(out var factory);
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            instance.Update(1.25f);
            var marker = root.Children.Single();
            var particles = (VfxPreviewSceneNode)marker.Children.Single();
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
            var preview = factory.Created.Single();
            var expected = Matrix.CreateScale(expectedScale) * Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation))
                * Matrix.CreateTranslation(effect.Position);
            AssertParameterMatrix(preview.Attachment, expected);
            AssertParameterMatrix(preview.History!(preview.Time), expected);
            Assert.Multiple(() =>
            {
                Assert.That(preview.Time, Is.EqualTo(version == 1 ? 1.25f : 1));
                Assert.That(Vector3.Distance(instance.FocusPosition, effect.Position), Is.LessThan(.0001f));
                Assert.That(effect.Position, Is.EqualTo(new Vector3(1.5f, -2, 3)));
                Assert.That(source.Data, Is.SameAs(originalData));
                Assert.That(source.Version, Is.EqualTo(version));
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [TestCase(7)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectParameters_TrackedVersionsUseTheSameLocalTransformForCurrentAndHistoricalBones(int version)
    {
        var source = ParameterEffect(version);
        var effect = (IEffectMeta)source;
        effect.Tracking = true; effect.NodeIndex = 0;
        effect.Position = new(2, 1, -3);
        effect.Orientation = Quaternion.CreateFromYawPitchRoll(.3f, -.5f, .2f).ToVector4();
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = Skeleton(player);
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(4) };
        foreach (var x in new[] { 0, 10, 20 })
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame
            { Position = [new(x, 0, 0)], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
        player.SetAnimation(clip, skeleton); player.Pause(); player.SeekToTimeSeconds(1.25f);
        var time = player.CurrentTime; var frameEvents = 0;
        player.OnFrameChanged += _ => frameEvents++;
        var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        var builder = ParameterBuilder(out var factory);
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor")
        { ModelMatrix = Matrix.CreateRotationY(.45f) * Matrix.CreateTranslation(7, 0, -3) });
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, provider.Object, player, null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            instance.Update(1.25f);
            var marker = root.Children.Single();
            var particles = (VfxPreviewSceneNode)marker.Children.Single();
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
            var preview = factory.Created.Single();
            var local = Matrix.CreateScale(2.5f) * Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation))
                * Matrix.CreateTranslation(effect.Position);
            AssertParameterMatrix(preview.Attachment, local * skeleton.GetAnimatedWorldTranform(0) * root.ModelMatrix);
            var past = AnimationSampler.Sample(.25f / (float)player.Duration.TotalSeconds,
                skeleton, clip, player.AnimationRules);
            AssertParameterMatrix(preview.History!(0), local * past.GetSkeletonAnimatedWorld(skeleton, 0) * root.ModelMatrix);
            Assert.Multiple(() =>
            {
                Assert.That(instance.HighlightedBoneIndex, Is.EqualTo(0));
                Assert.That(Vector3.Distance(preview.Attachment.Translation, instance.FocusPosition), Is.LessThan(.0001f));
                Assert.That(player.CurrentTime, Is.EqualTo(time));
                Assert.That(frameEvents, Is.EqualTo(0));
                Assert.That(effect.Tracking, Is.True);
                Assert.That(effect.NodeIndex, Is.EqualTo(0));
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [TestCase(0f)]
    [TestCase(-2.5f)]
    public void EffectParameters_ZeroAndNegativeScalePreserveTheSourceAndKeepTheMarker(float scale)
    {
        var source = (Effect_v12)ParameterEffect(12); source.Scale = scale;
        var builder = ParameterBuilder(out var factory);
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single(); instance.Update(1);
            var marker = root.Children.Single(); var particles = (VfxPreviewSceneNode)marker.Children.Single();
            Assert.Multiple(() =>
            {
                Assert.That(marker.IsVisible, Is.True);
                Assert.That(particles.IsVisible, Is.EqualTo(scale != 0));
                Assert.That(source.Scale, Is.EqualTo(scale));
                Assert.That(source.Position, Is.EqualTo(Vector3.Zero));
            });
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.EqualTo(scale != 0));
            if (scale == 0) Assert.That(factory.Created, Is.Empty);
            else
            {
                AssertParameterMatrix(factory.Created.Single().Attachment, Matrix.CreateScale(scale));
                Assert.That(factory.Created.Single().Attachment.Determinant(), Is.LessThan(0));
            }
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [Test]
    public void EffectParameters_UnimplementedSettingsReportTheirOriginalSourceAndOwnerWithoutChangingValues()
    {
        var current = (Effect_v12)ParameterEffect(12);
        current.EffectType = 2; current.TerrainMutable = true; current.DistanceCulled = true; current.ScalesWithParent = false;
        var legacy = (Effect_v3)ParameterEffect(3); legacy.BoolUnknown = "true";
        var originalCurrentData = current.Data; var originalLegacyData = legacy.Data;
        var builder = ParameterBuilder(out _);
        var root = new GroupNode("actor");
        var result = builder.Build(new ParsedMetadataFile { Version = 10, Attributes = [current] },
            new ParsedMetadataFile { Version = 10, Attributes = [legacy] }, null, root, null!, new AnimationPlayer(), null);
        try
        {
            var expected = new[] { "SuperView.Diagnostics.EffectTypeUnsupported", "SuperView.Diagnostics.EffectTerrainUnsupported",
                "SuperView.Diagnostics.EffectDistanceCullingUnsupported", "SuperView.Diagnostics.EffectParentScaleUnsupported" };
            foreach (var key in expected)
            {
                var diagnostic = result.Diagnostics.Single(value => value.ReasonKey == key);
                Assert.That(diagnostic.Source, Is.SameAs(current));
                Assert.That(diagnostic.Owner, Is.EqualTo(MetaDataDocumentOwner.Persistent));
            }
            var legacyDiagnostic = result.Diagnostics.Single(value => value.ReasonKey == "SuperView.Diagnostics.EffectLegacyFlagUnsupported");
            Assert.Multiple(() =>
            {
                Assert.That(legacyDiagnostic.Source, Is.SameAs(legacy));
                Assert.That(legacyDiagnostic.Owner, Is.EqualTo(MetaDataDocumentOwner.Animation));
                Assert.That(current.EffectType, Is.EqualTo(2));
                Assert.That(current.TerrainMutable, Is.True);
                Assert.That(current.DistanceCulled, Is.True);
                Assert.That(current.ScalesWithParent, Is.False);
                Assert.That(current.Scale, Is.EqualTo(2.5f));
                Assert.That(legacy.BoolUnknown, Is.EqualTo("true"));
                Assert.That(current.Data, Is.SameAs(originalCurrentData));
                Assert.That(legacy.Data, Is.SameAs(originalLegacyData));
                Assert.That(result.Instances.Count, Is.EqualTo(2));
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [Test]
    public void EffectParameters_InstantTriggerPlaysItsOwnTailWithoutChangingAuthoredTimeOrUserVisibility()
    {
        var source = (Effect_v12)ParameterEffect(12); source.StartTime = source.EndTime = 1.25f;
        var builder = ParameterBuilder(out var factory);
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor"));
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            var marker = root.Children.Single(); var particles = (VfxPreviewSceneNode)marker.Children.Single();
            instance.Update(1); Assert.That(marker.IsVisible, Is.False);
            instance.Update(1.5f);
            Assert.That(marker.IsVisible, Is.True, "A trigger crossed between animation frames must still display its VFX tail.");
            Assert.That(instance.ShowForEntireAnimation, Is.False, "Automatic tail visibility must not enable the user's show-all override.");
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
            Assert.That(factory.Created.Single().Time, Is.EqualTo(.25f));
            instance.IsEnabled = false; instance.Update(1.5f);
            Assert.That(marker.IsVisible, Is.False, "Disabling the Effect must suppress its tail as well.");
            instance.IsEnabled = true; instance.Update(1.5f);
            Assert.That(marker.IsVisible, Is.True);
            instance.Update(9.5f);
            Assert.That(marker.IsVisible, Is.False, "The default eight-second runtime tail has already ended.");
            instance.ShowForEntireAnimation = true; instance.Update(9.5f);
            Assert.That(marker.IsVisible, Is.True); Assert.That(instance.ShowForEntireAnimation, Is.True);
            instance.ShowForEntireAnimation = false; instance.Update(9.5f);
            Assert.Multiple(() =>
            {
                Assert.That(marker.IsVisible, Is.False);
                Assert.That(instance.ShowForEntireAnimation, Is.False);
                Assert.That(source.StartTime, Is.EqualTo(1.25f));
                Assert.That(source.EndTime, Is.EqualTo(1.25f));
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    private static ParsedMetadataAttribute ParameterEffect(int version)
    {
        IEffectMeta effect = version switch
        {
            1 => new Effect_v1(), 2 => new Effect_v2(), 3 => new Effect_v3 { Scale = 2.5f }, 4 => new Effect_v4(),
            5 => new Effect_v5 { Scale = 2.5f }, 7 => new Effect_v7 { Scale = 2.5f },
            11 => new Effect_v11 { Scale = 2.5f }, 12 => new Effect_v12 { Scale = 2.5f },
            _ => throw new ArgumentOutOfRangeException(nameof(version)),
        };
        effect.VfxName = "parameter_fixture";
        var source = (ParsedMetadataAttribute)effect;
        source.Name = "EFFECT"; source.Version = version; source.Data = [1, 2, 3];
        if (source is DecodedMetaEntryBase timed) { timed.StartTime = .25f; timed.EndTime = 3; }
        else if (source is DecodedMetaEntryBase_v2 older) { older.StartTime = .25f; older.EndTime = 3; }
        return source;
    }

    private static MetaDataBuilder ParameterBuilder(out CapturingFactory factory)
    {
        // A CPU-only parameter fixture, not a game VFX or visual-fidelity sample.
        var files = new Mock<IPackFileService>();
        var file = PackFile.CreateFromBytes("parameter_fixture.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>"));
        files.Setup(value => value.FindFile(@"vfx\parameter_fixture.xml", null)).Returns(file);
        files.Setup(value => value.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\parameter_fixture.xml");
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        factory = new CapturingFactory();
        return new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory, Mock.Of<IDeviceResolver>(), new VfxPreviewDocumentStore());
    }

    private static void AssertParameterMatrix(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(actual.Translation, expected.Translation), Is.LessThan(.0001f));
        });
    }
}
