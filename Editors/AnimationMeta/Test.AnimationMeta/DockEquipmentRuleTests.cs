using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Test.AnimationMeta;

[TestFixture]
internal sealed class DockEquipmentRuleTests
{
    [TestCaseSource(nameof(LegacyDockSources))]
    public void Build_LegacyDockV3NormalizesTheSlotWithoutChangingTheSource(
        DockEquipment_v3 source, string expectedBoneName)
    {
        const string path = @"animations\test\dock.anim";
        var file = CreateSkeletonFile();
        file.Header.Version = 8;
        file.Header.FrameRate = 30;
        file.Header.AnimationTotalPlayTimeInSec = 1;
        file.Bones[0].Name = "base";
        file.Bones[1].Name = source.SkeletonNameAlternatives[0];
        file.Bones[2].Name = expectedBoneName;
        var files = new Mock<IPackFileService>();
        files.Setup(service => service.FindFile(path, null)).Returns(
            PackFile.CreateFromBytes(path, AnimationFile.ConvertToBytes(file)));
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!);
        var skeleton = GameSkeleton.CreateFromAnimationFile(file, new AnimationPlayer());
        var provider = new Mock<ISkeletonProvider>();
        provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        var fragment = new Mock<IAnimationBinGenericFormat>();
        fragment.SetupGet(value => value.Entries).Returns([
            new AnimationBinEntryGenericFormat { SlotName = source.AnimationSlotName, AnimationFile = path },
        ]);
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var originalPayload = parser.Serialize(source, out _);
        var originalSlot = source.PropBoneId;
        var document = new ParsedMetadataFile { Version = 2, Attributes = [source] };

        var built = builder.Build(document, null, null, new GroupNode("root"),
            provider.Object, new AnimationPlayer(), fragment.Object);

