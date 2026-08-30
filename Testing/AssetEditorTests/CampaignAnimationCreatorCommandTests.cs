using AnimationEditor.CampaignAnimationCreator.Commands;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Moq;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests
{
    [TestClass]
    [DoNotParallelize]
    public class CampaignAnimationCreatorCommandTests
    {
        [ClassInitialize]
        public static void Initialize(TestContext _)
        {
            new LocalizationManager().LoadLanguage();
        }

        [TestMethod]
        public void RegisterTools_EnablesCampaignAnimationToolbarEntry()
        {
            var database = new EditorDatabase(null!, null!);

            new Editors.AnimationVisualEditors.DependencyInjectionContainer().RegisterTools(database);

            var editorInfo = database
                .GetEditorInfos()
                .Single(x => x.EditorEnum == EditorEnums.CampaginAnimation_Editor);
            Assert.IsTrue(editorInfo.AddToolbarButton);
            Assert.IsTrue(editorInfo.IsToolbarButtonEnabled);
            Assert.AreEqual(
                typeof(AnimationEditor.CampaignAnimationCreator.CampaignAnimationCreatorViewModel),
                editorInfo.ViewModel);
            Assert.AreEqual(
                typeof(AnimationEditor.Common.BaseControl.EditorHostView),
                editorInfo.View);
            Assert.IsTrue(
                typeof(Editors.Shared.Core.Common.BaseControl.IEditorViewModelTypeProvider)
                    .IsAssignableFrom(editorInfo.ViewModel));
            Assert.AreEqual("模型", LocalizationManager.Instance.Get("CampaignAnim.Model"));
        }

        [TestMethod]
        public void Convert_NoAnimation_ShowsLocalizedError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            var result = command.Execute(null, CreateRootBone(), out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(x => x.ShowDialogBox("无法转换动画：未选择动画。", "错误"), Times.Once);
        }

        [TestMethod]
        public void Convert_NoRootBone_ShowsLocalizedError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            var result = command.Execute(CreateAnimationClip(), null, out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(x => x.ShowDialogBox("无法转换动画：未选择根骨骼。", "错误"), Times.Once);
        }

        [TestMethod]
        public void Convert_NoFrames_ShowsLocalizedError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            var result = command.Execute(new AnimationClip(), CreateRootBone(), out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(x => x.ShowDialogBox("无法转换动画：动画不包含任何帧。", "错误"), Times.Once);
        }

        [TestMethod]
        public void Convert_BoneIndexOutOfRange_ShowsLocalizedError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            var result = command.Execute(
                CreateAnimationClip(boneCount: 1, frameCount: 1),
                new SkeletonBoneNode { BoneIndex = 1, BoneName = "animroot" },
                out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(
                x => x.ShowDialogBox("无法转换动画：第 1 帧中不存在索引为 1 的骨骼。", "错误"),
                Times.Once);
        }

        [TestMethod]
        public void Convert_NegativeBoneIndex_ShowsLocalizedError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            var result = command.Execute(
                CreateAnimationClip(),
                new SkeletonBoneNode { BoneIndex = -1, BoneName = "invalid" },
                out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(
                x => x.ShowDialogBox("无法转换动画：第 1 帧中不存在索引为 -1 的骨骼。", "错误"),
                Times.Once);
        }

        [TestMethod]
        public void Convert_LaterFrameHasFewerBones_ReportsThatFrame()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);
            var animation = CreateAnimationClip();
            animation.DynamicFrames[1].Position.RemoveAt(1);
            animation.DynamicFrames[1].Rotation.RemoveAt(1);
            animation.DynamicFrames[1].Scale.RemoveAt(1);

            var result = command.Execute(animation, CreateRootBone(), out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(
                x => x.ShowDialogBox("无法转换动画：第 2 帧中不存在索引为 1 的骨骼。", "错误"),
                Times.Once);
        }

        [TestMethod]
        public void Convert_InconsistentFrameData_ShowsLocalizedError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);
            var animation = CreateAnimationClip();
            animation.DynamicFrames[1].Scale.RemoveAt(1);

            var result = command.Execute(animation, CreateRootBone(), out var convertedAnimation);

            Assert.IsFalse(result);
            Assert.IsNull(convertedAnimation);
            dialogs.Verify(
                x => x.ShowDialogBox("无法转换动画：第 2 帧的骨骼数据不完整。", "错误"),
                Times.Once);
        }

        [TestMethod]
        public void Convert_ValidAnimation_ClonesAndResetsOnlySelectedBone()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);
            var sourceAnimation = CreateAnimationClip();
            var originalAnimation = sourceAnimation.Clone();

            var result = command.Execute(sourceAnimation, CreateRootBone(), out var convertedAnimation);

            Assert.IsTrue(result);
            Assert.IsNotNull(convertedAnimation);
            Assert.AreNotSame(sourceAnimation, convertedAnimation);
            Assert.AreEqual(sourceAnimation.Duration, convertedAnimation.Duration);
            Assert.AreEqual(sourceAnimation.DynamicFrames.Count, convertedAnimation.DynamicFrames.Count);
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            for (var frameIndex = 0; frameIndex < convertedAnimation.DynamicFrames.Count; frameIndex++)
            {
                Assert.AreEqual(Vector3.Zero, convertedAnimation.DynamicFrames[frameIndex].Position[1]);
                Assert.AreEqual(Quaternion.Identity, convertedAnimation.DynamicFrames[frameIndex].Rotation[1]);
                Assert.AreEqual(
                    originalAnimation.DynamicFrames[frameIndex].Position[0],
                    convertedAnimation.DynamicFrames[frameIndex].Position[0]);
                Assert.AreEqual(
                    originalAnimation.DynamicFrames[frameIndex].Rotation[0],
                    convertedAnimation.DynamicFrames[frameIndex].Rotation[0]);
                CollectionAssert.AreEqual(
                    originalAnimation.DynamicFrames[frameIndex].Scale,
                    convertedAnimation.DynamicFrames[frameIndex].Scale);
                Assert.AreEqual(
                    originalAnimation.DynamicFrames[frameIndex].Position[1],
                    sourceAnimation.DynamicFrames[frameIndex].Position[1]);
                Assert.AreEqual(
                    originalAnimation.DynamicFrames[frameIndex].Rotation[1],
                    sourceAnimation.DynamicFrames[frameIndex].Rotation[1]);
            }
        }

        [TestMethod]
        public void Save_NoSkeleton_ShowsLocalizedErrorAndDoesNotSave()
        {
            var packFileService = new Mock<IPackFileService>();
            var fileSaveService = new Mock<IFileSaveService>();
            var dialogs = new Mock<IStandardDialogs>();
            var command = new SaveCampaignAnimationCommand(
                packFileService.Object,
                fileSaveService.Object,
                dialogs.Object,
                LocalizationManager.Instance);

            var result = command.Execute(null, CreateAnimationClip());

            Assert.IsFalse(result);
            dialogs.Verify(x => x.ShowDialogBox("无法保存动画：未加载骨骼。", "错误"), Times.Once);
            fileSaveService.Verify(x => x.SaveAs(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        }

        [TestMethod]
        public void Save_NoAnimation_ShowsLocalizedErrorAndDoesNotSave()
        {
            var packFileService = new Mock<IPackFileService>();
            var fileSaveService = new Mock<IFileSaveService>();
            var dialogs = new Mock<IStandardDialogs>();
            var command = new SaveCampaignAnimationCommand(
                packFileService.Object,
                fileSaveService.Object,
                dialogs.Object,
                LocalizationManager.Instance);

            var result = command.Execute(CreateSkeleton(), null);

            Assert.IsFalse(result);
            dialogs.Verify(x => x.ShowDialogBox("无法保存动画：未加载动画。", "错误"), Times.Once);
            fileSaveService.Verify(x => x.SaveAs(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        }

        [TestMethod]
        public void Save_NoEditablePack_ShowsLocalizedErrorAndDoesNotSave()
        {
            var packFileService = new Mock<IPackFileService>();
            var fileSaveService = new Mock<IFileSaveService>();
            var dialogs = new Mock<IStandardDialogs>();
            var command = new SaveCampaignAnimationCommand(
                packFileService.Object,
                fileSaveService.Object,
                dialogs.Object,
                LocalizationManager.Instance);

            var result = command.Execute(CreateSkeleton(), CreateAnimationClip());

            Assert.IsFalse(result);
            dialogs.Verify(
                x => x.ShowDialogBox("无法保存动画：请先新建或打开一个可编辑的 Pack 文件。", "错误"),
                Times.Once);
            fileSaveService.Verify(x => x.SaveAs(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        }

        [TestMethod]
        public void Save_ValidAnimation_WritesReloadableAnimFile()
        {
            byte[]? savedBytes = null;
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetEditablePack())
                .Returns(new PackFileContainer("test.pack"));
            var fileSaveService = new Mock<IFileSaveService>();
            fileSaveService
                .Setup(x => x.SaveAs(".anim", It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, bytes) => savedBytes = bytes)
                .Returns(PackFile.CreateFromBytes("campaign.anim", [1]));
            var dialogs = new Mock<IStandardDialogs>();
            var command = new SaveCampaignAnimationCommand(
                packFileService.Object,
                fileSaveService.Object,
                dialogs.Object,
                LocalizationManager.Instance);
            var skeleton = CreateSkeleton();
            var animationClip = CreateAnimationClip();
            foreach (var frame in animationClip.DynamicFrames)
            {
                frame.Position[1] = Vector3.Zero;
                frame.Rotation[1] = Quaternion.Identity;
            }

            var result = command.Execute(skeleton, animationClip);

            Assert.IsTrue(result);
            Assert.IsNotNull(savedBytes);
            Assert.IsTrue(savedBytes.Length > 0);
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            fileSaveService.Verify(x => x.SaveAs(".anim", It.IsAny<byte[]>()), Times.Once);

            var savedAnimation = AnimationFile.Create(new ByteChunk(savedBytes));
            Assert.AreEqual(skeleton.SkeletonName, savedAnimation.Header.SkeletonName);
            CollectionAssert.AreEqual(skeleton.BoneNames, savedAnimation.Bones.Select(x => x.Name).ToList());
            Assert.AreEqual(animationClip.DynamicFrames.Count, savedAnimation.AnimationParts[0].DynamicFrames.Count);
            Assert.AreEqual(
                (float)animationClip.Duration.TotalSeconds,
                savedAnimation.Header.AnimationTotalPlayTimeInSec,
                0.001f);

            for (var frameIndex = 0; frameIndex < animationClip.DynamicFrames.Count; frameIndex++)
            {
                var savedFrame = savedAnimation.AnimationParts[0].DynamicFrames[frameIndex];
                for (var boneIndex = 0; boneIndex < animationClip.DynamicFrames[frameIndex].Position.Count; boneIndex++)
                {
                    Assert.AreEqual(
                        animationClip.DynamicFrames[frameIndex].Position[boneIndex],
                        savedFrame.Transforms[boneIndex].ToVector3());

                    var expectedRotation = animationClip.DynamicFrames[frameIndex].Rotation[boneIndex];
                    var savedRotation = savedFrame.Quaternion[boneIndex].ToQuaternion();
                    Assert.AreEqual(expectedRotation.X, savedRotation.X, 0.0001f);
                    Assert.AreEqual(expectedRotation.Y, savedRotation.Y, 0.0001f);
                    Assert.AreEqual(expectedRotation.Z, savedRotation.Z, 0.0001f);
                    Assert.AreEqual(expectedRotation.W, savedRotation.W, 0.0001f);
                }
            }
        }

        [TestMethod]
        public void Save_WhenUserCancels_ReturnsFalse()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetEditablePack())
                .Returns(new PackFileContainer("test.pack"));
            var fileSaveService = new Mock<IFileSaveService>();
            fileSaveService
                .Setup(x => x.SaveAs(".anim", It.IsAny<byte[]>()))
                .Returns((PackFile?)null);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new SaveCampaignAnimationCommand(
                packFileService.Object,
                fileSaveService.Object,
                dialogs.Object,
                LocalizationManager.Instance);

            var result = command.Execute(CreateSkeleton(), CreateAnimationClip());

            Assert.IsFalse(result);
            fileSaveService.Verify(x => x.SaveAs(".anim", It.IsAny<byte[]>()), Times.Once);
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        private static SkeletonBoneNode CreateRootBone()
        {
            return new SkeletonBoneNode { BoneIndex = 1, BoneName = "animroot" };
        }

        private static AnimationClip CreateAnimationClip(int boneCount = 2, int frameCount = 2)
        {
            var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(0.2) };
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var frame = new AnimationClip.KeyFrame();
                for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    frame.Position.Add(new Vector3(
                        frameIndex + boneIndex + 1,
                        frameIndex + boneIndex + 2,
                        frameIndex + boneIndex + 3));
                    frame.Rotation.Add(Quaternion.CreateFromYawPitchRoll(
                        frameIndex + boneIndex + 0.1f,
                        frameIndex + boneIndex + 0.2f,
                        frameIndex + boneIndex + 0.3f));
                    frame.Scale.Add(Vector3.One);
                }
                clip.DynamicFrames.Add(frame);
            }
            return clip;
        }

        private static GameSkeleton CreateSkeleton()
        {
            var skeletonFile = new AnimationFile
            {
                Header = { SkeletonName = "campaign_animation_test_skeleton" },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Id = 0,
                        Name = "root",
                        ParentId = AnimationFile.BoneIndexNoParent
                    },
                    new AnimationFile.BoneInfo
                    {
                        Id = 1,
                        Name = "animroot",
                        ParentId = 0
                    }
                ]
            };

            var frame = new AnimationFile.Frame();
            frame.Transforms.Add(new RmvVector3(Vector3.Zero));
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            frame.Transforms.Add(new RmvVector3(Vector3.One));
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));

            var animationPart = new AnimationFile.AnimationPart();
            animationPart.DynamicFrames.Add(frame);
            skeletonFile.AnimationParts.Add(animationPart);

            return new GameSkeleton(skeletonFile, null!);
        }
    }
}
