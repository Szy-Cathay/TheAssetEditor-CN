using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.TestUtility;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class AnimationFileV8WriterTests
{
    [Test]
    public void ConvertToBytes_VersionEightWithStaticAndDynamicTracks_RoundTrips()
    {
        var source = CreateAnimation();

        var bytes = AnimationFile.ConvertToBytes(source);
        var actual = AnimationFile.Create(new ByteChunk(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(actual.Header.Version, Is.EqualTo(8));
            Assert.That(actual.Header.UnknownValue_v8, Is.EqualTo(6));
            Assert.That(actual.Header.FlagVariables, Is.EqualTo(new[] { "flag_a" }));
            Assert.That(actual.Bones.Select(bone => bone.Name),
                Is.EqualTo(new[] { "root", "child" }));
            Assert.That(actual.AnimationParts, Has.Count.EqualTo(1));
        });

        var part = actual.AnimationParts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(part.TranslationMappings.Select(mapping => mapping.FileWriteValue),
                Is.EqualTo(new[] { 10000, 0 }));
            Assert.That(part.RotationMappings.Select(mapping => mapping.FileWriteValue),
                Is.EqualTo(new[] { 0, 10000 }));
            Assert.That(part.StaticFrame, Is.Not.Null);
            Assert.That(part.StaticFrame!.Transforms, Has.Count.EqualTo(1));
            Assert.That(part.StaticFrame.Quaternion, Has.Count.EqualTo(1));
            Assert.That(part.DynamicFrames, Has.Count.EqualTo(2));
        });

        AssertVector(part.StaticFrame!.Transforms.Single(), 1, 2, 3);
        AssertQuaternion(part.StaticFrame.Quaternion.Single(), 0, 0, 0, 1);
        AssertVector(part.DynamicFrames[0].Transforms.Single(), 4, 5, 6);
        AssertVector(part.DynamicFrames[1].Transforms.Single(), 7, 8, 9);
        AssertQuaternion(
            part.DynamicFrames[0].Quaternion.Single(),
            0,
            0.70710677f,
            0,
            0.70710677f);
        AssertQuaternion(
            part.DynamicFrames[1].Quaternion.Single(),
            0,
            -0.70710677f,
            0,
            0.70710677f);
    }

    [Test]
    public void ConvertToBytes_VersionEightWithMultipleParts_RoundTripsAllParts()
    {
        var source = CreateAnimation();
        source.AnimationParts.Add(CreatePart(10));

        var bytes = AnimationFile.ConvertToBytes(source);
        var actual = AnimationFile.Create(new ByteChunk(bytes));

        Assert.That(actual.AnimationParts, Has.Count.EqualTo(2));
        AssertVector(
            actual.AnimationParts[1].StaticFrame!.Transforms.Single(),
            11,
            12,
            13);
        AssertVector(
            actual.AnimationParts[1].DynamicFrames[1].Transforms.Single(),
            17,
            18,
            19);
    }

    [Test]
    public void ConvertToBytes_RealWarhammerThreeVersionEightAnimation_PreservesPose()
    {
        var dataRoot = PathHelper.GetDataFolder(
            @"Data\Karl_and_celestialgeneral_Pack");
        var animationPath = Path.Combine(
            dataRoot,
            @"animations\battle\humanoid01\2handed_hammer\stand\hu1_2hh_stand_idle_01.anim");
        var skeletonPath = Path.Combine(
            dataRoot,
            @"animations\skeletons\humanoid01.anim");
        var original = AnimationFile.Create(new ByteChunk(
            File.ReadAllBytes(animationPath)));
        var skeletonFile = AnimationFile.Create(new ByteChunk(
            File.ReadAllBytes(skeletonPath)));
        var skeleton = new GameSkeleton(
            skeletonFile,
            new AnimationPlayer());

        var bytes = AnimationFile.ConvertToBytes(original);
        var actual = AnimationFile.Create(new ByteChunk(bytes));
        var expectedClip = new AnimationClip(original, skeleton);
        var actualClip = new AnimationClip(actual, skeleton);

        Assert.Multiple(() =>
        {
            Assert.That(original.Header.Version, Is.EqualTo(8));
            Assert.That(actual.Header.Version, Is.EqualTo(8));
            Assert.That(actual.Header.UnknownValue_v8,
                Is.EqualTo(original.Header.UnknownValue_v8));
            Assert.That(actual.Header.FlagVariables,
                Is.EqualTo(original.Header.FlagVariables));
            Assert.That(actual.AnimationParts,
                Has.Count.EqualTo(original.AnimationParts.Count));
            Assert.That(actualClip.DynamicFrames,
                Has.Count.EqualTo(expectedClip.DynamicFrames.Count));
            Assert.That(actualClip.Duration,
                Is.EqualTo(expectedClip.Duration));
        });

        for (var frameIndex = 0;
             frameIndex < expectedClip.DynamicFrames.Count;
             frameIndex++)
        {
            for (var boneIndex = 0;
                 boneIndex < expectedClip.AnimationBoneCount;
                 boneIndex++)
            {
                var expectedPosition =
                    expectedClip.DynamicFrames[frameIndex].Position[boneIndex];
                var actualPosition =
                    actualClip.DynamicFrames[frameIndex].Position[boneIndex];
                Assert.That(
                    Vector3.Distance(expectedPosition, actualPosition),
                    Is.LessThanOrEqualTo(0.0001f),
                    $"Position mismatch at frame {frameIndex}, bone {boneIndex}.");

                var expectedRotation =
                    expectedClip.DynamicFrames[frameIndex].Rotation[boneIndex];
                var actualRotation =
                    actualClip.DynamicFrames[frameIndex].Rotation[boneIndex];
                expectedRotation.Normalize();
                actualRotation.Normalize();
                Assert.That(
                    MathF.Abs(Quaternion.Dot(expectedRotation, actualRotation)),
                    Is.GreaterThanOrEqualTo(0.9999f),
                    $"Rotation mismatch at frame {frameIndex}, bone {boneIndex}.");
            }
        }
    }

    private static AnimationFile CreateAnimation()
    {
        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 8,
                Unknown0_alwaysOne = 1,
                FrameRate = 20,
                SkeletonName = "test_skeleton",
                FlagCount = 1,
                FlagVariables = ["flag_a"],
                AnimationTotalPlayTimeInSec = 0.1f,
                UnknownValue_v8 = 6,
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = AnimationFile.BoneIndexNoParent,
                },
                new AnimationFile.BoneInfo
                {
                    Id = 1,
                    Name = "child",
                    ParentId = 0,
                },
            ],
        };
        animation.AnimationParts.Add(CreatePart(0));
        return animation;
    }

    private static AnimationFile.AnimationPart CreatePart(float offset)
    {
        var part = new AnimationFile.AnimationPart
        {
            TranslationMappings =
            [
                new AnimationFile.AnimationBoneMapping(10000),
                new AnimationFile.AnimationBoneMapping(0),
            ],
            RotationMappings =
            [
                new AnimationFile.AnimationBoneMapping(0),
                new AnimationFile.AnimationBoneMapping(10000),
            ],
            StaticFrame = new AnimationFile.Frame
            {
                Transforms = [new RmvVector3(1 + offset, 2 + offset, 3 + offset)],
                Quaternion = [new RmvVector4(0, 0, 0, 1)],
            },
        };
        part.DynamicFrames.Add(new AnimationFile.Frame
        {
            Transforms = [new RmvVector3(4 + offset, 5 + offset, 6 + offset)],
            Quaternion = [new RmvVector4(0, 0.70710677f, 0, 0.70710677f)],
        });
        part.DynamicFrames.Add(new AnimationFile.Frame
        {
            Transforms = [new RmvVector3(7 + offset, 8 + offset, 9 + offset)],
            Quaternion = [new RmvVector4(0, -0.70710677f, 0, 0.70710677f)],
        });
        return part;
    }

    private static void AssertVector(
        RmvVector3 actual,
        float x,
        float y,
        float z)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(y).Within(0.0001f));
            Assert.That(actual.Z, Is.EqualTo(z).Within(0.0001f));
        });
    }

    private static void AssertQuaternion(
        RmvVector4 actual,
        float x,
        float y,
        float z,
        float w)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(y).Within(0.0001f));
            Assert.That(actual.Z, Is.EqualTo(z).Within(0.0001f));
            Assert.That(actual.W, Is.EqualTo(w).Within(0.0001f));
        });
    }
}
