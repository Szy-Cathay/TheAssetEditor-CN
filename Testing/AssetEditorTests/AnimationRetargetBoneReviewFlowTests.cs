using System.Windows.Threading;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Ui.Editors.BoneMapping;
using NUnitAssert = NUnit.Framework.Assert;
using TestAttribute = NUnit.Framework.TestAttribute;

namespace AssetEditorTests;

public class AnimationRetargetBoneReviewFlowTests
{
    [Test]
    public void ManualSearch_OpensFullSourceTreeWithReviewedTargetSelected()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var source = CreateSkeleton(
                    ("root", -1),
                    ("source_a", 0),
                    ("source_b", 0));
                var target = CreateSkeleton(
                    ("root", -1),
                    ("unknown", 0));
                var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
                skeletonLookup.Setup(service => service.GetSkeletonFileFromName("source")).Returns(source);
                skeletonLookup.Setup(service => service.GetSkeletonFileFromName("target")).Returns(target);

                var viewModel = new BoneMappingViewModel();
                var window = new BoneMappingWindow(viewModel);
                var windowFactory = new Mock<IAbstractFormFactory<BoneMappingWindow>>();
                windowFactory.Setup(factory => factory.Create()).Returns(window);
                var manager = new BoneManager(
                    Mock.Of<IStandardDialogs>(),
                    windowFactory.Object,
                    skeletonLookup.Object);
                manager.UpdateSourceSkeleton("source");
                manager.UpdateTargetSkeleton("target");
                manager.AutoMapBones();

