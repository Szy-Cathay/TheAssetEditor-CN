using Editors.AnimationVisualEditors.AnimationWorkbench;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public sealed class AnimationWorkbenchRetargetingTests
{
    [TestMethod]
    public void CreateRetargetMapping_ReportsConfidenceAndUnmappedTargetBones()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("animroot", -1),
            ("root", 0),
            ("spine_0", 1),
            ("hand_left", 2));
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1),
            ("Bip", 0),
            ("spine_01", 1),
            ("hand_l", 2),
            ("cape", 1));
        using var document = CreateDocument(sourceSkeleton, targetSkeleton);

        var result = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(targetSkeleton.BoneCount, result.Mappings.Count);
        Assert.AreEqual(
            AnimationWorkbenchRetargetConfidence.High,
            result.Mappings.Single(item => item.TargetBoneName == "root")
                .Confidence);
        Assert.AreEqual(
            AnimationWorkbenchRetargetConfidence.Medium,
            result.Mappings.Single(item => item.TargetBoneName == "spine_01")
                .Confidence);
        Assert.AreEqual(
            "hand_left",
            result.Mappings.Single(item => item.TargetBoneName == "hand_l")
                .SourceBoneName);
        Assert.IsTrue(
            result.Mappings.Single(item => item.TargetBoneName == "Bip")
                .IsCoreBone);
        Assert.IsNull(
            result.Mappings.Single(item => item.TargetBoneName == "cape")
                .SourceBoneIndex);
        Assert.IsTrue(result.Diagnostics.Any(item =>
            item.Code == AnimationWorkbenchDiagnosticCode
                .RetargetNonCoreBoneUnmapped &&
            item.BoneName == "cape" &&
            item.Severity == AnimationWorkbenchDiagnosticSeverity.Warning));
    }

    [TestMethod]
    public void PreviewRetarget_CoreBoneIsUnmapped_BlocksGeneration()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("root", -1),
            ("spine_0", 0));
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1),
            ("spine_01", 0),
            ("hand_l", 1));
        using var document = CreateDocument(sourceSkeleton, targetSkeleton);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mapping.Mappings));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.State.HasActiveRetargetPreview);
        Assert.IsTrue(result.Diagnostics.Any(item =>
            item.Code == AnimationWorkbenchDiagnosticCode
                .RetargetCoreBoneUnmapped &&
            item.BoneName == "hand_l" &&
            item.Severity == AnimationWorkbenchDiagnosticSeverity.Error));
    }

    [TestMethod]
    public void PreviewRetarget_DuplicateTargetMapping_BlocksGeneration()
    {
        var skeleton = CreateSkeleton(
            "skeleton",
            ("root", -1),
            ("spine_0", 0));
        using var document = CreateDocument(skeleton, skeleton);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);
        var duplicateMappings = mapping.Mappings
            .Append(mapping.Mappings[0])
            .ToArray();

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                duplicateMappings));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(item =>
            item.Code == AnimationWorkbenchDiagnosticCode
                .RetargetMappingTargetDuplicate));
    }

    [TestMethod]
    public void PreviewRetarget_ParentOrderConflicts_BlocksGeneration()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("root", -1),
            ("spine_0", 0),
            ("hand_left", 1));
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1),
            ("spine_01", 0),
            ("hand_l", 1));
        using var document = CreateDocument(sourceSkeleton, targetSkeleton);
        var automatic = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);
        var conflicting = automatic.Mappings
            .Select(item => item.TargetBoneName switch
            {
                "spine_01" => item with
                {
                    SourceBoneIndex = 2,
                    SourceBoneName = "hand_left",
                    Confidence = AnimationWorkbenchRetargetConfidence.Manual,
                },
                "hand_l" => item with
                {
                    SourceBoneIndex = 1,
                    SourceBoneName = "spine_0",
                    Confidence = AnimationWorkbenchRetargetConfidence.Manual,
                },
                _ => item,
            })
            .ToArray();

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                conflicting));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(item =>
            item.Code == AnimationWorkbenchDiagnosticCode
                .RetargetParentConflict &&
            item.BoneName == "hand_l"));
    }

    [TestMethod]
    public void PreviewRetarget_CrossSkeleton_UsesBindWorldAnimationDelta()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("root", -1));
        sourceSkeleton.Rotation[0] = Quaternion.CreateFromAxisAngle(
            Vector3.UnitX,
            MathHelper.ToRadians(90));
        sourceSkeleton.RebuildSkeletonMatrix();
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1));
        targetSkeleton.Translation[0] = new Vector3(3, 2, 1);
        targetSkeleton.Rotation[0] = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(-90));
        targetSkeleton.RebuildSkeletonMatrix();
        var sourceClip = CreateClip(sourceSkeleton);
        sourceClip.DynamicFrames[0].Rotation[0] = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathHelper.ToRadians(35));
        using var document = CreateDocument(
            sourceSkeleton,
            targetSkeleton,
            sourceClip);
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mapping.Mappings));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.State.HasActiveRetargetPreview);
        var actualFrame = result.State.CurrentPreview!.Animation.DynamicFrames[0];
        var sourceAnimatedWorld = Matrix.CreateFromQuaternion(
            sourceClip.DynamicFrames[0].Rotation[0]);
        var expectedWorld = targetSkeleton.GetWorldTransform(0) *
            (Matrix.Invert(sourceSkeleton.GetWorldTransform(0)) *
             sourceAnimatedWorld);
        expectedWorld.Decompose(
            out _,
            out var expectedRotation,
            out var expectedPosition);
        Assert.IsTrue(Vector3.Distance(
            expectedPosition,
            actualFrame.Position[0]) < 0.0001f);
        Assert.IsTrue(MathF.Abs(Quaternion.Dot(
            Quaternion.Normalize(expectedRotation),
            Quaternion.Normalize(actualFrame.Rotation[0]))) > 0.9999f);
    }

    [TestMethod]
    public void PreviewRetarget_SameSkeleton_UsesIdentityFastPath()
    {
        var skeleton = CreateSkeleton(
            "skeleton",
            ("root", -1),
            ("cape", 0));
        var clip = CreateClip(skeleton);
        clip.DynamicFrames[0].Scale[0] = new Vector3(2, 2, 2);
        clip.DynamicFrames[0].Position[1] = new Vector3(4, 5, 6);
        using var document = CreateDocument(skeleton, skeleton, clip);
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mapping.Mappings));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(
            new Vector3(4, 5, 6),
            result.State.CurrentPreview!.Animation.DynamicFrames[0]
                .Position[1]);
        Assert.AreEqual(
            new Vector3(2, 2, 2),
            result.State.CurrentPreview!.Animation.DynamicFrames[0]
                .Scale[0]);
        Assert.AreEqual(
            1,
            mapping.Mappings.Single(item =>
                item.TargetBoneName == "cape").SourceBoneIndex);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void PreviewRetarget_ThreeKingdomsDocument_UsesCrossSkeletonPath()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("root", -1),
            ("spine_0", 0));
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1),
            ("spine_01", 0));
        using var document = CreateDocument(
            sourceSkeleton,
            targetSkeleton,
            targetGame: GameTypeEnum.ThreeKingdoms);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mapping.Mappings));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(
            GameTypeEnum.ThreeKingdoms,
            result.State.TargetGame);
        Assert.IsTrue(result.State.HasActiveRetargetPreview);
    }

    [TestMethod]
    public void PreviewRetarget_SingularBindPose_BlocksGeneration()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("root", -1));
        sourceSkeleton.Scale[0] = 0;
        sourceSkeleton.RebuildSkeletonMatrix();
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1));
        using var document = CreateDocument(sourceSkeleton, targetSkeleton);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mapping.Mappings));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(item =>
            item.Code == AnimationWorkbenchDiagnosticCode
                .RetargetBindPoseIncompatible));
    }

    [TestMethod]
    public void CommitRetargetAnimationB_LayerUsesPreparedTargetSkeleton()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("root", -1),
            ("spine_0", 0));
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("root", -1),
            ("spine_01", 0));
        using var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(targetSkeleton),
                targetSkeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            new AnimationWorkbenchSourceInput(
                "animation_b",
                CreateClip(sourceSkeleton),
                sourceSkeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            GameTypeEnum.Warhammer3,
            targetSkeleton));
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationB);
        var preview = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationB,
                mapping.Mappings));

        var commit = document.CommitRetargetPreview();
        var layer = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            new AnimationWorkbenchBoneMask(
                targetSkeleton.SkeletonName,
                ["spine_01"]),
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsTrue(preview.Succeeded);
        Assert.IsTrue(commit.Succeeded);
        Assert.IsTrue(commit.State.HasRetargetedAnimationB);
        Assert.IsTrue(layer.Succeeded);
    }

    [TestMethod]
    public void RetargetController_ManualMappingCanBeSavedAndRestored()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-workbench-retarget-{Guid.NewGuid():N}");
        try
        {
            var sourceSkeleton = CreateSkeleton(
                "source_skeleton",
                ("root", -1),
                ("spine_0", 0),
                ("hand_left", 1),
                ("weapon", 1));
            var targetSkeleton = CreateSkeleton(
                "target_skeleton",
                ("root", -1),
                ("spine_01", 0),
                ("weapon_socket", 1));
            var store = CharacterRetargetProfileStore.CreateForFile(
                Path.Combine(directory, "profiles.json"));
            using var document = CreateDocument(
                sourceSkeleton,
                targetSkeleton);
            var first = new AnimationWorkbenchRetargetController(
                document,
                AnimationWorkbenchSourceSlot.AnimationA,
                store);

            first.SetMapping(targetBoneIndex: 2, sourceBoneIndex: 3);
            Assert.IsTrue(first.Mappings.Single(item =>
                item.TargetBoneIndex == 2).ApplyTranslation);
            var saved = first.SaveProfile();
            first.ReleasePreview();
            var restored = new AnimationWorkbenchRetargetController(
                document,
                AnimationWorkbenchSourceSlot.AnimationA,
                store);

            Assert.IsTrue(saved.Succeeded);
            Assert.AreEqual(
                3,
                restored.Mappings.Single(item =>
                    item.TargetBoneIndex == 2).SourceBoneIndex);
            Assert.AreEqual(
                AnimationWorkbenchRetargetConfidence.Manual,
                restored.Mappings.Single(item =>
                    item.TargetBoneIndex == 2).Confidence);
            Assert.IsTrue(restored.Mappings.Single(item =>
                item.TargetBoneIndex == 2).ApplyTranslation);
            Assert.IsTrue(restored.HasLoadedProfile);
            restored.ReleasePreview();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PreviewRetarget_TargetChildPrecedesParent_GeneratesPreview()
    {
        var sourceSkeleton = CreateSkeleton(
            "source_skeleton",
            ("hand_left", 1),
            ("root", -1));
        var targetSkeleton = CreateSkeleton(
            "target_skeleton",
            ("hand_l", 1),
            ("root", -1));
        using var document = CreateDocument(sourceSkeleton, targetSkeleton);
        var mapping = document.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA);

        var result = document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mapping.Mappings));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.State.HasActiveRetargetPreview);
    }

    [TestMethod]
    public void RetargetController_CorruptProfileUsesAutoMappingWithDiagnostic()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-workbench-retarget-corrupt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var profilePath = Path.Combine(directory, "profiles.json");
            File.WriteAllText(profilePath, "{ invalid json");
            var skeleton = CreateSkeleton(
                "skeleton",
                ("root", -1));
            using var document = CreateDocument(skeleton, skeleton);

            var controller = new AnimationWorkbenchRetargetController(
                document,
                AnimationWorkbenchSourceSlot.AnimationA,
                CharacterRetargetProfileStore.CreateForFile(profilePath));

            Assert.IsTrue(controller.HasActivePreview);
            Assert.IsTrue(controller.Diagnostics.Any(item =>
                item.Code == AnimationWorkbenchDiagnosticCode
                    .RetargetProfileReadFailed));
            controller.ReleasePreview();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RetargetController_ReloadedDocumentRejectsOldCommit()
    {
        var skeleton = CreateSkeleton(
            "skeleton",
            ("root", -1));
        using var document = CreateDocument(skeleton, skeleton);
        var controller = new AnimationWorkbenchRetargetController(
            document,
            AnimationWorkbenchSourceSlot.AnimationA,
            CharacterRetargetProfileStore.CreateForFile(Path.Combine(
                Path.GetTempPath(),
                $"ae-workbench-retarget-stale-{Guid.NewGuid():N}.json")));
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(skeleton),
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));

        var result = controller.CommitPreview();

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(item =>
            item.Code == AnimationWorkbenchDiagnosticCode
                .RetargetDocumentChanged));
        Assert.IsFalse(document.GetState().HasRetargetedAnimationA);
    }

    private static AnimationWorkbenchDocument CreateDocument(
        GameSkeleton sourceSkeleton,
        GameSkeleton targetSkeleton,
        AnimationClip? sourceClip = null,
        GameTypeEnum targetGame = GameTypeEnum.Warhammer3)
    {
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                sourceClip ?? CreateClip(sourceSkeleton),
                sourceSkeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            targetGame,
            targetSkeleton));
        return document;
    }

    private static AnimationClip CreateClip(GameSkeleton skeleton)
    {
        var frame = new AnimationClip.KeyFrame();
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            frame.Position.Add(skeleton.Translation[boneIndex]);
            frame.Rotation.Add(skeleton.Rotation[boneIndex]);
            frame.Scale.Add(Vector3.One);
        }
        var clip = new AnimationClip
        {
            Duration = AnimationTimebase.FromFramesPerSecond(1, 1).Duration,
        };
        clip.DynamicFrames.Add(frame);
        return clip;
    }

    private static GameSkeleton CreateSkeleton(
        string name,
        params (string Name, int ParentIndex)[] bones)
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = name,
            },
            Bones = bones
                .Select((bone, index) => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = bone.Name,
                    ParentId = bone.ParentIndex,
                })
                .ToArray(),
        };
        var frame = new AnimationFile.Frame();
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            frame.Transforms.Add(new RmvVector3(
                boneIndex == 0 ? 0 : 1,
                0,
                0));
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return new GameSkeleton(file, new AnimationPlayer());
    }
}
