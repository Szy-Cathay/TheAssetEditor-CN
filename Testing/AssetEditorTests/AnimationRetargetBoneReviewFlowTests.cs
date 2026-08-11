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
