using System.Reflection;
using System.Windows;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using System.Collections.ObjectModel;

namespace Test.KitbashEditor.Animation
{
    [TestFixture]
    internal class AnimationControllerViewModelTests
    {
        #region Test helpers

        static GameSkeleton CreateTestSkeleton(AnimationPlayer player)
        {
            var animFile = new AnimationFile();
            animFile.Header = new AnimationFile.AnimationHeader { SkeletonName = "TestSkeleton" };
            animFile.Bones = new AnimationFile.BoneInfo[2];
            animFile.Bones[0] = new AnimationFile.BoneInfo { Name = "bone_0", ParentId = -1 };
            animFile.Bones[1] = new AnimationFile.BoneInfo { Name = "bone_1", ParentId = 0 };

            var frame = new AnimationFile.Frame();
            frame.Transforms.Add(new RmvVector3 { X = 0, Y = 0, Z = 0 });
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            frame.Transforms.Add(new RmvVector3 { X = 10, Y = 0, Z = 0 });
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));

            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            animFile.AnimationParts.Add(part);

            return new GameSkeleton(animFile, player);
        }

        static AnimationClip CreateTestClip(int frameCount, float seconds)
        {
            var clip = new AnimationClip();
            for (var index = 0; index < frameCount; index++)
            {
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [Microsoft.Xna.Framework.Vector3.Zero],
                    Rotation = [Microsoft.Xna.Framework.Quaternion.Identity],
                    Scale = [Microsoft.Xna.Framework.Vector3.One],
                });
            }

            clip.Duration = TimeSpan.FromSeconds(seconds);
            return clip;
        }

        (AnimationControllerViewModel ViewModel, KitbasherRootScene Scene) CreateViewModel()
        {
            var mockPfs = new Mock<IPackFileService>();
            var mockLookUp = new Mock<ISkeletonAnimationLookUpHelper>();
            mockLookUp.Setup(x => x.GetAllSkeletonFileNames())
                .Returns(new ObservableCollection<string>());

            var mockEventHub = new Mock<IEventHub>();

            var animComponent = new AnimationsContainerComponent();
            var kitbasherScene = new KitbasherRootScene(
                animComponent, mockPfs.Object, mockEventHub.Object);

            var vm = new AnimationControllerViewModel(
                mockPfs.Object, mockLookUp.Object, mockEventHub.Object, kitbasherScene);

            return (vm, kitbasherScene);
        }

        #endregion

        #region Bug fix: Show panel when skeleton exists but no animation selected

        [Test]
        public void KitbasherPlayer_DisablesRedundantPausedRefresh()
        {
            var (_, scene) = CreateViewModel();

            Assert.That(scene.Player.RefreshWhilePaused, Is.False);
        }

        [Test]
        public void OnEnableChanged_PanelVisible_WhenSkeletonExistsButNoAnimation()
        {
            // Bug fix: models with skeletons but without "stand_idle" default animation
            // should still show the animation panel so users can manually select an animation.
            var (vm, scene) = CreateViewModel();
            var skeleton = CreateTestSkeleton(scene.Player);
            typeof(KitbasherRootScene).GetProperty("Skeleton")!.SetValue(scene, skeleton);

            // Pre-condition: panel starts collapsed
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Collapsed));

            // Act - enable animation toggle
            vm.IsEnabled = true;

            // Assert - panel should be visible even without a selected animation
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Visible));
        }

        #endregion

        #region Existing behavior: no skeleton → panel stays collapsed

        [Test]
        public void OnEnableChanged_PanelCollapsed_WhenNoSkeleton()
        {
            // Models without a skeleton should not show the animation panel
            var (vm, _) = CreateViewModel();

            vm.IsEnabled = true;

            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Collapsed));
        }

        #endregion

        #region Existing behavior: disable hides panel

        [Test]
        public void OnEnableChanged_PanelCollapsed_WhenDisabled()
        {
            var (vm, scene) = CreateViewModel();
            var skeleton = CreateTestSkeleton(scene.Player);
            typeof(KitbasherRootScene).GetProperty("Skeleton")!.SetValue(scene, skeleton);

            // First enable → panel visible
            vm.IsEnabled = true;
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Visible));

            // Disable → panel collapsed
            vm.IsEnabled = false;
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Collapsed));
        }

        #endregion

        #region Enable/disable cycle preserves behavior

        [Test]
        public void OnEnableChanged_EnableDisableEnableCycle_PreservesVisibility()
        {
            var (vm, scene) = CreateViewModel();
            var skeleton = CreateTestSkeleton(scene.Player);
            typeof(KitbasherRootScene).GetProperty("Skeleton")!.SetValue(scene, skeleton);

            // Enable → visible
            vm.IsEnabled = true;
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Visible));

            // Disable → collapsed
            vm.IsEnabled = false;
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Collapsed));

            // Re-enable → visible again
            vm.IsEnabled = true;
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Visible));
        }

        #endregion

        #region Player.IsEnabled tracks ViewModel.IsEnabled

        [Test]
        public void OnEnableChanged_PlayerIsEnabled_TracksViewModelState()
        {
            var (vm, scene) = CreateViewModel();
            var skeleton = CreateTestSkeleton(scene.Player);
            typeof(KitbasherRootScene).GetProperty("Skeleton")!.SetValue(scene, skeleton);
            var player = scene.Player;

            // Player starts disabled
            Assert.That(player.IsEnabled, Is.False);

            // Enable
            vm.IsEnabled = true;
            Assert.That(player.IsEnabled, Is.True);

            // Disable
            vm.IsEnabled = false;
            Assert.That(player.IsEnabled, Is.False);
        }

        #endregion

        #region No animation selected does not crash

        [Test]
        public void OnEnableChanged_NoAnimationSelected_DoesNotThrow()
        {
            var (vm, scene) = CreateViewModel();
            var skeleton = CreateTestSkeleton(scene.Player);
            typeof(KitbasherRootScene).GetProperty("Skeleton")!.SetValue(scene, skeleton);
            var player = scene.Player;

            // No animation selected - should not throw
            Assert.DoesNotThrow(() => vm.IsEnabled = true);

            // Verify panel is visible and player is enabled
            Assert.That(vm.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Visible));
            Assert.That(player.IsEnabled, Is.True);
        }

        [Test]
        public void PlaybackPositionSeconds_SeeksAndKeepsPausedState()
        {
            var (vm, scene) = CreateViewModel();
            var skeleton = CreateTestSkeleton(scene.Player);
            typeof(KitbasherRootScene).GetProperty("Skeleton")!
                .SetValue(scene, skeleton);
            scene.Player.SetAnimation(
                CreateTestClip(10, 1),
                skeleton,
                true);
            vm.IsEnabled = true;
            scene.Player.Play();
            vm.PausePlayCommand.Execute(null);

            vm.PlaybackPositionSeconds = 0.4f;

            Assert.Multiple(() =>
            {
                Assert.That(scene.Player.CurrentTime, Is.EqualTo(TimeSpan.FromMilliseconds(400)));
                Assert.That(scene.Player.IsPlaying, Is.False);
                Assert.That(vm.MaxTimeSeconds, Is.EqualTo(1));
            });

            vm.PausePlayCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(scene.Player.CurrentTime, Is.EqualTo(TimeSpan.FromMilliseconds(400)));
                Assert.That(scene.Player.IsPlaying, Is.True);
            });

            vm.PlaybackPositionSeconds = 0.6f;
            Assert.Multiple(() =>
            {
                Assert.That(scene.Player.CurrentTime, Is.EqualTo(TimeSpan.FromMilliseconds(600)));
                Assert.That(scene.Player.IsPlaying, Is.True);
            });
        }

        #endregion
    }
}
