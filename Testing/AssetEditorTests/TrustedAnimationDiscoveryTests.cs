using Editors.AnimationVisualEditors.AnimationWorkbench;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class TrustedAnimationDiscoveryTests
{
    [OneTimeSetUp]
    public void InitializeLocalization() =>
        new LocalizationManager().LoadLanguage();

    [Test]
    public async Task Discovery_ReturnsExactReadOnlyFormatsWithOriginalMetadata()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var reference = CreateContainer(
            "reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);
        var ca = CreateContainer(
            "data.pack",
            TrustedAnimationModelSourceRole.CaPack);
        var skeleton = CreateSkeleton("humanoid", 1);
        Add(project, @"animations\skeletons\humanoid.anim", skeleton);
        Add(project, @"animations\battle\idle.anim",
            CreateAnimation("humanoid", 8, 2, 1));
        Add(reference, @"animations\battle\attack.anim",
            CreateAnimation("humanoid", 8, 4, 2));
        Add(reference, @"animations\battle\legacy.anim",
            CreateAnimation("humanoid", 7, 3, 3));
        Add(project, @"animations\battle\static_pose.anim",
            CreateStaticAnimation("humanoid", 8, 24));
        Add(ca, @"animations\battle\multipart.anim",
            CreateMultipartAnimation("humanoid", 8, 2, 2, 48));
        Add(ca, @"animations\battle\prefix.anim",
            CreateAnimation("human", 8, 2, 1));
        Add(ca, @"animations\battle\helper.anim",
            CreateAnimation("humanoid", 8, 2, 1, true));
        var service = CreateService([ca, reference, project]);
        var identity = TrustedAnimationSkeletonIdentity.Create(
            skeleton,
            @"animations\skeletons\humanoid.anim",
            "project");
        var discovery = new TrustedAnimationDiscovery(service.Object);

        var batches = new List<
            IReadOnlyList<TrustedAnimationCandidate>>();
        await foreach (var batch in discovery.DiscoverAsync(
                           identity,
                           CancellationToken.None))
        {
            batches.Add(batch);
        }

        var results = batches.SelectMany(batch => batch).ToArray();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(results.Select(candidate => candidate.Path),
                Is.EquivalentTo(new[]
                {
                    @"animations\battle\idle.anim",
                    @"animations\battle\attack.anim",
                    @"animations\battle\legacy.anim",
                    @"animations\battle\static_pose.anim",
                    @"animations\battle\multipart.anim",
                }));
            NUnitAssert.That(results, Has.All.Matches<TrustedAnimationCandidate>(
                candidate => candidate.Version is 7 or 8));
            NUnitAssert.That(results, Has.All.Matches<TrustedAnimationCandidate>(
                candidate => candidate.FrameCount > 0));
            NUnitAssert.That(results, Has.All.Matches<TrustedAnimationCandidate>(
                candidate => candidate.DurationSeconds >= 0));
            NUnitAssert.That(results, Has.All.Matches<TrustedAnimationCandidate>(
                candidate => !string.IsNullOrWhiteSpace(
                    candidate.SourcePack)));
            NUnitAssert.That(results, Has.All.Matches<TrustedAnimationCandidate>(
                candidate => !string.IsNullOrWhiteSpace(candidate.Name)));
            var staticPose = results.Single(candidate =>
                candidate.Path.EndsWith("static_pose.anim"));
            NUnitAssert.That(staticPose.HasStaticFrame, Is.True);
            NUnitAssert.That(staticPose.FrameCount, Is.EqualTo(1));
            NUnitAssert.That(staticPose.FramesPerSecond, Is.EqualTo(24));
            var multipart = results.Single(candidate =>
                candidate.Path.EndsWith("multipart.anim"));
            NUnitAssert.That(multipart.PartCount, Is.EqualTo(2));
            NUnitAssert.That(multipart.FrameCount, Is.EqualTo(4));
            NUnitAssert.That(multipart.FramesPerSecond, Is.EqualTo(48));
        });
    }

    [Test]
    public async Task Discovery_RejectsChangedConcreteBindingWithSameTopology()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var loadedSkeleton = CreateSkeleton("humanoid", 1);
        var changedSkeleton = CreateSkeleton("humanoid", 25);
        Add(project,
            @"animations\skeletons\humanoid.anim",
            changedSkeleton);
        Add(project,
            @"animations\battle\idle.anim",
            CreateAnimation("humanoid", 8, 2, 1));
        var identity = TrustedAnimationSkeletonIdentity.Create(
            loadedSkeleton,
            @"animations\skeletons\humanoid.anim",
            "original.pack");
        var discovery = new TrustedAnimationDiscovery(
            CreateService([project]).Object);

        var results = new List<TrustedAnimationCandidate>();
        await foreach (var batch in discovery.DiscoverAsync(
                           identity,
                           CancellationToken.None))
        {
            results.AddRange(batch);
        }

        NUnitAssert.That(results, Is.Empty);
    }

    [Test]
    public async Task Discovery_ParsesOffCallerThreadAndStreamsBoundedBatches()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var discoveryThread = callerThread;
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var skeleton = CreateSkeleton("humanoid", 1);
        Add(project, @"animations\skeletons\humanoid.anim", skeleton);
        for (var index = 0; index < 140; index++)
        {
            Add(project,
                $@"animations\battle\action_{index}.anim",
                CreateAnimation("humanoid", 8, 2, index + 1));
        }
        var service = CreateService([project]);
        service.Setup(candidate => candidate.GetAllPackfileContainers())
            .Callback(() => discoveryThread =
                Environment.CurrentManagedThreadId)
            .Returns([project]);
        var identity = TrustedAnimationSkeletonIdentity.Create(
            skeleton,
            @"animations\skeletons\humanoid.anim",
            "project");
        var discovery = new TrustedAnimationDiscovery(service.Object);

        var batches = new List<
            IReadOnlyList<TrustedAnimationCandidate>>();
        await foreach (var batch in discovery.DiscoverAsync(
                           identity,
                           CancellationToken.None))
        {
            batches.Add(batch);
        }

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(discoveryThread,
                Is.Not.EqualTo(callerThread));
            NUnitAssert.That(batches.Count, Is.GreaterThan(2));
            NUnitAssert.That(
                batches.All(batch => batch.Count <= 64),
                Is.True);
            NUnitAssert.That(batches.Sum(batch => batch.Count),
                Is.EqualTo(140));
        });
    }

    [Test]
    public void Discovery_CanBeCancelledAfterFirstAnimationBatch()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var skeleton = CreateSkeleton("humanoid", 1);
        Add(project, @"animations\skeletons\humanoid.anim", skeleton);
        for (var index = 0; index < 160; index++)
        {
            Add(project,
                $@"animations\battle\action_{index}.anim",
                CreateAnimation("humanoid", 8, 2, index + 1));
        }
        var identity = TrustedAnimationSkeletonIdentity.Create(
            skeleton,
            @"animations\skeletons\humanoid.anim",
            "project");
        var discovery = new TrustedAnimationDiscovery(
            CreateService([project]).Object);

        NUnitAssert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await using var enumerator = discovery
                .DiscoverAsync(identity, cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);
            NUnitAssert.That(await enumerator.MoveNextAsync(), Is.True);
            NUnitAssert.That(enumerator.Current, Is.Not.Empty);
            cancellation.Cancel();
            while (await enumerator.MoveNextAsync())
            {
            }
        });
    }

    private static PackFileContainer CreateContainer(
        string name,
        TrustedAnimationModelSourceRole role)
    {
        var container = new PackFileContainer(name)
        {
            SystemFilePath = $@"C:\packs\{name}",
        };
        if (role == TrustedAnimationModelSourceRole.FolderProject)
            container.Role = PackFileContainerRole.ProjectWorkspace;
        else if (role == TrustedAnimationModelSourceRole.ReferencePack)
            container.Role = PackFileContainerRole.Reference;
        else
            container.IsCaPackFile = true;
        return container;
    }

    private static PackFile CreateSkeleton(
        string skeletonName,
        float bindTranslation)
    {
        var file = CreateAnimationFile(
            skeletonName,
            7,
            2,
            bindTranslation,
            false);
        return PackFile.CreateFromBytes(
            $"{skeletonName}.anim",
            AnimationFile.ConvertToBytes(file));
    }

    private static PackFile CreateAnimation(
        string skeletonName,
        uint version,
        int frameCount,
        float translation,
        bool includeHelperBone = false)
    {
        var file = CreateAnimationFile(
            skeletonName,
            version,
            frameCount,
            translation,
            includeHelperBone);
        return PackFile.CreateFromBytes(
            $"{skeletonName}_{version}.anim",
            AnimationFile.ConvertToBytes(file));
    }

    private static PackFile CreateStaticAnimation(
        string skeletonName,
        uint version,
        float frameRate)
    {
        var file = CreateAnimationFile(
            skeletonName,
            version,
            1,
            4,
            false);
        file.Header.FrameRate = frameRate;
        file.Header.AnimationTotalPlayTimeInSec = 0;
        var part = file.AnimationParts.Single();
        part.TranslationMappings[0] =
            new AnimationFile.AnimationBoneMapping(10000);
        part.TranslationMappings[1] =
            new AnimationFile.AnimationBoneMapping(10001);
        part.RotationMappings[0] =
            new AnimationFile.AnimationBoneMapping(10000);
        part.RotationMappings[1] =
            new AnimationFile.AnimationBoneMapping(10001);
        part.StaticFrame = part.DynamicFrames.Single();
        part.DynamicFrames.Clear();
        return PackFile.CreateFromBytes(
            $"{skeletonName}_static.anim",
            AnimationFile.ConvertToBytes(file));
    }

    private static PackFile CreateMultipartAnimation(
        string skeletonName,
        uint version,
        int framesPerPart,
        int partCount,
        float frameRate)
    {
        var file = CreateAnimationFile(
            skeletonName,
            version,
            framesPerPart,
            5,
            false);
        for (var partIndex = 1; partIndex < partCount; partIndex++)
        {
            var extra = CreateAnimationFile(
                skeletonName,
                version,
                framesPerPart,
                5 + partIndex,
                false);
            file.AnimationParts.Add(extra.AnimationParts.Single());
        }
        file.Header.FrameRate = frameRate;
        file.Header.AnimationTotalPlayTimeInSec =
            framesPerPart * partCount / frameRate;
        return PackFile.CreateFromBytes(
            $"{skeletonName}_multipart.anim",
            AnimationFile.ConvertToBytes(file));
    }

    private static AnimationFile CreateAnimationFile(
        string skeletonName,
        uint version,
        int frameCount,
        float translation,
        bool includeHelperBone)
    {
        var bones = new List<AnimationFile.BoneInfo>
        {
            new() { Id = 0, Name = "root", ParentId = -1 },
            new() { Id = 1, Name = "pelvis", ParentId = 0 },
        };
        if (includeHelperBone)
        {
            bones.Add(new AnimationFile.BoneInfo
            {
                Id = 2,
                Name = "helper",
                ParentId = 1,
            });
        }

        var part = new AnimationFile.AnimationPart();
        for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            part.TranslationMappings.Add(
                new AnimationFile.AnimationBoneMapping(boneIndex));
            part.RotationMappings.Add(
                new AnimationFile.AnimationBoneMapping(boneIndex));
        }
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new AnimationFile.Frame();
            for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                frame.Transforms.Add(new RmvVector3(
                    translation + frameIndex,
                    boneIndex,
                    0));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            part.DynamicFrames.Add(frame);
        }

        return new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = version,
                SkeletonName = skeletonName,
                FrameRate = 30,
                AnimationTotalPlayTimeInSec = frameCount / 30f,
            },
            Bones = bones.ToArray(),
            AnimationParts = [part],
        };
    }

    private static void Add(
        PackFileContainer container,
        string path,
        PackFile file) =>
        container.FileList[path.ToLowerInvariant()] = file;

    private static Mock<IPackFileService> CreateService(
        List<PackFileContainer> containers)
    {
        var service = new Mock<IPackFileService>();
        service.Setup(candidate => candidate.GetAllPackfileContainers())
            .Returns(containers);
        service.Setup(candidate => candidate.GetFileEntriesSnapshot(
                It.IsAny<PackFileContainer>()))
            .Returns((PackFileContainer container) =>
                container.FileList.ToArray());
        return service;
    }
}
