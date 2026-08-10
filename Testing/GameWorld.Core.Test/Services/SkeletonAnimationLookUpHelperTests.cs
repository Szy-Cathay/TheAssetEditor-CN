using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using System.Text;

namespace Test.GameWorld.Core.Services
{
    [TestFixture]
    [NonParallelizable]
    public class SkeletonAnimationLookUpHelperTests
    {
        [Test]
        public void Load_MixedPackedAndMemoryAnimations_IndexesActualMemoryAnimation()
        {
            var packedBytes = CreateAnimationBytes("packed_skeleton");
            var memoryBytes = CreateAnimationBytes("memory_skeleton");
            var tempPath = Path.GetTempFileName();
            File.WriteAllBytes(tempPath, packedBytes);
            var parent = new PackedFileSourceParent { FilePath = tempPath };

            try
            {
                var container = CreateContainer("mixed");
                container.FileList["animations\\packed.anim"] = new PackFile(
                    "packed.anim",
                    new PackedFileSource(
                        parent,
                        0,
                        packedBytes.Length,
                        false,
                        false,
                        CompressionFormat.None,
                        0));
                var memoryFile = AddAnimation(
                    container,
                    "animations\\memory.anim",
                    "memory_skeleton",
                    memoryBytes);
                var (service, eventHub) = CreateService(container);

                using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

                var references = helper.GetAnimationsForSkeleton("memory_skeleton");
                Assert.That(references.Select(x => x.AnimationFile), Does.Contain("animations\\memory.anim"));
                Assert.That(helper.FindAnimationRefFromPackFile(memoryFile)?.Container, Is.SameAs(container));
            }
            finally
            {
                parent.CloseStream();
                File.Delete(tempPath);
            }
        }

        [Test]
        public void Load_UppercaseAnimExtension_IsIndexed()
        {
            var container = CreateContainer("uppercase");
            AddAnimation(container, "animations\\upper.ANIM", "upper_skeleton");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

            Assert.That(helper.GetAnimationsForSkeleton("upper_skeleton"), Has.Count.EqualTo(1));
        }

        [Test]
        public void GetAnimationsForSkeleton_IsCaseInsensitiveAndReturnsStableCollection()
        {
            var container = CreateContainer("case");
            AddAnimation(container, "animations\\case.anim", "Human_Skeleton");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

            var first = helper.GetAnimationsForSkeleton("human_skeleton");
            var second = helper.GetAnimationsForSkeleton("HUMAN_SKELETON");
            Assert.That(first, Is.SameAs(second));
            Assert.That(first, Has.Count.EqualTo(1));
        }

        [Test]
        public void GetAnimationsForSkeleton_ClassifiesSkeletonAndActionFiles()
        {
            var container = CreateContainer("classification");
            AddAnimation(
                container,
                "animations\\skeletons\\humanoid.anim",
                "humanoid");
            AddAnimation(
                container,
                "animations\\battle\\humanoid\\stand_idle.anim",
                "humanoid");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

            var references = helper.GetAnimationsForSkeleton("humanoid");
            Assert.Multiple(() =>
            {
                Assert.That(
                    references.Single(reference => reference.AnimationFile.Contains("skeletons"))
                        .IsSkeletonFile,
                    Is.True);
                Assert.That(
                    references.Single(reference => reference.AnimationFile.Contains("stand_idle"))
                        .IsSkeletonFile,
                    Is.False);
            });
        }

        [Test]
        public void KnownBrokenPath_WithDifferentCasing_IsSkipped()
        {
            var container = CreateContainer("broken");
            AddAnimation(
                container,
                "RIGIDMODELS\\BUILDINGS\\ROMAN_AQUEDUCT_STRAIGHT\\ROMAN_AQUEDUCT_STRAIGHT_PIECE01_DESTRUCT01_ANIM.ANIM",
                "must_not_be_indexed");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

            Assert.That(helper.GetAnimationsForSkeleton("must_not_be_indexed"), Is.Empty);
        }

