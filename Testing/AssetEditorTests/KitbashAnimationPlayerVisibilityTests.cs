using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.Events;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Components;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.Ui.Common;
using Assert = NUnit.Framework.Assert;
using PlayerView = KitbasherEditor.Views.EditorViews.AnimationPlayerView;

namespace AssetEditorTests;

[NonParallelizable]
public class KitbashAnimationPlayerVisibilityTests
{
    [Test]
    public void ExpandedWithoutSkeleton_KeepsResourceSelectorsVisible()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var input = new PlayerInputs();
            var view = new PlayerView { DataContext = input.Model };
            Expand(view, 760);
            var selectors = (Expander)view.FindName("AnimationSelectorsExpander");

            Assert.That(input.Model.IsEnabled, Is.True);
            Assert.That(input.Model.HeaderText, Is.EqualTo("未加载骨架"));
            Assert.That(IsVisibleWithin(selectors, view), Is.True,
                "An expanded player must expose its resource selectors even before a skeleton is available.");
            Assert.That(selectors.ActualHeight, Is.GreaterThan(0));
            Assert.That(input.Model.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Collapsed));
            Assert.That(input.Scene.Player.AnimationClip, Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SkeletonLoadedAfterExpansion_RefreshesPlaybackWithoutAnotherToggle(bool sameSkeletonPath)
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var input = new PlayerInputs();
            if (sameSkeletonPath)
                input.Model.SelectedSkeleton = PlayerInputs.SkeletonPath;
            var view = new PlayerView { DataContext = input.Model };
            Expand(view, 760);

            input.LoadSkeleton();
            Layout(view, 760);

            Assert.That(input.Model.AnimationControllerVisability.Value, Is.EqualTo(Visibility.Visible));
            Assert.That(IsVisibleWithin((FrameworkElement)view.FindName("PlaybackCommandRow"), view), Is.True);
            Assert.That(input.Model.IsEnabled, Is.True);
            Assert.That(input.Model.AnimationsForCurrentSkeleton, Has.Count.EqualTo(1));
        });
    }

    [TestCaseSource(nameof(ThemeLayouts))]
    public void Player_RendersResourceSelectionAndPlaybackAcrossSkeletonChanges(ThemeType theme, int width, double scale)
    {
        WithTheme(theme, () =>
        {
            var windowCount = Application.Current.Windows.Count;
            var input = new PlayerInputs();
            var view = new PlayerView { DataContext = input.Model };
            VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
            Expand(view, width);
            var selectors = (Expander)view.FindName("AnimationSelectorsExpander");
            selectors.IsExpanded = true;
            Layout(view, width);
            Assert.That(IsVisibleWithin(selectors, view), Is.True);
            Render(view, $"{theme}-{width}-{scale}-without-skeleton");

            input.LoadSkeleton();
            input.Model.SelectedAnimation = input.Model.AnimationsForCurrentSkeleton.Single();
            Layout(view, width);
            var commands = (FrameworkElement)view.FindName("PlaybackCommandRow");
            var timeline = (Slider)view.FindName("PlaybackTimelineRow");
            Assert.That(IsVisibleWithin(commands, view), Is.True);
            Assert.That(IsVisibleWithin(selectors, view), Is.True);
            Assert.That(selectors.TransformToAncestor(view).Transform(new Point()).Y,
                Is.GreaterThanOrEqualTo(timeline.TransformToAncestor(view).Transform(new Point()).Y + timeline.ActualHeight));
            Assert.That(timeline.Maximum, Is.EqualTo(1));
            if (input.Model.IsPlaying)
                input.Model.PausePlayCommand.Execute(null);
            timeline.SetCurrentValue(RangeBase.ValueProperty, .5);
            Assert.That(input.Scene.Player.CurrentTime.TotalSeconds, Is.EqualTo(.5).Within(.00001));
            Assert.That(input.Scene.Player.IsPlaying, Is.False);
            Assert.That(input.Model.IsPlaying, Is.False);
            Layout(view, width);
            Render(view, $"{theme}-{width}-{scale}-with-skeleton");

            var expander = (Expander)view.FindName("AnimationPlayerExpander");
            var header = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
            header.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            Layout(view, width);
            Assert.That(input.Scene.Player.IsEnabled, Is.False);
            header.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Layout(view, width);
            Assert.That(IsVisibleWithin(commands, view), Is.True);
            Assert.That(input.Scene.Player.IsEnabled, Is.True);
            Assert.That(input.Scene.Player.AnimationClip, Is.Not.Null);
            Assert.That(Application.Current.Windows.Count, Is.EqualTo(windowCount));
        });
    }

    private static IEnumerable<TestCaseData> ThemeLayouts()
    {
        foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
            foreach (var width in new[] { 480, 900 })
                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                    yield return new TestCaseData(theme, width, scale);
    }

    private static void WithTheme(ThemeType theme, Action action) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                ThemesController.SetTheme(theme);
                action();
            }
            finally
            {
                ThemesController.SetTheme(previous);
            }
        });

    private static void Expand(PlayerView view, double width)
    {
        Layout(view, width);
        var expander = (Expander)view.FindName("AnimationPlayerExpander");
        var header = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
        header.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Layout(view, width);
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    private static bool IsVisibleWithin(DependencyObject element, DependencyObject root)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
                return false;
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    private static void Render(FrameworkElement view, string name)
    {
        var dpi = VisualTreeHelper.GetDpi(view);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(view.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var output = Environment.GetEnvironmentVariable("AECN_KITBASH_PLAYER_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, $"{name}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private sealed class PlayerInputs
    {
        public const string SkeletonPath = @"animations\skeletons\player_test.anim";
        public AnimationControllerViewModel Model { get; }
        public KitbasherRootScene Scene { get; }
        private bool _skeletonAvailable;

        public PlayerInputs()
        {
            var skeleton = MountToolTests.Skeleton("player_test", ("root", -1));
            var file = MountToolTests.Clip(skeleton, 3, 1).ConvertToFileFormat(skeleton);
            var skeletonFile = PackFile.CreateFromBytes("player_test.anim", AnimationFile.ConvertToBytes(file));
            var animationFile = PackFile.CreateFromBytes("test.anim", skeletonFile.DataSource.ReadData());
            var packs = new Mock<IPackFileService>();
            packs.Setup(value => value.FindFile(SkeletonPath, null)).Returns(() => _skeletonAvailable ? skeletonFile : null);
            packs.Setup(value => value.FindFile(@"animations\test.anim", null)).Returns(animationFile);
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup.Setup(value => value.GetAllSkeletonFileNames()).Returns(new ObservableCollection<string> { SkeletonPath });
            lookup.Setup(value => value.GetAnimationsForSkeleton("player_test"))
                .Returns(new ObservableCollection<AnimationReference> { new(@"animations\test.anim", null!) });
            Action<KitbasherSkeletonChangedEvent>? onSkeletonChanged = null;
            var events = new Mock<IEventHub>();
            events.Setup(value => value.Register(It.IsAny<object>(), It.IsAny<Action<KitbasherSkeletonChangedEvent>>()))
                .Callback<object, Action<KitbasherSkeletonChangedEvent>>((_, action) => onSkeletonChanged = action);
            events.Setup(value => value.Publish(It.IsAny<KitbasherSkeletonChangedEvent>()))
                .Callback<KitbasherSkeletonChangedEvent>(change => onSkeletonChanged!(change));
            Scene = new KitbasherRootScene(new AnimationsContainerComponent(), packs.Object, events.Object);
            Model = new AnimationControllerViewModel(packs.Object, lookup.Object, events.Object, Scene);
        }

        public void LoadSkeleton()
        {
            _skeletonAvailable = true;
            Scene.SetSkeletonFromName("player_test");
        }
    }
}
