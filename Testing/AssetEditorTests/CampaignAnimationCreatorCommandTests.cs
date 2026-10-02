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
using Shared.Ui.BaseDialogs.StandardDialog;

namespace AssetEditorTests
{
    [TestClass]
    [DoNotParallelize]
    public class CampaignAnimationCreatorCommandTests
    {
        public TestContext TestContext { get; set; } = null!;

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

        [DataTestMethod]
        [DataRow(90f, 0f)]
        [DataRow(-90f, 0f)]
        [DataRow(0f, 90f)]
        [DataRow(0f, -90f)]
        public void Convert_FixedCoordinateRotation_PreservesBodyAndAttachmentPose(float pitch, float roll)
        {
            var correction = Quaternion.CreateFromYawPitchRoll(0, MathHelper.ToRadians(pitch), MathHelper.ToRadians(roll));
            var (skeleton, source, root) = CreateCoordinateAnimation(correction);
            var original = source.Clone();
            var command = new ConvertCampaignAnimationCommand(new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);

            Assert.IsTrue(command.Execute(source, root, out var converted, skeleton));
            Assert.IsNotNull(converted);

            for (var frameIndex = 0; frameIndex < source.DynamicFrames.Count; frameIndex++)
            {
                Assert.AreEqual(Vector3.Zero, converted.DynamicFrames[frameIndex].Position[0]);
                Assert.AreEqual(Quaternion.Identity, converted.DynamicFrames[frameIndex].Rotation[0]);
                AssertBodyPosePreserved(skeleton, original, converted, frameIndex);
                foreach (var boneIndex in new[] { 2, 4, 5, 6 })
                {
                    Assert.AreEqual(original.DynamicFrames[frameIndex].Position[boneIndex], converted.DynamicFrames[frameIndex].Position[boneIndex]);
                    Assert.AreEqual(original.DynamicFrames[frameIndex].Rotation[boneIndex], converted.DynamicFrames[frameIndex].Rotation[boneIndex]);
                }
                CollectionAssert.AreEqual(original.DynamicFrames[frameIndex].Position, source.DynamicFrames[frameIndex].Position);
                CollectionAssert.AreEqual(original.DynamicFrames[frameIndex].Rotation, source.DynamicFrames[frameIndex].Rotation);
                CollectionAssert.AreEqual(original.DynamicFrames[frameIndex].Scale, converted.DynamicFrames[frameIndex].Scale);
            }
        }

        [TestMethod]
        public void Convert_FixedCoordinateRotation_SaveAndReloadPreservesPose()
        {
            var (skeleton, source, root) = CreateCoordinateAnimation(Quaternion.CreateFromAxisAngle(Vector3.Right, -MathHelper.PiOver2));
            var command = new ConvertCampaignAnimationCommand(new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);
            Assert.IsTrue(command.Execute(source, root, out var converted, skeleton));

            byte[]? savedBytes = null;
            var packFiles = new Mock<IPackFileService>();
            packFiles.Setup(x => x.GetEditablePack()).Returns(new PackFileContainer("test.pack"));
            var fileSave = new Mock<IFileSaveService>();
            fileSave.Setup(x => x.SaveAs(".anim", It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, bytes) => savedBytes = bytes)
                .Returns(PackFile.CreateFromBytes("campaign.anim", [1]));
            var save = new SaveCampaignAnimationCommand(packFiles.Object, fileSave.Object, new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);

            Assert.IsTrue(save.Execute(skeleton, converted));
            Assert.IsNotNull(savedBytes);
            var reloaded = new AnimationClip(AnimationFile.Create(new ByteChunk(savedBytes)), skeleton);
            Assert.AreEqual(source.Duration, reloaded.Duration);
            Assert.AreEqual(source.DynamicFrames.Count, reloaded.DynamicFrames.Count);
            for (var frameIndex = 0; frameIndex < source.DynamicFrames.Count; frameIndex++)
                AssertBodyPosePreserved(skeleton, source, reloaded, frameIndex, 0.001f);
        }

