using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using GameWorld.Core.Animation;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.TestUtility;
using SharpGLTF.Schema2;
using Test.TestingUtility.TestUtility;
using Xna = Microsoft.Xna.Framework;

namespace Test.ImportExport.Importing.Importers.GltfImporterTest;

public class AnimationRoundTripTests
{
    private const string SkeletonPath = @"animations\skeletons\humanoid01.anim";
    private const string AnimationPath = @"animations\battle\humanoid01\2handed_hammer\stand\hu1_2hh_stand_idle_01.anim";


    [Test]
    public void ExportThenImport_RealHumanoidAnimation_PreservesLocalBoneTransforms()
    {
        var packFileService = PackFileSerivceTestHelper.Create(
            PathHelper.GetDataFolder("Data\\Karl_and_celestialgeneral_Pack"));
        var skeletonPackFile = packFileService.FindFile(SkeletonPath);
        var animationPackFile = packFileService.FindFile(AnimationPath);
        Assert.That(skeletonPackFile, Is.Not.Null);
        Assert.That(animationPackFile, Is.Not.Null);

        var skeleton = AnimationFile.Create(skeletonPackFile!);
        var originalAnimation = AnimationFile.Create(animationPackFile!);
        var modelRoot = ModelRoot.CreateModel();
        var exportSettings = new RmvToGltfExporterSettings(
            new PackFile("model.rigid_model_v2", new MemorySource([])),
            [animationPackFile!],
            "roundtrip.gltf",
            false,
            false,
            false,
            true,
            true);
        var gltfSkeleton = new GltfSkeletonBuilder(packFileService)
            .CreateSkeleton(skeleton, modelRoot, exportSettings);
        new GltfAnimationBuilder(packFileService).Build(
            skeleton,
            exportSettings,
            gltfSkeleton,
            modelRoot);

        var importedAnimation = AnimationBuilder.Build(
            new AnimationBuilderSettings(
                modelRoot,
                skeleton.Header.SkeletonName,
                originalAnimation.Header.FrameRate,
                new PackFileContainer("roundtrip"),
                "animations"),
            skeleton,
            modelRoot.LogicalAnimations.Single());

        var gameSkeleton = new GameSkeleton(skeleton, null!);
        var originalClip = new AnimationClip(originalAnimation, gameSkeleton);
        var importedClip = new AnimationClip(importedAnimation, gameSkeleton);
        var maxTranslationError = 0.0f;
        var maxRotationError = 0.0f;
        var maxWorldPositionError = 0.0f;
        var translationLocation = "";
        var rotationLocation = "";
        var worldPositionLocation = "";

        var comparedFrameCount = Math.Min(
            originalClip.DynamicFrames.Count,
            importedClip.DynamicFrames.Count);
        for (var frameIndex = 0; frameIndex < comparedFrameCount; frameIndex++)
        {
            var originalFrame = originalClip.DynamicFrames[frameIndex];
            var importedFrame = importedClip.DynamicFrames[frameIndex];
            var originalWorldTransforms = BuildWorldTransforms(originalFrame, gameSkeleton);
            var importedWorldTransforms = BuildWorldTransforms(importedFrame, gameSkeleton);
            for (var boneIndex = 0; boneIndex < originalClip.AnimationBoneCount; boneIndex++)
            {
                var translationError = Xna.Vector3.Distance(
                    originalFrame.Position[boneIndex],
                    importedFrame.Position[boneIndex]);
                if (translationError > maxTranslationError)
                {
                    maxTranslationError = translationError;
                    translationLocation = $"frame {frameIndex}, bone {boneIndex} ({skeleton.Bones[boneIndex].Name})";
                }

                var originalRotation = Xna.Quaternion.Normalize(
                    originalFrame.Rotation[boneIndex]);
                var importedRotation = Xna.Quaternion.Normalize(
                    importedFrame.Rotation[boneIndex]);
                var quaternionDot = Math.Clamp(
                    Math.Abs(Xna.Quaternion.Dot(originalRotation, importedRotation)),
                    0.0f,
                    1.0f);
                var rotationError = 2.0f * MathF.Acos(quaternionDot);
                if (rotationError > maxRotationError)
                {
                    maxRotationError = rotationError;
                    rotationLocation = $"frame {frameIndex}, bone {boneIndex} ({skeleton.Bones[boneIndex].Name})";
                }

                var worldPositionError = Xna.Vector3.Distance(
                    originalWorldTransforms[boneIndex].Translation,
                    importedWorldTransforms[boneIndex].Translation);
                if (worldPositionError > maxWorldPositionError)
                {
                    maxWorldPositionError = worldPositionError;
                    worldPositionLocation = $"frame {frameIndex}, bone {boneIndex} ({skeleton.Bones[boneIndex].Name})";
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                importedClip.DynamicFrames,
                Has.Count.EqualTo(originalClip.DynamicFrames.Count));
            Assert.That(
                maxTranslationError,
                Is.LessThan(0.0001f),
                $"最大局部平移误差位于 {translationLocation}");
            Assert.That(
                maxRotationError,
                Is.LessThan(0.002f),
                $"最大局部旋转误差位于 {rotationLocation}");
            Assert.That(
                maxWorldPositionError,
                Is.LessThan(0.0001f),
                $"最大骨骼世界位置误差位于 {worldPositionLocation}");
        });
    }

    private static Xna.Matrix[] BuildWorldTransforms(
        AnimationClip.KeyFrame frame,
        GameSkeleton skeleton)
    {
        var worldTransforms = new Xna.Matrix[skeleton.BoneCount];
        var completed = new bool[skeleton.BoneCount];

        Xna.Matrix BuildWorldTransform(int boneIndex)
        {
            if (completed[boneIndex])
                return worldTransforms[boneIndex];

            var localTransform =
                Xna.Matrix.CreateScale(frame.Scale[boneIndex]) *
                Xna.Matrix.CreateFromQuaternion(frame.Rotation[boneIndex]) *
                Xna.Matrix.CreateTranslation(frame.Position[boneIndex]);
            var parentBoneIndex = skeleton.GetParentBoneIndex(boneIndex);
            worldTransforms[boneIndex] = parentBoneIndex == -1
                ? localTransform
                : localTransform * BuildWorldTransform(parentBoneIndex);
            completed[boneIndex] = true;
            return worldTransforms[boneIndex];
        }

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            BuildWorldTransform(boneIndex);

        return worldTransforms;
    }
}
