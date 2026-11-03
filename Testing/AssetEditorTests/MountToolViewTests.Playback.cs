using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimationEditor.MountAnimationCreator;
using AnimationEditor.MountAnimationCreator.Views;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using HostView = AnimationEditor.Common.BaseControl.EditorHostView;
using PlayerView = AnimationEditor.Common.AnimationPlayer.AnimationPlayerView;

namespace AssetEditorTests;

public partial class MountToolViewTests
{
    [TestMethod]
    public void PlaybackSources_OnlyMountBecomesTheClockAndEachSummaryUsesItsOwnClipIncludingSubCentisecondDurations()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var input = new PlaybackInputs();
            var vm = input.Model;
            Assert.IsFalse(vm.HasPlaybackSources.Value);
            Assert.IsNull(vm.SelectedPlaybackSource);
            CollectionAssert.AreEqual(new[] { "未加载动画", "未加载动画", "尚未生成" },
                vm.AnimationPlaybackSources.Select(source => source.Summary.Value).ToArray());
            input.Editor.SetAnimationClip(input.Mount, MountToolTests.Clip(input.Skeleton, 29, 2.75), "carrier.anim");
            Assert.AreEqual("mount", vm.SelectedPlaybackSource!.Id);
            Assert.AreSame(input.Mount, vm.Player.SelectedMainAnimation.Asset);
            Assert.AreEqual(29, vm.Player.SelectedAnimationFrameCount.Value);
            Assert.AreEqual(2.75f, vm.Player.SelectedAnimationMaxTime.Value);

