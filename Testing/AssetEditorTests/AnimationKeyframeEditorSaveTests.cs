using System.Reflection;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests
{
    [TestClass]
    [DoNotParallelize]
    public class AnimationKeyframeEditorSaveTests
    {
        [ClassInitialize]
        public static void Initialize(TestContext _)
        {
            new LocalizationManager().LoadLanguage();
        }

        [TestMethod]
        public void Save_WhenSaveIsCancelled_KeepsDirty()
        {
            var harness = CreateHarness();
            harness.FileSaveService
                .Setup(x => x.Save("animations/test.anim", It.IsAny<byte[]>(), true))
                .Returns((PackFile?)null);

            harness.Editor.Save();

            Assert.IsTrue(harness.Editor.IsDirty.Value);
        }

        [TestMethod]
        public void Save_WhenSaveSucceeds_ClearsDirty()
        {
            var harness = CreateHarness();
            harness.FileSaveService
                .Setup(x => x.Save("animations/test.anim", It.IsAny<byte[]>(), true))
                .Returns(PackFile.CreateFromBytes("test.anim", [1]));

            harness.Editor.Save();

            Assert.IsFalse(harness.Editor.IsDirty.Value);
        }

        [TestMethod]
        public void SaveAs_WhenSaveIsCancelled_KeepsDirty()
        {
            var harness = CreateHarness();
            harness.FileSaveService
                .Setup(x => x.SaveAs(".anim", It.IsAny<byte[]>()))
                .Returns((PackFile?)null);

            harness.Editor.SaveAs();

            Assert.IsTrue(harness.Editor.IsDirty.Value);
        }

        [TestMethod]
        public void SaveAs_WhenSaveSucceeds_ClearsDirty()
        {
            var harness = CreateHarness();
            harness.FileSaveService
                .Setup(x => x.SaveAs(".anim", It.IsAny<byte[]>()))
                .Returns(PackFile.CreateFromBytes("test.anim", [1]));

            harness.Editor.SaveAs();

            Assert.IsFalse(harness.Editor.IsDirty.Value);
        }

        private static EditorHarness CreateHarness()
        {
            var fileSaveService = new Mock<IFileSaveService>();
            var editor = new AnimationKeyframeEditorViewModel(
                new Mock<IPackFileService>().Object,
                new Mock<ISkeletonAnimationLookUpHelper>().Object,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                fileSaveService.Object,
                Mock.Of<IStandardDialogs>());

            var rider = new SceneObject("rider")
            {
                AnimationClip = CreateAnimationClip(),
                Skeleton = CreateSkeleton(),
            };
            rider.AnimationName.Value = "animations/test.anim";

            var riderField = typeof(AnimationKeyframeEditorViewModel)
                .GetField("_rider", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(riderField);
            riderField.SetValue(editor, rider);

            editor.IsDirty.Value = true;
            return new EditorHarness(editor, fileSaveService);
        }

        private static AnimationClip CreateAnimationClip()
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.Add(Vector3.Zero);
            frame.Rotation.Add(Quaternion.Identity);
            frame.Scale.Add(Vector3.One);

            var clip = new AnimationClip();
            clip.DynamicFrames.Add(frame);
            clip.Duration = TimeSpan.FromSeconds(0.05);
            return clip;
        }

        private static GameSkeleton CreateSkeleton()
        {
            var skeletonFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader
                {
                    SkeletonName = "test_skeleton",
                },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Id = 0,
                        Name = "root",
                        ParentId = AnimationFile.BoneIndexNoParent,
                    },
                ],
            };

            var frame = new AnimationFile.Frame();
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));

            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            skeletonFile.AnimationParts.Add(part);

            return new GameSkeleton(skeletonFile, new AnimationPlayer());
        }

        private sealed record EditorHarness(
            AnimationKeyframeEditorViewModel Editor,
            Mock<IFileSaveService> FileSaveService);
    }
}