        [Test]
        public void GetSkeletonFileFromName_ExcludesReferencePoseAndTechCaseInsensitively()
        {
            var container = CreateContainer("filtered");
            AddAnimation(
                container,
                "animations\\skeletons\\REFERENCE_POSES\\filtered_reference.anim",
                "filtered_reference");
            AddAnimation(
                container,
                "animations\\skeletons\\TECH\\filtered_tech.anim",
                "filtered_tech");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

            Assert.That(helper.GetSkeletonFileFromName("FILTERED_REFERENCE"), Is.Null);
            Assert.That(helper.GetSkeletonFileFromName("FILTERED_TECH"), Is.Null);
        }

        [Test]
        public void UnloadContainer_RemovesItsAnimationsAndSkeletonPaths()
        {
            var firstContainer = CreateContainer("first");
            AddAnimation(
                firstContainer,
                "animations\\skeletons\\first_skeleton.anim",
                "first_skeleton");
            var secondContainer = CreateContainer("second");
            AddAnimation(
                secondContainer,
                "animations\\skeletons\\second_skeleton.anim",
                "second_skeleton");
            var (service, eventHub) = CreateService(firstContainer, secondContainer);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            service.UnloadPackContainer(firstContainer);

            Assert.That(helper.GetAnimationsForSkeleton("first_skeleton"), Is.Empty);
            Assert.That(helper.GetAnimationsForSkeleton("second_skeleton"), Has.Count.EqualTo(1));
            Assert.That(
                helper.GetAllSkeletonFileNames(),
                Does.Not.Contain("animations\\skeletons\\first_skeleton.anim"));
            Assert.That(
                helper.GetAllSkeletonFileNames(),
                Does.Contain("animations\\skeletons\\second_skeleton.anim"));
        }

        [Test]
        public void SharedSkeletonPath_RemainsUniqueUntilLastContainerIsRemoved()
        {
            const string sharedPath = "animations\\skeletons\\shared_skeleton.anim";
            var firstContainer = CreateContainer("first_shared");
            AddAnimation(firstContainer, sharedPath, "shared_skeleton");
            var secondContainer = CreateContainer("second_shared");
            AddAnimation(secondContainer, sharedPath, "shared_skeleton");
            var (service, eventHub) = CreateService(firstContainer, secondContainer);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            Assert.That(helper.GetAllSkeletonFileNames().Count(x => x == sharedPath), Is.EqualTo(1));

            service.UnloadPackContainer(firstContainer);
            Assert.That(helper.GetAllSkeletonFileNames().Count(x => x == sharedPath), Is.EqualTo(1));

            service.UnloadPackContainer(secondContainer);
            Assert.That(helper.GetAllSkeletonFileNames(), Does.Not.Contain(sharedPath));
        }

        [Test]
        public void FindAnimationRef_DuplicatePathAcrossContainers_ReturnsExactContainer()
        {
            const string sharedPath = "animations\\shared.anim";
            var firstContainer = CreateContainer("first_duplicate");
            var firstFile = AddAnimation(firstContainer, sharedPath, "first_skeleton");
            var secondContainer = CreateContainer("second_duplicate");
            var secondFile = AddAnimation(secondContainer, sharedPath, "second_skeleton");
            var (service, eventHub) = CreateService(firstContainer, secondContainer);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);

            Assert.That(helper.FindAnimationRefFromPackFile(firstFile)?.Container, Is.SameAs(firstContainer));
            Assert.That(helper.FindAnimationRefFromPackFile(secondFile)?.Container, Is.SameAs(secondContainer));

            service.UnloadPackContainer(firstContainer);
            Assert.That(helper.FindAnimationRefFromPackFile(firstFile), Is.Null);
            Assert.That(helper.FindAnimationRefFromPackFile(secondFile)?.Container, Is.SameAs(secondContainer));
        }

        [Test]
        public void DeleteFile_RemovesOnlyDeletedAnimation()
        {
            var container = CreateContainer("delete_file");
            var deletedFile = AddAnimation(container, "animations\\deleted.anim", "deleted_skeleton");
            AddAnimation(container, "animations\\remaining.anim", "remaining_skeleton");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            service.DeleteFile(container, deletedFile);

            Assert.That(helper.GetAnimationsForSkeleton("deleted_skeleton"), Is.Empty);
            Assert.That(helper.GetAnimationsForSkeleton("remaining_skeleton"), Has.Count.EqualTo(1));
        }