            input.Editor.SetAnimationClip(input.Rider, MountToolTests.Clip(input.Skeleton, 7, .005), "rider.anim");
            var rider = vm.AnimationPlaybackSources.Single(source => source.Id == "rider");
            var mount = vm.AnimationPlaybackSources.Single(source => source.Id == "mount");
            Assert.AreEqual(7, rider.FrameCount.Value);
            Assert.AreEqual(.005, rider.DurationSeconds.Value, .00000001);
            StringAssert.Contains(rider.Summary.Value, .005.ToString("0.#######", System.Globalization.CultureInfo.CurrentCulture));
            Assert.AreEqual(29, mount.FrameCount.Value);
            Assert.AreEqual(2.75, mount.DurationSeconds.Value);
            Assert.AreEqual(0, vm.AnimationPlaybackSources.Single(source => source.Id == "mount-preview").FrameCount.Value);
            Assert.AreEqual("rider", vm.SelectedPlaybackSource!.Id, "Without an explicit preference the normal rider source becomes primary when available.");
            Assert.AreSame(input.Rider.AnimationClip, input.Rider.Player.AnimationClip);
        });
    }

    [TestMethod]
    public void PlaybackSources_AnExplicitChoiceSurvivesInputRefreshAndDoesNotSeekOrPauseThePlayers()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var input = new PlaybackInputs();
            input.LoadInputs();
            var vm = input.Model;
            vm.Player.IsEnabled.Value = true;
            vm.Player.ToggleAnimationPausePlay();
            vm.Player.PlaybackPositionSeconds = .4f;
            var riderTime = input.Rider.Player.CurrentTime;
            var mountTime = input.Mount.Player.CurrentTime;
            vm.SelectedPlaybackSource = vm.AvailablePlaybackSources.Single(source => source.Id == "mount");
            Assert.AreSame(input.Mount, vm.Player.SelectedMainAnimation.Asset);
            Assert.AreEqual(riderTime, input.Rider.Player.CurrentTime);
            Assert.AreEqual(mountTime, input.Mount.Player.CurrentTime);
            Assert.IsFalse(input.Rider.Player.IsPlaying);
            Assert.IsFalse(input.Mount.Player.IsPlaying);
            input.Editor.SetAnimationClip(input.Rider, MountToolTests.Clip(input.Skeleton, 21, 1.5), "replacement.anim");
            Assert.AreEqual("mount", vm.SelectedPlaybackSource!.Id);
            Assert.AreEqual(21, vm.AnimationPlaybackSources.Single(source => source.Id == "rider").FrameCount.Value);
            Assert.AreEqual(2f, vm.Player.SelectedAnimationMaxTime.Value);
        });
    }

    [TestMethod]
    public void PlaybackSources_ActualGeneratedResultHasItsOwnLengthAndRecoversTheExplicitClockAfterInvalidation()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var input = new PlaybackInputs();
            input.LoadInputs();
            input.ConnectAndGenerate();
            var vm = input.Model;
            vm.AnimationSettings.FitAnimation = false;
            vm.AnimationSettings.LoopCounter.Value = 3;
            Assert.IsNotNull(input.Preview.AnimationClip);
            var result = vm.AnimationPlaybackSources.Single(source => source.Id == "mount-preview");
            Assert.AreEqual(22, result.FrameCount.Value);
            Assert.AreEqual(3d, result.DurationSeconds.Value);
            Assert.AreEqual(8, vm.AnimationPlaybackSources.Single(source => source.Id == "rider").FrameCount.Value);
            Assert.AreEqual(16, vm.AnimationPlaybackSources.Single(source => source.Id == "mount").FrameCount.Value);
            vm.SelectedPlaybackSource = result;
            Assert.AreEqual(22, vm.Player.SelectedAnimationFrameCount.Value);
            Assert.AreEqual(3f, vm.Player.SelectedAnimationMaxTime.Value);

            input.Editor.SetAnimationClip(input.Rider, null, "");
            Assert.IsNull(input.Preview.AnimationClip);
            Assert.AreEqual("尚未生成", result.Summary.Value);
            Assert.AreEqual("mount", vm.SelectedPlaybackSource!.Id);
            input.Editor.SetAnimationClip(input.Rider, MountToolTests.Clip(input.Skeleton, 9, .75), "new-rider.anim");
            Assert.IsNotNull(input.Preview.AnimationClip);
            Assert.AreEqual("mount-preview", vm.SelectedPlaybackSource!.Id);
            Assert.AreEqual(input.Preview.AnimationClip.Timebase!.FrameCount, result.FrameCount.Value);
            Assert.AreEqual(input.Preview.AnimationClip.Duration.TotalSeconds, result.DurationSeconds.Value);
            input.Editor.SetAnimationClip(input.Mount, null, "");
            Assert.AreEqual("rider", vm.SelectedPlaybackSource!.Id);
            input.Editor.SetAnimationClip(input.Rider, null, "");
            Assert.IsFalse(vm.HasPlaybackSources.Value);
            Assert.IsNull(vm.SelectedPlaybackSource);
            Assert.IsFalse(vm.Player.IsPlaying.Value);
        });
    }

    [DataTestMethod]
    [DataRow("rider", false)]
    [DataRow("rider", true)]
    [DataRow("mount", false)]
    [DataRow("mount", true)]
    [DataRow("mount-preview", false)]
    [DataRow("mount-preview", true)]
    public void PlaybackSources_TheSelectedClockControlsTheCommonNaturalLoopOrStop(string primary, bool loop)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var input = new PlaybackInputs();
            input.LoadInputs();
            input.ConnectAndGenerate();
            input.Model.AnimationSettings.FitAnimation = false;
            input.Model.AnimationSettings.LoopCounter.Value = 3;
            var vm = input.Model;
            vm.SelectedPlaybackSource = vm.AvailablePlaybackSources.Single(source => source.Id == primary);
            vm.Player.LoopAnimation.Value = loop;
            vm.Player.IsEnabled.Value = true;
            var clock = vm.Player.SelectedMainAnimation.Asset.Player;
            Assert.AreEqual(primary == "rider" ? 1d : primary == "mount" ? 2d : 3d, clock.Duration.TotalSeconds);
            if (primary != "rider")
            {
                input.Rider.Player.Update(new GameTime(TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.1)));
                Assert.AreEqual(0L, clock.PlaybackTime.CycleIndex, "The shorter unselected rider must not restart the scene.");
                Assert.IsTrue(clock.IsPlaying, "The shorter unselected rider must not pause the selected clock.");
            }
            clock.Update(new GameTime(clock.Duration + TimeSpan.FromMilliseconds(10), clock.Duration + TimeSpan.FromMilliseconds(10)));
            foreach (var asset in new[] { input.Rider, input.Mount, input.Preview })
            {
                Assert.AreEqual(loop, asset.Player.IsPlaying);
                Assert.AreEqual(loop ? 1L : 0L, asset.Player.PlaybackTime.CycleIndex);
                if (loop) Assert.AreEqual(TimeSpan.Zero, asset.Player.CurrentTime);
            }
            Assert.AreEqual(loop, vm.Player.IsPlaying.Value);
        });
    }

    [DataTestMethod]
    [DataRow("DarkTheme", 900)]
    [DataRow("DarkTheme", 1400)]
    [DataRow("LightTheme", 900)]
    [DataRow("LightTheme", 1400)]
    [DataRow("HighContrastDark", 900)]
    [DataRow("HighContrastDark", 1400)]
    [DataRow("HighContrastLight", 900)]
    [DataRow("HighContrastLight", 1400)]
    public void PlaybackSources_WholeMountHostRendersTheChineseSelectorSummariesAndSharedPlaybackRows(string theme, int width)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            ThemesController.SetTheme(Enum.Parse<ThemeType>(theme));
            try
            {
                using var input = new PlaybackInputs();
                input.LoadInputs();
                input.ConnectAndGenerate();
                var vm = input.Model;
                vm.Player.IsEnabled.Value = true;
                var view = new HostView { DataContext = vm };
                var root = new Border { Child = view, Background = (Brush)Application.Current.FindResource("AeBrush.Canvas") };
                void Layout()
                {
                    root.Measure(new System.Windows.Size(width, 1000));
                    root.Arrange(new Rect(0, 0, width, 1000));
                    root.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                }
                Layout();
                var sceneList = Descendants<ListView>(root).Single(list => ReferenceEquals(list.ItemsSource, vm.SceneObjects));
                for (DependencyObject? parent = sceneList; parent != null; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is Expander expander) { expander.IsExpanded = false; break; }
                Layout();
                var sourceView = Descendants<PlaybackSourcesView>(root).Single();
                var combo = (ComboBox)sourceView.FindName("MainPlaybackSource");
                combo.SelectedItem = vm.AvailablePlaybackSources.Single(source => source.Id == "mount");
                combo.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
                Layout();
                Assert.AreSame(input.Mount, vm.Player.SelectedMainAnimation.Asset);
                vm.Player.PlaybackPositionSeconds = 1.5f;
                combo.SelectedItem = vm.AvailablePlaybackSources.Single(source => source.Id == "rider");
                combo.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
                Layout();
                Assert.AreEqual(TimeSpan.FromSeconds(1.5), input.Mount.Player.CurrentTime,
                    "Shrinking the bound slider range must not seek the longer reference back through a coerced value.");
                combo.SelectedItem = vm.AvailablePlaybackSources.Single(source => source.Id == "mount");
                combo.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
                Layout();
                Assert.AreEqual("主播放动画", System.Windows.Automation.AutomationProperties.GetName(combo));
                var summaries = (ItemsControl)sourceView.FindName("AnimationSourceSummaries");
                foreach (var item in vm.AnimationPlaybackSources)
                {
                    var container = summaries.ItemContainerGenerator.ContainerFromItem(item);
                    Assert.IsNotNull(container, "Each source must have its own rendered summary row.");
                    var text = Descendants<TextBlock>(container!).Single(text =>
                        ReferenceEquals(text.DataContext, item) && text.Text == item.Summary.Value);
                    Assert.IsTrue(text.ActualWidth > 0);
                    var position = text.TranslatePoint(new System.Windows.Point(), sourceView);
                    Assert.IsTrue(position.X >= -.5 && position.X + text.ActualWidth <= sourceView.ActualWidth + .5,
                        "Each source summary must fit the narrow business panel.");
                }
                CollectionAssert.AreEqual(new[] { "骑手", "坐骑", "生成结果" }, vm.AnimationPlaybackSources.Select(item => item.Name).ToArray());
                Assert.IsTrue(sourceView.ActualWidth > 200);
                var playerView = Descendants<PlayerView>(root).Single();
                var expanderPlayer = (Expander)playerView.FindName("AnimationPlayerExpander");
                Assert.IsTrue(expanderPlayer.IsExpanded);
                Assert.AreEqual("动画播放器", expanderPlayer.Header);
                Assert.AreEqual(0, Grid.GetRow((UIElement)playerView.FindName("PlaybackCommandRow")));
                Assert.AreEqual(1, Grid.GetRow((UIElement)playerView.FindName("PlaybackInformationRow")));
                Assert.AreEqual(2, Grid.GetRow((UIElement)playerView.FindName("PlaybackTimelineRow")));
                var slider = Descendants<Slider>(playerView).Single();
                Assert.AreEqual(2d, slider.Maximum);
                Assert.IsTrue(slider.ActualWidth > 200);
                var bitmap = new RenderTargetBitmap(width, 1000, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_MOUNT_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"playback-host-{theme}-{width}.png"));
                    encoder.Save(stream);
                }
            }
            finally { ThemesController.SetTheme(previousTheme); }
        });
    }

    private sealed class PlaybackInputs : IDisposable
    {
        public MountAnimationCreatorViewModel Model { get; }
        public SceneObjectEditor Editor { get; }
        public SceneObject Rider { get; }
        public SceneObject Mount { get; }
        public SceneObject Preview { get; }
        public GameSkeleton Skeleton { get; }
        private readonly SelectionManager _selection;
        public PlaybackInputs()
        {
            var (model, pfs, selection, editor) = CreateEditor();
            Model = model; Editor = editor; _selection = selection;
            Rider = model.GetSceneObjectFromId("rider")!.Data;
            Mount = model.GetSceneObjectFromId("mount")!.Data;
            Preview = model.Player.PlayerItems.Single(item => item.Asset.Id == "mount-preview").Asset;
            Skeleton = MountToolTests.Skeleton("test", ("animroot", -1), ("root", 0));
            var file = PackFile.CreateFromBytes("test.anim", AnimationFile.ConvertToBytes(MountToolTests.Clip(Skeleton, 1, 1).ConvertToFileFormat(Skeleton)));
            pfs.AddContainer(new PackFileContainer("playback-source") { IsCaPackFile = true,
                FileList = new() { [@"animations\skeletons\test.anim"] = file } });
            editor.SetSkeleton(Rider, file);
            editor.SetSkeleton(Mount, file);
        }
        public void LoadInputs()
        {
            Editor.SetAnimationClip(Rider, MountToolTests.Clip(Skeleton, 8, 1), "rider.anim");
            Editor.SetAnimationClip(Mount, MountToolTests.Clip(Skeleton, 16, 2), "mount.anim");
        }
        public void ConnectAndGenerate()
        {
            var geometry = new MeshObject(Mock.Of<IGraphicsCardGeometry>(), "test")
            {
                VertexArray = [new VertexPositionNormalTextureCustom { Position = new(0, 2, 0, 1), BlendWeights = new(1, 0, 0, 0) }], IndexArray = []
            };
            geometry.ChangeVertexType(UiVertexFormat.Weighted, false);
            var mesh = new Rmv2MeshNode(geometry, new WeightedMaterial(), null!, Mount.Player);
            Mount.ModelNode = mesh;
            Mount.TriggerMeshChanged();
            ((VertexSelectionState)_selection.CreateSelectionSate(GeometrySelectionMode.Vertex, mesh)).SetSelection([0]);
            Model.SetMountVertex();
            Assert.IsNotNull(Preview.AnimationClip, "Use the production generator rather than assigning a synthetic result to the view model.");
        }
        public void Dispose() => Model.Close();
    }
}