        Assert.That(built.AnimationRules, Has.Count.EqualTo(1));
        Assert.That(built.AnimationRules.Single(), Is.TypeOf<DockEquipmentRule>());
        Assert.That(built.Diagnostics, Is.Empty);
        files.Verify(service => service.FindFile(path, null), Times.Once);
        var clip = CreateClip(new(3, 7, 2), Quaternion.Identity);
        var expected = AnimationSampler.Sample(0.5f, skeleton, clip);
        var authored = AnimationSampler.Sample(0, skeleton, new AnimationClip(file, skeleton));
        var equipmentOffset = authored.GetSkeletonAnimatedWorld(skeleton, 2) *
            Matrix.Invert(authored.GetSkeletonAnimatedWorld(skeleton, 1));
        var actual = AnimationSampler.Sample(0.5f, skeleton, clip, built.AnimationRules.ToList());
        AssertTransform(actual.GetSkeletonAnimatedWorld(skeleton, 2), equipmentOffset *
            expected.GetSkeletonAnimatedWorld(skeleton, 1));
        Assert.That(document.Attributes.Single(), Is.SameAs(source));
        Assert.That(source.PropBoneId, Is.EqualTo(originalSlot));
        Assert.That(source.Version, Is.EqualTo(3));
        Assert.That(parser.Serialize(source, out _), Is.EqualTo(originalPayload));
    }

    private static IEnumerable<TestCaseData> LegacyDockSources()
    {
        // Original v3 payloads from the manifest-verified demigryph and dual-pistol META.
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        foreach (var (name, payload, boneName) in new[]
        {
            ("DOCK_EQPT_RHAND", "0300000000000000000000000000010000000000000000000000", "be_prop_0"),
            ("DOCK_EQPT_LHAND", "0300000000000000000000000000030000000000000000000000", "be_prop_2"),
            ("DOCK_EQPT_LWAIST", "0300000000000000000000000000020000000000000000000000", "be_prop_1"),
            ("DOCK_EQPT_RHAND", "0300000000000000000000000000040000000000000000000000", "be_prop_3"),
            ("DOCK_EQPT_LHAND", "0300000000000000000000000000060000000000000000000000", "be_prop_5"),
        })
        {
            var decoded = parser.DeSerialize(new ParsedUnknownMetadataAttribute
            {
                Name = name, Version = 3, Data = Convert.FromHexString(payload),
            }, out var error);
            if (decoded is not DockEquipment_v3 dock)
                throw new InvalidOperationException($"Original dock META did not decode: {error}");
            yield return new TestCaseData(dock, boneName);
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Build_LegacyInvalidSlotIsDiagnosedWithoutChangingItsStoredValue(int slot)
    {
        var source = new DockEquipmentRHand_v3
        {
            Name = "DOCK_EQPT_RHAND", Version = 3, PropBoneId = slot,
        };
        var provider = new Mock<ISkeletonProvider>();
        provider.SetupGet(value => value.Skeleton).Returns(CreateSkeleton());
        var builder = new MetaDataBuilder(null!, null!, null!, Mock.Of<IPackFileService>(), null!);
        var document = new ParsedMetadataFile { Version = 2, Attributes = [source] };

        var built = builder.Build(document, null, null, new GroupNode("root"),
            provider.Object, new AnimationPlayer(), null);

        Assert.That(built.AnimationRules, Is.Empty);
        Assert.That(built.Diagnostics, Has.Exactly(1).Matches<MetaDataBuildDiagnostic>(diagnostic =>
            ReferenceEquals(diagnostic.Source, source) && diagnostic.ReasonKey == "SuperView.Diagnostics.RuleUnavailable"));
        Assert.That(source.PropBoneId, Is.EqualTo(slot));
    }

    [Test]
    public void BuildPreview_LegacyDockEditsRequireTheRulesToBeRebuilt()
    {
        var source = new DockEquipmentRHand_v3 { Name = "DOCK_EQPT_RHAND", PropBoneId = 1 };
        var builder = new MetaDataBuilder(null!, null!, null!, Mock.Of<IPackFileService>(), null!);

        var result = builder.BuildPreview(source, MetaDataDocumentOwner.Persistent, false,
            new GroupNode("root"), Mock.Of<ISkeletonProvider>(), new AnimationPlayer());

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    public void Dock_UsesTheSampledPoseOnFirstFrameAndPausedSeek(float time)
    {
        var skeleton = CreateSkeleton();
        var staleClip = CreateClip(new(-12, -8, -3), Quaternion.Identity);
        skeleton.SetAnimationFrame(AnimationSampler.Sample(0, skeleton, staleClip));
        var staleHand = skeleton.GetAnimatedWorldTranform(1);
        var provider = new Mock<ISkeletonProvider>();
        provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        var dockClip = CreateClip(new(1, 2, 0), Quaternion.Identity);
        var mainClip = CreateClip(new(3, 7, 2),
            Quaternion.CreateFromAxisAngle(Vector3.Forward, 0.7f));
        var dock = new DockEquipmentRule(1, 0, dockClip,
            provider.Object, 0, 0);

        var authoredDock = AnimationSampler.Sample(0, skeleton, dockClip);
        var handInDock = authoredDock.GetSkeletonAnimatedWorld(skeleton, 1);
        var weaponInDock = authoredDock.GetSkeletonAnimatedWorld(skeleton, 2);
        var currentPose = AnimationSampler.Sample(time, skeleton, mainClip);
        var currentHand = currentPose.GetSkeletonAnimatedWorld(skeleton, 1);
        var expectedWeapon = weaponInDock * Matrix.Invert(handInDock) * currentHand;

        var result = AnimationSampler.Sample(time, skeleton, mainClip, [dock]);

        AssertTransform(result.GetSkeletonAnimatedWorld(skeleton, 2), expectedWeapon);
        AssertTransform(result.GetSkeletonAnimatedWorld(skeleton, 1), currentHand);
        AssertTransform(skeleton.GetAnimatedWorldTranform(1), staleHand);
    }

    [TestCase(0f)]
    [TestCase(0.2f)]
    [TestCase(1f)]
    public void Dock_OutsideAuthoredRangePreservesTheAnimatedWeapon(float time)
    {
        var skeleton = CreateSkeleton();
        var provider = new Mock<ISkeletonProvider>();
        provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        var clip = CreateClip(new(3, 7, 2), Quaternion.Identity);
        var dock = new DockEquipmentRule(1, 0,
            CreateClip(new(1, 2, 0), Quaternion.Identity), provider.Object, 0.25f, 0.75f);
        var expected = AnimationSampler.Sample(time, skeleton, clip);

        var actual = AnimationSampler.Sample(time, skeleton, clip, [dock]);

        AssertTransform(actual.GetSkeletonAnimatedWorld(skeleton, 2),
            expected.GetSkeletonAnimatedWorld(skeleton, 2));
    }

    private static void AssertTransform(Matrix actual, Matrix expected)
    {
        foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            Assert.That(Vector3.Distance(Vector3.Transform(point, actual),
                Vector3.Transform(point, expected)), Is.LessThan(0.0001f));
    }

    private static AnimationClip CreateClip(Vector3 hand, Quaternion rotation)
    {
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        foreach (var shift in new[] { Vector3.Zero, new Vector3(2, 1, -1) })
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.AddRange([new(1, 0, 2), hand + shift, new(2, 3, 0)]);
            frame.Rotation.AddRange([
                Quaternion.CreateFromAxisAngle(Vector3.Up, 0.4f),
                rotation, Quaternion.Identity]);
            frame.Scale.AddRange([Vector3.One, Vector3.One, Vector3.One]);
            clip.DynamicFrames.Add(frame);
        }
        return clip;
    }

    private static GameSkeleton CreateSkeleton() =>
        GameSkeleton.CreateFromAnimationFile(CreateSkeletonFile(), new AnimationPlayer());

    private static AnimationFile CreateSkeletonFile()
    {
        var source = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "dock_test" },
            Bones = [
                new() { Id = 0, Name = "root", ParentId = -1 },
                new() { Id = 1, Name = "hand_right", ParentId = 0 },
                new() { Id = 2, Name = "be_prop_0", ParentId = 0 },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.AddRange([
            new RmvVector3(), new RmvVector3(0, 1, 0), new RmvVector3()]);
        frame.Quaternion.AddRange(Enumerable.Repeat(new RmvVector4(0, 0, 0, 1), 3));
        var part = new AnimationFile.AnimationPart();
        for (var index = 0; index < 3; index++)
        {
            part.TranslationMappings.Add(new(index));
            part.RotationMappings.Add(new(index));
        }
        part.DynamicFrames.Add(frame);
        source.AnimationParts.Add(part);
        return source;
    }
}