        [Test]
        public void DeleteFolder_RemovesOnlyAnimationsInsideFolder()
        {
            var container = CreateContainer("delete_folder");
            AddAnimation(container, "animations\\remove\\deleted.anim", "deleted_skeleton");
            AddAnimation(container, "animations\\keep\\remaining.anim", "remaining_skeleton");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            service.DeleteFolder(container, "animations\\remove");

            Assert.That(helper.GetAnimationsForSkeleton("deleted_skeleton"), Is.Empty);
            Assert.That(helper.GetAnimationsForSkeleton("remaining_skeleton"), Has.Count.EqualTo(1));
        }

        [Test]
        public void DeleteFolder_RemovesEveryAnimationDeletedByPackFileService()
        {
            var container = CreateContainer("delete_folder_prefix");
            AddAnimation(container, "animations\\remove\\deleted.anim", "deleted_skeleton");
            AddAnimation(container, "animations\\remove_extra\\also_deleted.anim", "also_deleted_skeleton");
            var (service, eventHub) = CreateService(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            service.DeleteFolder(container, "animations\\remove");

            Assert.That(helper.GetAnimationsForSkeleton("deleted_skeleton"), Is.Empty);
            Assert.That(helper.GetAnimationsForSkeleton("also_deleted_skeleton"), Is.Empty);
            Assert.That(container.FileList, Is.Empty);
        }

        [Test]
        public void RefreshOlderContainer_DoesNotOverrideLaterSkeleton()
        {
            const string skeletonPath = "animations\\skeletons\\priority.anim";
            var firstBytes = CreateCompleteAnimationBytes("first_header", "first_bone");
            var firstContainer = CreateContainer("priority_first");
            var firstSkeleton = AddAnimation(
                firstContainer,
                skeletonPath,
                "first_header",
                firstBytes);
            var secondContainer = CreateContainer("priority_second");
            AddAnimation(
                secondContainer,
                skeletonPath,
                "second_header",
                CreateCompleteAnimationBytes("second_header", "second_bone"));
            var (service, eventHub) = CreateService(firstContainer, secondContainer);
            service.SetEditablePack(firstContainer);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            Assert.That(helper.GetSkeletonFileFromName("priority")?.Header.SkeletonName, Is.EqualTo("second_header"));

            service.SaveFile(firstSkeleton, firstBytes);

            Assert.That(helper.GetSkeletonFileFromName("priority")?.Header.SkeletonName, Is.EqualTo("second_header"));
        }

        [Test]
        public void AddFilesToPack_FolderProjectAnimation_IsAvailableWithoutReopeningProject()
        {
            var projectPath = Path.Combine(
                Path.GetTempPath(),
                $"ae-animation-refresh-{Guid.NewGuid():N}");
            Directory.CreateDirectory(projectPath);
            try
            {
                using var container = FolderProjectContainer.Create(
                    projectPath,
                    new FolderProjectSettings { Name = "工程" });
                var (service, eventHub) = CreateService(container);

                using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
                service.AddFilesToPack(
                    container,
                    [
                        new NewPackFileEntry(
                            @"animations\battle\humanoid",
                            PackFile.CreateFromBytes(
                                "imported.anim",
                                CreateAnimationBytes("folder_project_skeleton"))),
                    ]);

                Assert.That(
                    helper.GetAnimationsForSkeleton("folder_project_skeleton")
                        .Select(reference => reference.AnimationFile),
                    Does.Contain(@"animations\battle\humanoid\imported.anim"));
            }
            finally
            {
                Directory.Delete(projectPath, true);
            }
        }

        [Test]
        public void SaveNonAnimationFile_DoesNotRebuildAnimationIndex()
        {
            var container = CreateContainer("non_animation_update");
            AddAnimation(container, "animations\\stable.anim", "stable_skeleton");
            var modelFile = PackFile.CreateFromBytes("stable.rigid_model_v2", [1, 2, 3]);
            container.FileList["variantmeshes\\stable.rigid_model_v2"] = modelFile;
            var (service, eventHub) = CreateService(container);
            service.SetEditablePack(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            var originalReference = helper.GetAnimationsForSkeleton("stable_skeleton").Single();

            service.SaveFile(modelFile, [4, 5, 6]);

            Assert.That(
                helper.GetAnimationsForSkeleton("stable_skeleton").Single(),
                Is.SameAs(originalReference));
        }

        [Test]
        public void SaveFile_WhenSkeletonNameChanges_ReindexesAnimation()
        {
            var container = CreateContainer("update");
            var animation = AddAnimation(container, "animations\\updated.anim", "old_skeleton");
            var (service, eventHub) = CreateService(container);
            service.SetEditablePack(container);

            using var helper = new SkeletonAnimationLookUpHelper(service, eventHub);
            service.SaveFile(animation, CreateAnimationBytes("new_skeleton"));

            Assert.That(helper.GetAnimationsForSkeleton("old_skeleton"), Is.Empty);
            Assert.That(helper.GetAnimationsForSkeleton("new_skeleton"), Has.Count.EqualTo(1));
        }

        private static (
            IPackFileService Service,
            TestGlobalEventHub EventHub) CreateService(params PackFileContainer[] containers)
        {
            var eventHub = new TestGlobalEventHub();
            var service = new PackFileService(eventHub)
            {
                EnforceGameFilesMustBeLoaded = false
            };
            foreach (var container in containers)
                service.AddContainer(container);
            return (service, eventHub);
        }

        private static PackFileContainer CreateContainer(string name)
        {
            return new PackFileContainer(name)
            {
                SystemFilePath = $"C:\\tests\\{name}.pack"
            };
        }

        private static PackFile AddAnimation(
            PackFileContainer container,
            string path,
            string skeletonName,
            byte[] bytes = null)
        {
            var file = PackFile.CreateFromBytes(
                Path.GetFileName(path),
                bytes ?? CreateAnimationBytes(skeletonName));
            container.FileList[path] = file;
            return file;
        }

        private static byte[] CreateAnimationBytes(string skeletonName)
        {
            var skeletonNameBytes = Encoding.UTF8.GetBytes(skeletonName);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write((uint)7);
            writer.Write((uint)1);
            writer.Write(20f);
            writer.Write((short)skeletonNameBytes.Length);
            writer.Write(skeletonNameBytes);
            while (stream.Length < 128)
                writer.Write((byte)0);
            return stream.ToArray();
        }

        private static byte[] CreateCompleteAnimationBytes(string skeletonName, string boneName)
        {
            var animation = new AnimationFile
            {
                Header =
                {
                    Version = 7,
                    SkeletonName = skeletonName,
                    AnimationTotalPlayTimeInSec = 0.1f
                },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Id = 0,
                        Name = boneName,
                        ParentId = AnimationFile.BoneIndexNoParent
                    }
                ]
            };

            var frame = new AnimationFile.Frame();
            frame.Transforms.Add(new RmvVector3(0, 0, 0));
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));

            var animationPart = new AnimationFile.AnimationPart();
            animationPart.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(0));
            animationPart.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(0));
            animationPart.DynamicFrames.Add(frame);
            animation.AnimationParts.Add(animationPart);

            return AnimationFile.ConvertToBytes(animation);
        }

        private sealed class TestGlobalEventHub : IGlobalEventHub
        {
            private readonly Dictionary<Type, List<(object Owner, Delegate Callback)>> _callbacks = [];

            public void PublishGlobalEvent<T>(T e)
            {
                if (_callbacks.TryGetValue(typeof(T), out var callbacks) == false)
                    return;

                foreach (var callback in callbacks.ToList())
                    ((Action<T>)callback.Callback)(e);
            }

            public void Register<T>(object owner, Action<T> action)
            {
                if (_callbacks.TryGetValue(typeof(T), out var callbacks) == false)
                {
                    callbacks = [];
                    _callbacks[typeof(T)] = callbacks;
                }
                callbacks.Add((owner, action));
            }

            public void UnRegister(object owner)
            {
                foreach (var callbacks in _callbacks.Values)
                    callbacks.RemoveAll(x => ReferenceEquals(x.Owner, owner));
            }
        }
    }
}