        [TestMethod]
        public void Convert_FixedCoordinateRotation_HandlesQuaternionSignAndUniformScale()
        {
            var correction = Quaternion.CreateFromAxisAngle(Vector3.Right, -MathHelper.PiOver2);
            var (skeleton, source, root) = CreateCoordinateAnimation(correction);
            source.DynamicFrames[1].Rotation[0] = -correction;
            foreach (var frame in source.DynamicFrames)
                frame.Scale[0] = new Vector3(2);
            var command = new ConvertCampaignAnimationCommand(new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);

            Assert.IsTrue(command.Execute(source, root, out var converted, skeleton));
            Assert.IsNotNull(converted);
            for (var frameIndex = 0; frameIndex < source.DynamicFrames.Count; frameIndex++)
            {
                AssertBodyPosePreserved(skeleton, source, converted, frameIndex);
                CollectionAssert.AreEqual(source.DynamicFrames[frameIndex].Scale, converted.DynamicFrames[frameIndex].Scale);
            }
        }

        [DataTestMethod]
        [DataRow(0f, 90f, 0f)]
        [DataRow(5f, 0f, 0f)]
        [DataRow(90f, 0f, 10f)]
        public void Convert_HeadingAndAnimatedTilt_KeepExistingRootMotionRemoval(float pitch, float yaw, float pitchChange)
        {
            var correction = Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(yaw), MathHelper.ToRadians(pitch), 0);
            var (skeleton, source, root) = CreateCoordinateAnimation(correction);
            if (pitchChange != 0)
                source.DynamicFrames[1].Rotation[0] = Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(yaw), MathHelper.ToRadians(pitch + pitchChange), 0);
            var original = source.Clone();
            var command = new ConvertCampaignAnimationCommand(new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);