                Exception? callbackException = null;
                var inspected = false;
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    () =>
                    {
                        try
                        {
                            var visibleSourceBones = FlattenBones(viewModel.ParentModelBones.Values)
                                .Where(bone => bone.IsVisible.Value)
                                .ToArray();
                            NUnitAssert.Multiple(() =>
                            {
                                NUnitAssert.That(viewModel.OnlyShowUsedBones.Value, Is.False);
                                NUnitAssert.That(visibleSourceBones, Has.Length.EqualTo(3));
                                NUnitAssert.That(viewModel.MeshBones.SelectedItem?.BoneIndex.Value, Is.EqualTo(1));
                            });
                            inspected = true;
                        }
                        catch (Exception exception)
                        {
                            callbackException = exception;
                        }
                        finally
                        {
                            window.Close();
                        }
                    });

                manager.ShowManualBoneMapping(manager.ReviewItems.Single());

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(callbackException, Is.Null);
                    NUnitAssert.That(inspected, Is.True);
                });
            });
    }

    [Test]
    public void ManualMapping_CancelledEditDoesNotLeakIntoCurrentScheme()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var skeleton = CreateSkeleton(("root", -1), ("spine", 0));
                var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
                skeletonLookup.Setup(service => service.GetSkeletonFileFromName("source")).Returns(skeleton);
                skeletonLookup.Setup(service => service.GetSkeletonFileFromName("target")).Returns(skeleton);

                var cancelledViewModel = new BoneMappingViewModel();
                var cancelledWindow = new BoneMappingWindow(cancelledViewModel);
                var reopenedViewModel = new BoneMappingViewModel();
                var reopenedWindow = new BoneMappingWindow(reopenedViewModel);
                var windowFactory = new Mock<IAbstractFormFactory<BoneMappingWindow>>();
                windowFactory.SetupSequence(factory => factory.Create())
                    .Returns(cancelledWindow)
                    .Returns(reopenedWindow);
                var manager = new BoneManager(
                    Mock.Of<IStandardDialogs>(),
                    windowFactory.Object,
                    skeletonLookup.Object);
                manager.UpdateSourceSkeleton("source");
                manager.UpdateTargetSkeleton("target");
                manager.AutoMapBones();
                var previewRevision = manager.BeginMappingPreview();
                manager.CompleteMappingPreview(previewRevision!.Value);
                manager.ConfirmMapping();

                cancelledWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    () =>
                    {
                        cancelledViewModel.MeshBones.SelectedItem = FlattenBones(cancelledViewModel.MeshBones.Values)
                            .Single(bone => bone.BoneIndex.Value == 1);
                        cancelledViewModel.ParentModelBones.SelectedItem = FlattenBones(cancelledViewModel.ParentModelBones.Values)
                            .Single(bone => bone.BoneIndex.Value == 0);
                        cancelledWindow.Close();
                    });
                manager.ShowBoneMappingWindowCommand.Execute(null);

                int? reopenedMappedIndex = null;
                reopenedWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    () =>
                    {
                        reopenedMappedIndex = FlattenBones(reopenedViewModel.MeshBones.Values)
                            .Single(bone => bone.BoneIndex.Value == 1)
                            .MappedBoneIndex.Value;
                        reopenedWindow.Close();
                    });
                manager.ShowBoneMappingWindowCommand.Execute(null);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(reopenedMappedIndex, Is.EqualTo(1));
                    NUnitAssert.That(manager.IsMappingConfirmed, Is.True);
                    NUnitAssert.That(manager.CanBatchRetarget, Is.True);
                });
            });
    }

    [Test]
    public void ManualMapping_AcceptedEditInvalidatesOnlyWhenContentChanges()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var skeleton = CreateSkeleton(("root", -1), ("spine", 0));
                var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
                skeletonLookup.Setup(service => service.GetSkeletonFileFromName("source")).Returns(skeleton);
                skeletonLookup.Setup(service => service.GetSkeletonFileFromName("target")).Returns(skeleton);

                var unchangedViewModel = new BoneMappingViewModel();
                var unchangedWindow = new BoneMappingWindow(unchangedViewModel);
                var changedViewModel = new BoneMappingViewModel();
                var changedWindow = new BoneMappingWindow(changedViewModel);
                var windowFactory = new Mock<IAbstractFormFactory<BoneMappingWindow>>();
                windowFactory.SetupSequence(factory => factory.Create())
                    .Returns(unchangedWindow)
                    .Returns(changedWindow);
                var manager = new BoneManager(
                    Mock.Of<IStandardDialogs>(),
                    windowFactory.Object,
                    skeletonLookup.Object);
                manager.UpdateSourceSkeleton("source");
                manager.UpdateTargetSkeleton("target");
                manager.AutoMapBones();
                var previewRevision = manager.BeginMappingPreview();
                manager.CompleteMappingPreview(previewRevision!.Value);
                manager.ConfirmMapping();

                unchangedWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    () => unchangedWindow.DialogResult = true);
                manager.ShowBoneMappingWindowCommand.Execute(null);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(manager.IsMappingConfirmed, Is.True);
                    NUnitAssert.That(manager.CanBatchRetarget, Is.True);
                });

                changedWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    () =>
                    {
                        changedViewModel.MeshBones.SelectedItem = FlattenBones(changedViewModel.MeshBones.Values)
                            .Single(bone => bone.BoneIndex.Value == 1);
                        changedViewModel.ParentModelBones.SelectedItem = FlattenBones(changedViewModel.ParentModelBones.Values)
                            .Single(bone => bone.BoneIndex.Value == 0);
                        changedWindow.DialogResult = true;
                    });
                manager.ShowBoneMappingWindowCommand.Execute(null);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(manager.IsMappingConfirmed, Is.False);
                    NUnitAssert.That(manager.CanBatchRetarget, Is.False);
                    NUnitAssert.That(manager.Bones.Single().Children.Single().MappedIndex, Is.EqualTo(0));
                });
            });
    }

    private static IEnumerable<AnimatedBone> FlattenBones(IEnumerable<AnimatedBone> bones)
    {
        foreach (var bone in bones)
        {
            yield return bone;
            foreach (var child in FlattenBones(bone.Children))
                yield return child;
        }
    }

    private static AnimationFile CreateSkeleton(params (string Name, int ParentId)[] bones)
    {
        return new AnimationFile
        {
            Bones = bones
                .Select((bone, index) => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = bone.Name,
                    ParentId = bone.ParentId
                })
                .ToArray()
        };
    }
}
