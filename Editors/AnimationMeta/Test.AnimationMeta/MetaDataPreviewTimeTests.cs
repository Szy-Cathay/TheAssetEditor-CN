using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta;

[TestFixture]
internal class MetaDataPreviewTimeTests
{
    [Test]
    public void DrawableTag_IsVisibleOnlyDuringItsActiveTimeRange()
    {
        var node = new SimpleDrawableNode("tag");
        var instance = new DrawableMetaInstance(1, 2, node);

        instance.Update(0.5f);
        Assert.That(node.IsVisible, Is.False);

        instance.Update(1.5f);
        Assert.That(node.IsVisible, Is.True);

        instance.Update(2.5f);
        Assert.That(node.IsVisible, Is.False);
    }

    [Test]
    public void DrawableTag_CanRemainVisibleForTheEntireAnimation()
    {
        var node = new SimpleDrawableNode("tag");
        var instance = new DrawableMetaInstance(1, 2, node)
        {
            ShowForEntireAnimation = true,
        };

        instance.Update(0.5f);
        Assert.That(node.IsVisible, Is.True);

        instance.Update(2.5f);
        Assert.That(node.IsVisible, Is.True);
    }

    [Test]
    public void AnimatedProp_IsVisibleOnlyDuringItsActiveTimeRange()
    {
        var node = new GroupNode("prop");
        var instance = new AnimatedPropInstance(node, new(), 1, 2);

        instance.Update(0.5f);
        Assert.That(node.IsVisible, Is.False);

        instance.Update(1.5f);
        Assert.That(node.IsVisible, Is.True);

        instance.Update(2.5f);
        Assert.That(node.IsVisible, Is.False);
    }

    [Test]
    public void OrdinaryProp_IsVisibleOnlyDuringItsActiveTimeRange()
    {
        var node = new GroupNode("prop");
        var instance = new PropInstance(
            node,
            new(),
            null,
            -1,
            default,
            default,
            1,
            2);

        instance.Update(0.5f);
        Assert.That(node.IsVisible, Is.False);

        instance.Update(1.5f);
        Assert.That(node.IsVisible, Is.True);

        instance.Update(2.5f);
        Assert.That(node.IsVisible, Is.False);
    }

    [Test]
    public void TimedMetadata_ExposesItsAuthoredTimeRange()
    {
        var timedTag = new FirePos_v10
        {
            StartTime = 1.25f,
            EndTime = 2.5f,
        };

        var hasTimeRange = MetaDataTimeRange.TryCreate(
            timedTag,
            out var timeRange);

        Assert.Multiple(() =>
        {
            Assert.That(hasTimeRange, Is.True);
            Assert.That(timeRange.StartTime, Is.EqualTo(1.25f));
            Assert.That(timeRange.EndTime, Is.EqualTo(2.5f));
            Assert.That(
                MetaDataTimeRange.TryCreate(new FirePos_v0(), out _),
                Is.False);
        });
    }

