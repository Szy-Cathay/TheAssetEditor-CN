using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using Editors.AnimatioReTarget.Editor.Saving;
using Editors.AnimatioReTarget.Editor.Settings;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Components;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using NUnit.Framework;
using Shared.Core.Misc;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Assert = NUnit.Framework.Assert;
using TestContext = NUnit.Framework.TestContext;

namespace AssetEditorTests;

[NonParallelizable]
public class AnimationRetargetPlaybackTests
{
    [OneTimeSetUp]
    public void InitializeLocalization() => new LocalizationManager().LoadLanguage();

    private const string Walk = "animations/battle/humanoid01e/celestial_general/locomotion/hu1e_celestial_general_walk_01.anim";
    private const string Attack = "animations/battle/humanoid01e/celestial_general/attacks/hu1e_celestial_general_attack_01.anim";

    [TestCase(Walk, false)]
    [TestCase(Walk, true)]
    [TestCase(Attack, false)]
    [TestCase(Attack, true)]
    public void SourceOnly_UsesRealDurationAndLoopsAtTheEnd(string animation, bool loop)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var fixture = new PlaybackFixture();
            fixture.LoadSource(animation);
            var model = fixture.Model.Player;
            model.IsEnabled.Value = true;
            model.LoopAnimation.Value = loop;
            AssertTimeline(model, fixture.Source);
            fixture.Source.Player.Update(new GameTime(TimeSpan.Zero, fixture.Source.Player.Duration + TimeSpan.FromSeconds(0.1)));
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Source.Player.CurrentTime, Is.EqualTo(loop ? TimeSpan.Zero : fixture.Source.Player.Duration));
                Assert.That(fixture.Source.Player.IsPlaying, Is.EqualTo(loop));
                Assert.That(model.IsPlaying.Value, Is.EqualTo(loop));
                Assert.That(model.PlaybackPositionSeconds, Is.EqualTo((float)fixture.Source.Player.CurrentTime.TotalSeconds).Within(0.00001));
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Seeking_UpdatesActualPoseAndKeepsPlaybackState(bool playing)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var fixture = new PlaybackFixture();
            fixture.LoadSource(Walk);
            var model = fixture.Model.Player;
            model.IsEnabled.Value = true;
            if (!playing)
                model.ToggleAnimationPausePlay();
            var slider = BindSlider(model);
            var startPose = fixture.Source.Skeleton.GetAnimatedWorldTranform(fixture.Source.Skeleton.GetBoneIndexByName("hand_left"));
            model.IsScrubbing = true;
            slider.SetCurrentValue(Slider.ValueProperty, fixture.Source.Player.Duration.TotalSeconds * 0.45);
            fixture.Source.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)));
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Source.Player.CurrentTime.TotalSeconds, Is.EqualTo(slider.Value).Within(0.00001));
                Assert.That(slider.Value, Is.GreaterThan(0));
                Assert.That(fixture.Source.Skeleton.GetAnimatedWorldTranform(fixture.Source.Skeleton.GetBoneIndexByName("hand_left")), Is.Not.EqualTo(startPose));
                Assert.That(fixture.Source.Player.IsPlaying, Is.EqualTo(playing));
            });
            model.IsScrubbing = false;
            slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum);
            Assert.That(fixture.Source.Player.CurrentTime, Is.EqualTo(fixture.Source.Player.Duration));
            Assert.That(fixture.Source.Player.IsPlaying, Is.EqualTo(playing));
            slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum * 0.2);
            Assert.That(fixture.Source.Player.CurrentTime.TotalSeconds, Is.EqualTo(slider.Maximum * 0.2).Within(0.00001));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TargetAndResultChanges_KeepTheCurrentMainAnimationAndSeekRange(bool playing)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var fixture = new PlaybackFixture();
            fixture.LoadSource(Walk);
            var model = fixture.Model.Player;
            model.IsEnabled.Value = true;
            if (!playing)
                model.ToggleAnimationPausePlay();
            var slider = BindSlider(model);
            fixture.LoadTarget();
            AssertTimeline(model, fixture.Source);
            fixture.Editor.SetAnimation(fixture.Target, "animations/battle/humanoid01d/2handed_axe/stand/hu1d_2ha_stand_idle_01.anim");
            AssertTimeline(model, fixture.Source);
            fixture.Model.BoneManager.ApplyDefaultMapping();
            fixture.Model.UpdateAnimation();
            AssertTimeline(model, fixture.Result);
            slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum * 0.8);
            fixture.Model.Settings.AnimationSpeedMult = 2;
            fixture.Model.UpdateAnimation();
            AssertTimeline(model, fixture.Result);
            Assert.That(slider.Maximum, Is.EqualTo(fixture.Result.Player.Duration.TotalSeconds).Within(0.00001));
            slider.SetCurrentValue(Slider.ValueProperty, slider.Maximum * 0.3);
            Assert.That(fixture.Result.Player.CurrentTime.TotalSeconds, Is.EqualTo(slider.Value).Within(0.00001));
            Assert.That(fixture.Result.Player.IsPlaying, Is.EqualTo(playing));
            fixture.LoadSource(Attack);
            AssertTimeline(model, fixture.Source);
            fixture.Editor.SetSkeleton(fixture.Target, null!);
            AssertTimeline(model, fixture.Source);
            fixture.LoadTarget();
            fixture.Model.BoneManager.ApplyDefaultMapping();
            fixture.Model.UpdateAnimation();
            AssertTimeline(model, fixture.Result);
            model.LoopAnimation.Value = false;
            if (!model.IsPlaying.Value)
                model.ToggleAnimationPausePlay();
            fixture.Result.Player.Update(new GameTime(TimeSpan.Zero, fixture.Result.Player.Duration + TimeSpan.FromSeconds(0.1)));
            Assert.That(model.IsPlaying.Value, Is.False);
            model.LoopAnimation.Value = true;
            model.SetAnimationFirstFrame();
            model.ToggleAnimationPausePlay();
            fixture.Result.Player.Update(new GameTime(TimeSpan.Zero, fixture.Result.Player.Duration + TimeSpan.FromSeconds(0.1)));
            Assert.That(fixture.Result.Player.IsPlaying, Is.True);
            Assert.That(fixture.Result.Player.CurrentTime, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void TargetOnly_CanPreviewUntilSourceIsLoaded()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var fixture = new PlaybackFixture();
            fixture.LoadTarget();
            fixture.Editor.SetAnimation(fixture.Target, "animations/battle/humanoid01d/2handed_axe/stand/hu1d_2ha_stand_idle_01.anim");
            var model = fixture.Model.Player;
            model.IsEnabled.Value = true;
            AssertTimeline(model, fixture.Target);
            model.PlaybackPositionSeconds = 0.3f;
            Assert.That(fixture.Target.Player.CurrentTime.TotalSeconds, Is.EqualTo(0.3).Within(0.00001));
            fixture.LoadSource(Walk);
            AssertTimeline(model, fixture.Source);
            fixture.Editor.SetAnimation(fixture.Source, null!);
            AssertTimeline(model, fixture.Target);
        });
    }

    private static Slider BindSlider(AnimationPlayerViewModel model)
    {
        var slider = new Slider { DataContext = model };
        slider.SetBinding(Slider.MaximumProperty, new Binding("SelectedAnimationMaxTime.Value") { Mode = BindingMode.OneWay });
        slider.SetBinding(Slider.ValueProperty, new Binding(nameof(model.PlaybackPositionSeconds)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return slider;
    }

    private static void AssertTimeline(AnimationPlayerViewModel model, SceneObject expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(model.SelectedMainAnimation.Asset, Is.SameAs(expected));
            Assert.That(model.SelectedAnimationMaxTime.Value, Is.EqualTo(expected.Player.Duration.TotalSeconds).Within(0.00001));
            Assert.That(model.SelectedAnimationFrameCount.Value, Is.EqualTo(expected.Player.FrameCount));
            Assert.That(model.SelectedAnimationFps.Value, Is.EqualTo(expected.Player.FramesPerSecond).Within(0.00001));
            Assert.That(model.SelectedAnimationCurrentFrame.Value, Is.EqualTo(expected.Player.CurrentFrame));
        });
    }

    private sealed class PlaybackFixture : IDisposable
    {
        public AnimationRetargetEditor Model { get; }
        public SceneObjectEditor Editor { get; }
        public SceneObject Source => Model.SourceModel.Data;
        public SceneObject Target => Model.TargetModel.Data;
        public SceneObject Result => Model.GetSceneObjectFromId(AnimationRetargetIds.Generated)!.Data;
        private readonly Dictionary<string, PackFile> _files = new(StringComparer.OrdinalIgnoreCase);

        public PlaybackFixture()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AssetEditor.CN.sln")))
                root = root.Parent;
            Assert.That(root, Is.Not.Null);
            var dataRoot = Path.Combine(root!.FullName, "Data", "AnimationRetarget");
            foreach (var path in Directory.EnumerateFiles(dataRoot, "*.anim", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(dataRoot, path).Replace('\\', '/');
                _files[relative] = new PackFile(relative, new MemorySource(File.ReadAllBytes(path)));
            }
            var pfs = new Mock<IPackFileService>();
            pfs.Setup(x => x.FindFile(It.IsAny<string>(), It.IsAny<PackFileContainer>())).Returns((string path, PackFileContainer _) => _files.GetValueOrDefault(path.Replace('\\', '/')));
            pfs.Setup(x => x.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer>())).Returns((PackFile file, PackFileContainer _) => file.Name);
            pfs.Setup(x => x.GetAllPackfileContainers()).Returns([]);
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup.Setup(x => x.GetSkeletonFileFromName(It.IsAny<string>())).Returns((string path) => string.IsNullOrWhiteSpace(path) ? null! : AnimationFile.Create(_files[path.Replace('\\', '/')]));
            var game = new Mock<IWpfGame>();
            game.Setup(x => x.AddComponent(It.IsAny<SceneObject>())).Returns((SceneObject item) => item);
            var hub = new TestEventHub();
            var dialogs = Mock.Of<IStandardDialogs>();
            Editor = new SceneObjectEditor(game.Object, new SceneManager(null!, null!, hub), pfs.Object, new AnimationsContainerComponent(), null!, hub);
            var player = new AnimationPlayerViewModel();
            var builder = new SceneObjectViewModelBuilder(player, Editor, pfs.Object, lookup.Object, Mock.Of<IUiCommandFactory>(), dialogs, new ModelSkeletonResolver(pfs.Object));
            var parameters = new Mock<IEditorHostParameters>();
            parameters.SetupGet(x => x.GameWorld).Returns(game.Object);
            parameters.SetupGet(x => x.AnimationPlayerViewModel).Returns(player);
            parameters.SetupGet(x => x.SceneObjectEditor).Returns(Editor);
            parameters.SetupGet(x => x.SceneObjectViewModelBuilder).Returns(builder);
            parameters.SetupGet(x => x.ComponentInserter).Returns(Mock.Of<IComponentInserter>());
            var bones = new BoneManager(dialogs, Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(), lookup.Object);
            var save = new SaveManager(bones, lookup.Object, pfs.Object, new SaveSettings(), Mock.Of<IFileSaveService>(), dialogs, Mock.Of<IAbstractFormFactory<SaveWindow>>());
            Model = new AnimationRetargetEditor(parameters.Object, player, builder, dialogs, hub, save, bones, new AnimationReTargetRenderingComponent(null!), null!, null!, pfs.Object);
        }

        public void LoadSource(string path)
        {
            Editor.SetSkeleton(Source, _files["animations/skeletons/humanoid01e.anim"]);
            Editor.SetAnimation(Source, path);
        }

        public void LoadTarget() => Editor.SetSkeleton(Target, _files["animations/skeletons/humanoid01d.anim"]);
        public void Dispose() { Model.Dispose(); Model.Close(); }
    }
}