            Assert.IsTrue(command.Execute(source, root, out var converted, skeleton));
            Assert.IsNotNull(converted);
            for (var frameIndex = 0; frameIndex < source.DynamicFrames.Count; frameIndex++)
            {
                Assert.AreEqual(Vector3.Zero, converted.DynamicFrames[frameIndex].Position[0]);
                Assert.AreEqual(Quaternion.Identity, converted.DynamicFrames[frameIndex].Rotation[0]);
                for (var boneIndex = 1; boneIndex < source.AnimationBoneCount; boneIndex++)
                {
                    Assert.AreEqual(original.DynamicFrames[frameIndex].Position[boneIndex], converted.DynamicFrames[frameIndex].Position[boneIndex]);
                    Assert.AreEqual(original.DynamicFrames[frameIndex].Rotation[boneIndex], converted.DynamicFrames[frameIndex].Rotation[boneIndex]);
                }
            }
        }

        [TestMethod]
        public void Convert_FixedCoordinateRotation_RepeatedConversionDoesNotChangePose()
        {
            var (skeleton, source, root) = CreateCoordinateAnimation(Quaternion.CreateFromAxisAngle(Vector3.Right, -MathHelper.PiOver2));
            var command = new ConvertCampaignAnimationCommand(new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);

            Assert.IsTrue(command.Execute(source, root, out var converted, skeleton));
            Assert.IsTrue(command.Execute(converted, root, out var repeated, skeleton));
            Assert.IsNotNull(repeated);
            for (var frameIndex = 0; frameIndex < source.DynamicFrames.Count; frameIndex++)
                AssertBodyPosePreserved(skeleton, source, repeated, frameIndex);
            CollectionAssert.AreEqual(AnimationFile.ConvertToBytes(converted!.ConvertToFileFormat(skeleton)), AnimationFile.ConvertToBytes(repeated.ConvertToFileFormat(skeleton)));
        }

        [TestMethod]
        public void Convert_FixedCoordinateRotation_NoSkeletonShowsChineseDialog()
        {
            var (_, source, root) = CreateCoordinateAnimation(Quaternion.CreateFromAxisAngle(Vector3.Right, -MathHelper.PiOver2));
            var dialogs = new Mock<IStandardDialogs>();
            string? message = null;
            string? title = null;
            dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((text, caption) => { message = text; title = caption; });
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            Assert.IsFalse(command.Execute(source, root, out var converted));
            Assert.IsNull(converted);
            Assert.AreEqual("无法转换动画：请先加载骨骼，以保留动作的坐标校正。", message);
            Assert.AreEqual("错误", title);
            WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
            {
                var application = System.Windows.Application.Current;
                var originalMainWindow = application.MainWindow;
                var originalWindowCount = application.Windows.Count;
                using var dialog = new MessageDialogWindow(title!, message!, MessageDialogButtonSet.Ok, System.Windows.MessageBoxImage.Error);
                try
                {
                    Assert.AreEqual(message, dialog.Message);
                    var ok = (System.Windows.Controls.Button)dialog.FindName("OkButton");
                    Assert.AreEqual("确定", ok.Content);
                    var content = (System.Windows.FrameworkElement)dialog.Content;
                    dialog.Content = null;
                    var preview = new System.Windows.Controls.Border
                    {
                        Background = (System.Windows.Media.Brush)application.FindResource("AeBrush.Canvas"),
                        Child = content
                    };
                    preview.Measure(new System.Windows.Size(440, 220));
                    preview.Arrange(new System.Windows.Rect(0, 0, 440, 220));
                    preview.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(440, 220, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(preview);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    var path = Path.Combine(AppContext.BaseDirectory, "campaign-coordinate-correction-dialog.png");
                    using (var stream = File.Create(path)) encoder.Save(stream);
                    TestContext.AddResultFile(path);
                }
                finally
                {
                    dialog.Close();
                    application.MainWindow = originalMainWindow;
                }
                Assert.AreEqual(originalWindowCount, application.Windows.Count);
                Assert.AreSame(originalMainWindow, application.MainWindow);
            });
        }

        [TestMethod]
        public void Convert_FixedCoordinateRotation_SkeletonFrameMismatchReportsFrame()
        {
            var (skeleton, source, root) = CreateCoordinateAnimation(Quaternion.CreateFromAxisAngle(Vector3.Right, -MathHelper.PiOver2));
            source.DynamicFrames[1].Position.RemoveAt(6);
            source.DynamicFrames[1].Rotation.RemoveAt(6);
            source.DynamicFrames[1].Scale.RemoveAt(6);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ConvertCampaignAnimationCommand(dialogs.Object, LocalizationManager.Instance);

            Assert.IsFalse(command.Execute(source, root, out var converted, skeleton));
            Assert.IsNull(converted);
            dialogs.Verify(x => x.ShowDialogBox("无法转换动画：第 2 帧的骨骼数据不完整。", "错误"), Times.Once);
        }

        [TestMethod]
        public void Convert_OrdinaryBoneQuarterTurn_KeepsExistingBehavior()
        {
            var (skeleton, source, _) = CreateCoordinateAnimation(Quaternion.Identity);
            foreach (var frame in source.DynamicFrames)
                frame.Rotation[1] = Quaternion.CreateFromAxisAngle(Vector3.Right, -MathHelper.PiOver2);
            var original = source.Clone();
            var command = new ConvertCampaignAnimationCommand(new Mock<IStandardDialogs>().Object, LocalizationManager.Instance);

            Assert.IsTrue(command.Execute(source, new SkeletonBoneNode { BoneIndex = 1, BoneName = "root" }, out var converted, skeleton));
            Assert.IsNotNull(converted);
            for (var frameIndex = 0; frameIndex < source.DynamicFrames.Count; frameIndex++)
            {
                Assert.AreEqual(Quaternion.Identity, converted.DynamicFrames[frameIndex].Rotation[1]);
                Assert.AreEqual(Vector3.Zero, converted.DynamicFrames[frameIndex].Position[1]);
                foreach (var boneIndex in new[] { 0, 2, 3, 4, 5, 6 })
                {
                    Assert.AreEqual(original.DynamicFrames[frameIndex].Position[boneIndex], converted.DynamicFrames[frameIndex].Position[boneIndex]);
                    Assert.AreEqual(original.DynamicFrames[frameIndex].Rotation[boneIndex], converted.DynamicFrames[frameIndex].Rotation[boneIndex]);
                }
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

        private static (GameSkeleton Skeleton, AnimationClip Animation, SkeletonBoneNode Root) CreateCoordinateAnimation(Quaternion correction)
        {
            var file = new AnimationFile
            {
                Header = { SkeletonName = "coordinate_animation_test" },
                Bones =
                [
                    new() { Id = 0, Name = "animroot", ParentId = -1 },
                    new() { Id = 1, Name = "root", ParentId = 0 },
                    new() { Id = 2, Name = "head_0", ParentId = 1 },
                    new() { Id = 3, Name = "weapon_1", ParentId = 0 },
                    new() { Id = 4, Name = "attachment", ParentId = 3 },
                    new() { Id = 5, Name = "independent_root", ParentId = -1 },
                    new() { Id = 6, Name = "independent_child", ParentId = 5 }
                ]
            };
            var skeleton = GameSkeleton.CreateFromAnimationFile(file, new AnimationPlayer());
            var inverse = Quaternion.Inverse(correction);
            var animation = new AnimationClip { Duration = TimeSpan.FromSeconds(0.15) };
            for (var frameIndex = 0; frameIndex < 3; frameIndex++)
            {
                animation.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position =
                    [
                        new Vector3(frameIndex * 0.25f, 0, frameIndex * 0.5f),
                        Vector3.Transform(Vector3.Up, inverse),
                        new Vector3(0.1f * frameIndex, 1, 0),
                        Vector3.Transform(new Vector3(0.5f, 1.25f, -0.25f), inverse),
                        new Vector3(0, 0.5f, 0),
                        new Vector3(3, 2, 1),
                        Vector3.Up
                    ],
                    Rotation = [correction, inverse, Quaternion.CreateFromYawPitchRoll(0.1f * frameIndex, 0, 0), inverse, Quaternion.Identity, Quaternion.Identity, Quaternion.Identity],
                    Scale = Enumerable.Repeat(Vector3.One, 7).ToList()
                });
            }
            return (skeleton, animation, new SkeletonBoneNode { BoneIndex = 0, BoneName = "animroot", ParentBoneIndex = -1 });
        }

        private static void AssertBodyPosePreserved(GameSkeleton skeleton, AnimationClip source, AnimationClip converted, int frameIndex, float tolerance = 0.0001f)
        {
            var before = AnimationSampler.Sample(frameIndex, 0, skeleton, source);
            var after = AnimationSampler.Sample(frameIndex, 0, skeleton, converted);
            for (var boneIndex = 1; boneIndex < skeleton.BoneCount; boneIndex++)
            {
                var expected = before.GetSkeletonAnimatedWorld(skeleton, boneIndex);
                if (boneIndex < 5)
                    expected.Translation -= source.DynamicFrames[frameIndex].Position[0];
                var actual = after.GetSkeletonAnimatedWorld(skeleton, boneIndex);
                AssertVector(expected.Right, actual.Right, tolerance);
                AssertVector(expected.Up, actual.Up, tolerance);
                AssertVector(expected.Backward, actual.Backward, tolerance);
                AssertVector(expected.Translation, actual.Translation, tolerance);
            }
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance)
        {
            Assert.AreEqual(expected.X, actual.X, tolerance);
            Assert.AreEqual(expected.Y, actual.Y, tolerance);
            Assert.AreEqual(expected.Z, actual.Z, tolerance);
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