    [Test]
    public void ZeroZeroSplashAttack_IsOnlyActiveAtTheFirstPreviewFrame()
    {
        var source = new SplashAttack_v10
        {
            StartTime = 0,
            EndTime = 0,
        };

        var created = MetaDataTimeRange.TryCreate(source, out var timeRange);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(timeRange.IsZeroRange, Is.True);
            Assert.That(timeRange.IsWholeAnimationRange, Is.False);
            Assert.That(timeRange.Contains(0), Is.True);
            Assert.That(timeRange.Contains(0.01f, 1f / 30), Is.True);
            Assert.That(timeRange.Contains(1.5f), Is.False);
            Assert.That(timeRange.Contains(1.5f, 1f / 30), Is.False);
        });
    }

    [Test]
    public void ZeroZeroSplashPreview_UsesPlayerTimebaseSampleDuration()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromMilliseconds(300),
        };
        for (var frameIndex = 0; frameIndex < 7; frameIndex++)
            clip.DynamicFrames.Add(CreateFrame(Quaternion.Identity));
        player.SetAnimation(clip, skeleton);
        var node = new SimpleDrawableNode("splash");
        var source = new SplashAttack_v10
        {
            StartTime = 0,
            EndTime = 0,
        };
        var instance = new CombatMetaDataInstance(
            source,
            CombatMetaDataPreviewCategory.Splash,
            Vector3.Zero,
            node,
            false,
            _ => { },
            player,
            new MetaDataTimeRange(0, 0));

        instance.Update(0.04f);
        Assert.That(node.IsVisible, Is.True);

        instance.Update(0.05f);
        Assert.That(node.IsVisible, Is.False);
    }

    [TestCaseSource(nameof(WholeAnimationZeroRangeTags))]
    public void ZeroZeroStateTag_IsActiveForTheWholeAnimation(
        ParsedMetadataAttribute source)
    {
        var created = MetaDataTimeRange.TryCreate(source, out var timeRange);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(timeRange.IsWholeAnimationRange, Is.True);
            Assert.That(timeRange.Contains(0), Is.True);
            Assert.That(timeRange.Contains(1.5f), Is.True);
        });
    }

    [Test]
    public void AnimationPlayer_SeekToTimeSeconds_UsesExactMetadataTime()
    {
        var player = new AnimationPlayer();

        player.SeekToTimeSeconds(1.25f);

        Assert.That(player.CurrentTime, Is.EqualTo(TimeSpan.FromMilliseconds(1250)));
    }

    [Test]
    public void AnimatedProp_PausedUpdate_RefreshesAttachedModelSkeleton()
    {
        var rootPlayer = new AnimationPlayer();
        var rootSkeleton = CreateSkeleton(rootPlayer);
        rootPlayer.IsEnabled = true;
        var rootSkeletonProvider = new Mock<ISkeletonProvider>();
        rootSkeletonProvider.SetupGet(value => value.Skeleton)
            .Returns(rootSkeleton);
        rootPlayer.SetAnimation(null!, rootSkeleton);
        rootPlayer.SetManualFrame(AnimationSampler.Sample(
            0,
            rootSkeleton,
            null!));

        var propPlayer = new AnimationPlayer();
        var propSkeleton = CreateSkeleton(propPlayer);
        propPlayer.IsEnabled = true;
        propPlayer.SetAnimation(null!, propSkeleton);

        var position = new Vector3(1, 2, 3);
        propPlayer.AnimationRules.Add(new CopyRootTransform(
            rootSkeletonProvider.Object,
            0,
            () => position,
            () => Quaternion.Identity));
        propPlayer.Pause();
        propPlayer.Refresh();

        var source = new AnimatedProp_v14
        {
            Name = "ANIMATED_PROP",
            Version = 14,
        };
        var instance = new AnimatedPropInstance(
            new GroupNode("animated prop"),
            propPlayer,
            rootSkeletonProvider.Object,
            0,
            () => position,
            () => Quaternion.Identity,
            0,
            1,
            source,
            false,
            _ => { });

        position = new Vector3(7, 8, 9);
        instance.Update(0.5f);

        Assert.That(
            propPlayer.GetCurrentAnimationFrame()
                .BoneTransforms[0]
                .WorldTransform
                .Translation,
            Is.EqualTo(position));
    }

    [Test]
    public void AnimatedProp_PausedUpdate_SeeksAttachedAnimationToMainTime()
    {
        var propPlayer = new AnimationPlayer();
        var propSkeleton = CreateSkeleton(propPlayer, includeChild: true);
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        clip.DynamicFrames.Add(CreateFrame(Quaternion.Identity));
        var expectedRotation = Quaternion.CreateFromAxisAngle(
            Vector3.Up,
            MathHelper.PiOver2);
        clip.DynamicFrames.Add(CreateFrame(expectedRotation));
        propPlayer.IsEnabled = true;
        propPlayer.SetAnimation(clip, propSkeleton);
        propPlayer.Pause();

        var instance = new AnimatedPropInstance(
            new GroupNode("animated prop"),
            propPlayer,
            0,
            1);

        instance.Update(0.5f);

        var actualRotation = propPlayer.GetCurrentAnimationFrame()
            .BoneTransforms[1]
            .Rotation;
        Assert.That(
            Math.Abs(Quaternion.Dot(actualRotation, expectedRotation)),
            Is.EqualTo(1).Within(0.0001f));

        instance.Update(1.5f);
        actualRotation = propPlayer.GetCurrentAnimationFrame()
            .BoneTransforms[1]
            .Rotation;
        Assert.That(
            Math.Abs(Quaternion.Dot(actualRotation, expectedRotation)),
            Is.EqualTo(1).Within(0.0001f));
    }

    private static IEnumerable<ParsedMetadataAttribute>
        WholeAnimationZeroRangeTags()
    {
        yield return new AnimatedProp_v14();
        yield return new DockEquipmentLHand_v11();
        yield return new Splice_v12();
        yield return new Transform_v10();
    }

    private static AnimationClip.KeyFrame CreateFrame(
        Quaternion childRotation)
    {
        var frame = new AnimationClip.KeyFrame();
        frame.Position.AddRange([Vector3.Zero, Vector3.Zero]);
        frame.Rotation.AddRange([Quaternion.Identity, childRotation]);
        frame.Scale.AddRange([Vector3.One, Vector3.One]);
        return frame;
    }

    private static GameSkeleton CreateSkeleton(
        AnimationPlayer player,
        bool includeChild = false)
    {
        var bones = new List<AnimationFile.BoneInfo>
        {
            new()
            {
                Id = 0,
                Name = "root",
                ParentId = -1,
            },
        };
        if (includeChild)
        {
            bones.Add(new AnimationFile.BoneInfo
            {
                Id = 1,
                Name = "child",
                ParentId = 0,
            });
        }

        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test",
            },
            Bones = bones.ToArray(),
        };

        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        if (includeChild)
        {
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }

        var part = new AnimationFile.AnimationPart();
        part.TranslationMappings.Add(
            new AnimationFile.AnimationBoneMapping(0));
        part.RotationMappings.Add(
            new AnimationFile.AnimationBoneMapping(0));
        if (includeChild)
        {
            part.TranslationMappings.Add(
                new AnimationFile.AnimationBoneMapping(1));
            part.RotationMappings.Add(
                new AnimationFile.AnimationBoneMapping(1));
        }
        part.DynamicFrames.Add(frame);
        animation.AnimationParts.Add(part);

        return GameSkeleton.CreateFromAnimationFile(animation, player);
    }
}
