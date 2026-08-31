using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public class AnimationWorkbenchBaseAnimationCompletionTests
{
    [DataTestMethod]
    [DataRow(
        @"animations\battle\humanoid01\stand\hu1_stand_idle_01.anim",
        AnimationWorkbenchBaseAnimationRole.Idle)]
    [DataRow(
        @"animations\battle\humanoid01\locomotion\hu1_walk_forward.anim",
        AnimationWorkbenchBaseAnimationRole.Walk)]
    [DataRow(
        @"animations\battle\humanoid01\locomotion\hu1_run_forward.anim",
        AnimationWorkbenchBaseAnimationRole.Run)]
    [DataRow(
        @"animations\battle\humanoid01\reactions\hu1_hit_back.anim",
        AnimationWorkbenchBaseAnimationRole.HitReaction)]
    [DataRow(
        @"animations\battle\humanoid01\deaths\hu1_death_front.anim",
        AnimationWorkbenchBaseAnimationRole.Death)]
    [DataRow(
        @"animations\battle\humanoid01\attack\hu1_attack_01.anim",
        AnimationWorkbenchBaseAnimationRole.Other)]
    [DataRow(
        @"animations\battle\humanoid01\locomotion\hu1_stand_to_walk.anim",
        AnimationWorkbenchBaseAnimationRole.Other)]
    public void Classify_RecognizesBaseAnimationRoles(
        string path,
        AnimationWorkbenchBaseAnimationRole expected)
    {
        Assert.AreEqual(
            expected,
            AnimationWorkbenchBaseAnimationClassifier.Classify(path));
    }

    [TestMethod]
    public void GetFamilyRoot_KeepsSiblingBaseAnimationFoldersTogether()
    {
        var root = AnimationWorkbenchBaseAnimationClassifier.GetFamilyRoot(
            @"animations\battle\humanoid01\sword_and_shield\stand\idle.anim");

        Assert.AreEqual(
            @"animations\battle\humanoid01\sword_and_shield",
            root);
        Assert.IsTrue(AnimationWorkbenchBaseAnimationClassifier.IsInFamily(
            @"animations\battle\humanoid01\sword_and_shield\deaths\death.anim",
            root));
        Assert.IsFalse(AnimationWorkbenchBaseAnimationClassifier.IsInFamily(
            @"animations\battle\humanoid01\spear\deaths\death.anim",
            root));
    }

    [TestMethod]
    public void Generate_StyleReferencePreservesDonorRootMotionAndWritesV8()
    {
        var donorSkeleton = CreateSkeleton(
            "donor_skeleton",
            ("root", -1),
            ("spine_0", 0));
        var targetSkeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var donor = CreateClip(donorSkeleton, frameCount: 2);
        donor.DynamicFrames[1].Position[0] = new Vector3(10, 0, 0);
        var style = CreateClip(targetSkeleton, frameCount: 2);
        style.DynamicFrames[0].Position[1] = new Vector3(1, 0, 0);
        style.DynamicFrames[1].Position[1] = new Vector3(3, 0, 0);
        var originalStyleRoot = style.DynamicFrames[1].Position[0];
        using var mappingDocument = new AnimationWorkbenchDocument();
        mappingDocument.Load(new AnimationWorkbenchLoadRequest(
            CreateSource("donor", donor, donorSkeleton),
            null,
            GameTypeEnum.Warhammer3,
            targetSkeleton));
        var mappings = mappingDocument.CreateRetargetMapping(
            AnimationWorkbenchSourceSlot.AnimationA).Mappings;
        var module = new AnimationWorkbenchBaseAnimationCompletionModule();

        var result = module.Generate(new AnimationWorkbenchBaseAnimationRequest(
            [
                new AnimationWorkbenchBaseAnimationRecipeItem(
                    @"animations\battle\donor\run\run_forward.anim",
                    @"animations\battle\external\base\run\run_forward.anim",
                    AnimationWorkbenchBaseAnimationRole.Run,
                    CreateSource("run_forward", donor, donorSkeleton)),
            ],
            targetSkeleton,
            CreateSource("external_style", style, targetSkeleton),
            AnimationWorkbenchBaseAnimationStyleMode.PreserveMotion,
            StyleWeight: 0.5,
            IncludeRootMotion: false,
            mappings,
            new AnimationWorkbenchSourceFormat(8, 1)),
            progress: null,
            CancellationToken.None);

        var candidate = result.Items.Single();
        Assert.AreEqual(
            AnimationWorkbenchBaseAnimationItemStatus.Ready,
            candidate.Status);
        Assert.IsNotNull(candidate.Bytes);
        Assert.IsNotNull(candidate.PreviewAnimation);
        Assert.AreEqual(
            new Vector3(10, 0, 0),
            candidate.PreviewAnimation.DynamicFrames[1].Position[0]);
        Assert.AreEqual(
            new Vector3(2, 0, 0),
            candidate.PreviewAnimation.DynamicFrames[1].Position[1]);
        Assert.AreEqual(
            originalStyleRoot,
            style.DynamicFrames[1].Position[0]);

        var roundTrip = AnimationFile.Create(new ByteChunk(candidate.Bytes));
        Assert.AreEqual(8u, roundTrip.Header.Version);
        Assert.AreEqual("external_skeleton", roundTrip.Header.SkeletonName);
    }

    [TestMethod]
    public void Generate_BlocksOutputsThatWouldOverwriteInputAnimations()
    {
        var skeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var clip = CreateClip(skeleton, frameCount: 2);
        const string donorPath =
            @"animations\battle\donor\idle\idle.anim";
        const string stylePath =
            @"animations\battle\external\original.anim";
        var request = new AnimationWorkbenchBaseAnimationRequest(
            [
                new AnimationWorkbenchBaseAnimationRecipeItem(
                    donorPath,
                    donorPath,
                    AnimationWorkbenchBaseAnimationRole.Idle,
                    CreateSource(donorPath, clip, skeleton)),
                new AnimationWorkbenchBaseAnimationRecipeItem(
                    @"animations\battle\donor\walk\walk.anim",
                    stylePath,
                    AnimationWorkbenchBaseAnimationRole.Walk,
                    CreateSource("walk", clip, skeleton)),
            ],
            skeleton,
            CreateSource(stylePath, clip, skeleton),
            AnimationWorkbenchBaseAnimationStyleMode.PreserveMotion,
            StyleWeight: 0.25,
            IncludeRootMotion: false,
            Mappings: null,
            new AnimationWorkbenchSourceFormat(8, 1));

        var result = new AnimationWorkbenchBaseAnimationCompletionModule()
            .Generate(request, progress: null, CancellationToken.None);

        Assert.IsTrue(result.Items.All(item =>
            item.Status == AnimationWorkbenchBaseAnimationItemStatus.Failed));
        Assert.IsTrue(result.Items.All(item => item.Diagnostics.Any(
            diagnostic => diagnostic.Code ==
                AnimationWorkbenchDiagnosticCode
                    .BaseAnimationSourceOverwriteBlocked)));
    }

    [TestMethod]
    public void Generate_MissingBaseRoleBlocksAnimationSet()
    {
        var skeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var clip = CreateClip(skeleton, frameCount: 2);
        var request = new AnimationWorkbenchBaseAnimationRequest(
            [
                CreateRecipe(
                    "idle",
                    @"animations\battle\external\base\idle\idle.anim",
                    AnimationWorkbenchBaseAnimationRole.Idle,
                    clip,
                    skeleton),
                CreateRecipe(
                    "walk",
                    @"animations\battle\external\base\walk\walk.anim",
                    AnimationWorkbenchBaseAnimationRole.Walk,
                    clip,
                    skeleton),
                CreateRecipe(
                    "run",
                    @"animations\battle\external\base\run\run.anim",
                    AnimationWorkbenchBaseAnimationRole.Run,
                    clip,
                    skeleton),
                CreateRecipe(
                    "hit",
                    @"animations\battle\external\base\hit\hit.anim",
                    AnimationWorkbenchBaseAnimationRole.HitReaction,
                    clip,
                    skeleton),
            ],
            skeleton,
            StyleReference: null,
            AnimationWorkbenchBaseAnimationStyleMode.None,
            StyleWeight: 0,
            IncludeRootMotion: false,
            Mappings: null,
            new AnimationWorkbenchSourceFormat(8, 1))
        {
            AnimationSetOutputPath =
                @"animations\database\battle\bin\ext_external_base.animpack",
        };

        var result = new AnimationWorkbenchBaseAnimationCompletionModule()
            .Generate(request, progress: null, CancellationToken.None);

        Assert.IsNotNull(result.AnimationSet);
        Assert.AreEqual(
            AnimationWorkbenchBaseAnimationItemStatus.Failed,
            result.AnimationSet.Status);
        Assert.IsNull(result.AnimationSet.Bytes);
        Assert.IsTrue(result.AnimationSet.Diagnostics.Any(diagnostic =>
            diagnostic.Code == AnimationWorkbenchDiagnosticCode
                .BaseAnimationSetIncomplete));
    }

    [TestMethod]
    public void Generate_PreCancelledRequestDoesNotCreatePartialAnimationSet()
    {
        var skeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var clip = CreateClip(skeleton, frameCount: 2);
        var request = CreateCompleteRequest(skeleton, clip);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new AnimationWorkbenchBaseAnimationCompletionModule()
            .Generate(request, progress: null, cancellation.Token);

        Assert.IsTrue(result.Items.All(item => item.Status ==
            AnimationWorkbenchBaseAnimationItemStatus.NotProcessed));
        Assert.AreEqual(
            AnimationWorkbenchBaseAnimationItemStatus.NotProcessed,
            result.AnimationSet?.Status);
        Assert.IsNull(result.AnimationSet?.Bytes);
    }

    [TestMethod]
    public async Task SaveReadyCandidates_WritesOneAtomicBatch()
    {
        var skeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var clip = CreateClip(skeleton, frameCount: 2);
        var module = new AnimationWorkbenchBaseAnimationCompletionModule();
        var request = new AnimationWorkbenchBaseAnimationRequest(
                [
                    CreateRecipe(
                        "idle",
                        @"animations\battle\external\base\idle\idle.anim",
                        AnimationWorkbenchBaseAnimationRole.Idle,
                        clip,
                        skeleton),
                    CreateRecipe(
                        "death",
                        @"animations\battle\external\base\death\death.anim",
                        AnimationWorkbenchBaseAnimationRole.Death,
                        clip,
                        skeleton),
                    CreateRecipe(
                        "walk",
                        @"animations\battle\external\base\walk\walk.anim",
                        AnimationWorkbenchBaseAnimationRole.Walk,
                        clip,
                        skeleton),
                    CreateRecipe(
                        "run",
                        @"animations\battle\external\base\run\run.anim",
                        AnimationWorkbenchBaseAnimationRole.Run,
                        clip,
                        skeleton),
                    CreateRecipe(
                        "hit",
                        @"animations\battle\external\base\hit\hit.anim",
                        AnimationWorkbenchBaseAnimationRole.HitReaction,
                        clip,
                        skeleton),
                ],
                skeleton,
                StyleReference: null,
                AnimationWorkbenchBaseAnimationStyleMode.None,
                StyleWeight: 0,
                IncludeRootMotion: false,
                Mappings: null,
                new AnimationWorkbenchSourceFormat(8, 1))
        {
            AnimationSetOutputPath =
                    @"animations\database\battle\bin\ext_external_base.animpack",
        };
        var generated = module.Generate(
            request,
            progress: null,
            CancellationToken.None);
        var projectPath = Path.Combine(
            Path.GetTempPath(),
            $"ae-base-animation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectPath);
        try
        {
            using var project = FolderProjectContainer.Create(
                projectPath,
                new FolderProjectSettings { Name = "test" });
            IReadOnlyCollection<PackFileWrite>? capturedWrites = null;
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.ApplyFileWritesAsync(
                    project,
                    It.IsAny<IReadOnlyCollection<PackFileWrite>>(),
                    false,
                    CancellationToken.None))
                .Callback((
                    PackFileContainer _,
                    IReadOnlyCollection<PackFileWrite> writes,
                    bool _,
                    CancellationToken _) =>
                    capturedWrites = writes)
                .ReturnsAsync([]);

            var saved = await module.SaveReadyCandidatesAsync(
                packFileService.Object,
                project,
                generated,
                overwriteExisting: false,
                CancellationToken.None);

            packFileService.Verify(service => service.ApplyFileWritesAsync(
                project,
                It.IsAny<IReadOnlyCollection<PackFileWrite>>(),
                false,
                CancellationToken.None), Times.Once);
            Assert.AreEqual(6, capturedWrites?.Count);
            Assert.AreEqual(5, saved.SavedCount);
            Assert.IsTrue(saved.Items.All(item => item.Status ==
                AnimationWorkbenchBaseAnimationItemStatus.Saved));
            Assert.AreEqual(
                AnimationWorkbenchBaseAnimationItemStatus.Saved,
                saved.AnimationSet?.Status);
        }
        finally
        {
            if (Directory.Exists(projectPath))
                Directory.Delete(projectPath, recursive: true);
        }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SaveReadyCandidates_ConflictLeavesWholeBatchRetryable(
        bool animationSetConflict)
    {
        var skeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var clip = CreateClip(skeleton, frameCount: 2);
        var module = new AnimationWorkbenchBaseAnimationCompletionModule();
        var generated = module.Generate(
            CreateCompleteRequest(skeleton, clip),
            progress: null,
            CancellationToken.None);
        var projectPath = Path.Combine(
            Path.GetTempPath(),
            $"ae-base-animation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectPath);
        try
        {
            using var project = FolderProjectContainer.Create(
                projectPath,
                new FolderProjectSettings { Name = "test" });
            var conflictPath = animationSetConflict
                ? generated.AnimationSet!.OutputPath
                : generated.Items.First().OutputPath;
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.ApplyFileWritesAsync(
                    project,
                    It.IsAny<IReadOnlyCollection<PackFileWrite>>(),
                    false,
                    CancellationToken.None))
                .ThrowsAsync(new FolderProjectFileConflictException(
                    [conflictPath]));

            var saved = await module.SaveReadyCandidatesAsync(
                packFileService.Object,
                project,
                generated,
                overwriteExisting: false,
                CancellationToken.None);

            Assert.AreEqual(0, saved.SavedCount);
            Assert.AreEqual(animationSetConflict ? 0 : 1, saved.SkippedCount);
            Assert.AreEqual(animationSetConflict ? 5 : 4, saved.ReadyCount);
            Assert.AreEqual(
                animationSetConflict
                    ? AnimationWorkbenchBaseAnimationItemStatus.Skipped
                    : AnimationWorkbenchBaseAnimationItemStatus.Ready,
                saved.AnimationSet?.Status);

            packFileService
                .Setup(service => service.ApplyFileWritesAsync(
                    project,
                    It.IsAny<IReadOnlyCollection<PackFileWrite>>(),
                    true,
                    CancellationToken.None))
                .ReturnsAsync([]);

            var retried = await module.SaveReadyCandidatesAsync(
                packFileService.Object,
                project,
                saved,
                overwriteExisting: true,
                CancellationToken.None);

            Assert.AreEqual(5, retried.SavedCount);
            Assert.AreEqual(0, retried.SkippedCount);
            Assert.AreEqual(
                AnimationWorkbenchBaseAnimationItemStatus.Saved,
                retried.AnimationSet?.Status);
        }
        finally
        {
            if (Directory.Exists(projectPath))
                Directory.Delete(projectPath, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_CreatesWarhammer3AnimationSetWithBaseSlots()
    {
        var skeleton = CreateSkeleton(
            "external_skeleton",
            ("root", -1),
            ("spine_01", 0));
        var clip = CreateClip(skeleton, frameCount: 2);
        var request = new AnimationWorkbenchBaseAnimationRequest(
            [
                CreateRecipe(
                    "idle",
                    @"animations\battle\external\base\idle\idle.anim",
                    AnimationWorkbenchBaseAnimationRole.Idle,
                    clip,
                    skeleton),
                CreateRecipe(
                    "death",
                    @"animations\battle\external\base\death\death.anim",
                    AnimationWorkbenchBaseAnimationRole.Death,
                    clip,
                    skeleton),
                CreateRecipe(
                    "walk",
                    @"animations\battle\external\base\walk\walk.anim",
                    AnimationWorkbenchBaseAnimationRole.Walk,
                    clip,
                    skeleton),
                CreateRecipe(
                    "run",
                    @"animations\battle\external\base\run\run.anim",
                    AnimationWorkbenchBaseAnimationRole.Run,
                    clip,
                    skeleton),
                CreateRecipe(
                    "hit",
                    @"animations\battle\external\base\hit\hit.anim",
                    AnimationWorkbenchBaseAnimationRole.HitReaction,
                    clip,
                    skeleton),
            ],
            skeleton,
            StyleReference: null,
            AnimationWorkbenchBaseAnimationStyleMode.None,
            StyleWeight: 0,
            IncludeRootMotion: false,
            Mappings: null,
            new AnimationWorkbenchSourceFormat(8, 1))
        {
            AnimationSetOutputPath =
                @"animations\database\battle\bin\ext_external_base.animpack",
        };

        var result = new AnimationWorkbenchBaseAnimationCompletionModule()
            .Generate(request, progress: null, CancellationToken.None);

        Assert.IsNotNull(result.AnimationSet);
        Assert.AreEqual(
            AnimationWorkbenchBaseAnimationItemStatus.Ready,
            result.AnimationSet.Status);
        Assert.IsNotNull(result.AnimationSet.Bytes);
        var pack = PackFile.CreateFromBytes(
            result.AnimationSet.OutputPath,
            result.AnimationSet.Bytes);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(pack, null))
            .Returns(result.AnimationSet.OutputPath);
        var database = AnimationPackSerializer.Load(
            pack,
            packFileService.Object);
        var bin = (AnimationBinWh3)database.Files.Single();
        var slots = ((IAnimationBinGenericFormat)bin).Entries
            .Select(entry => entry.SlotName)
            .ToArray();

        Assert.AreEqual("external_skeleton", bin.SkeletonName);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "STAND_IDLE_1",
                "WALK_1",
                "RUN_1",
                "HIT_REACTION_STAND_1",
                "DEATH_STAND_1",
            },
            slots);
    }

    private static AnimationWorkbenchBaseAnimationRecipeItem CreateRecipe(
        string name,
        string outputPath,
        AnimationWorkbenchBaseAnimationRole role,
        AnimationClip clip,
        GameSkeleton skeleton) => new(
            $@"animations\battle\donor\{name}.anim",
            outputPath,
            role,
            CreateSource(name, clip, skeleton));

    private static AnimationWorkbenchBaseAnimationRequest CreateCompleteRequest(
        GameSkeleton skeleton,
        AnimationClip clip) => new(
        [
            CreateRecipe(
                "idle",
                @"animations\battle\external\base\idle\idle.anim",
                AnimationWorkbenchBaseAnimationRole.Idle,
                clip,
                skeleton),
            CreateRecipe(
                "walk",
                @"animations\battle\external\base\walk\walk.anim",
                AnimationWorkbenchBaseAnimationRole.Walk,
                clip,
                skeleton),
            CreateRecipe(
                "run",
                @"animations\battle\external\base\run\run.anim",
                AnimationWorkbenchBaseAnimationRole.Run,
                clip,
                skeleton),
            CreateRecipe(
                "hit",
                @"animations\battle\external\base\hit\hit.anim",
                AnimationWorkbenchBaseAnimationRole.HitReaction,
                clip,
                skeleton),
            CreateRecipe(
                "death",
                @"animations\battle\external\base\death\death.anim",
                AnimationWorkbenchBaseAnimationRole.Death,
                clip,
                skeleton),
        ],
        skeleton,
        StyleReference: null,
        AnimationWorkbenchBaseAnimationStyleMode.None,
        StyleWeight: 0,
        IncludeRootMotion: false,
        Mappings: null,
        new AnimationWorkbenchSourceFormat(8, 1))
        {
            AnimationSetOutputPath =
            @"animations\database\battle\bin\ext_external_base.animpack",
        };

    private static AnimationWorkbenchSourceInput CreateSource(
        string name,
        AnimationClip clip,
        GameSkeleton skeleton) => new(
            name,
            clip,
            skeleton,
            new AnimationWorkbenchSourceFormat(8, 1));

    private static AnimationClip CreateClip(
        GameSkeleton skeleton,
        int frameCount)
    {
        var clip = new AnimationClip
        {
            Duration = AnimationTimebase.FromFramesPerSecond(
                frameCount,
                frameCount).Duration,
        };
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new AnimationClip.KeyFrame();
            for (var boneIndex = 0;
                 boneIndex < skeleton.BoneCount;
                 boneIndex++)
            {
                frame.Position.Add(skeleton.Translation[boneIndex]);
                frame.Rotation.Add(skeleton.Rotation[boneIndex]);
                frame.Scale.Add(Vector3.One);
            }
            clip.DynamicFrames.Add(frame);
        }
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
