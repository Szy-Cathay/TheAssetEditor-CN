using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using Editors.AnimatioReTarget.Editor.Saving;
using Editors.AnimatioReTarget.Editor.Settings;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Editors.BoneMapping;
using Assert = NUnit.Framework.Assert;
using MappingView = CommonControls.Editors.BoneMapping.View.BoneMappingView;
using PreviewBone = Editors.Shared.Core.Common.ReferenceModel.SkeletonBoneNode;

namespace AssetEditorTests;

[NonParallelizable]
public class AnimationRetargetLayoutTests
{
    public static IEnumerable<TestCaseData> LayoutCases =>
        from theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight }
        from dpi in new[] { 96, 120, 144 }
        select new TestCaseData(theme, dpi);

    [TestCaseSource(nameof(LayoutCases))]
    public void Workspace_ContainsAllCategoriesAndKeepsActionsReachable(ThemeType theme, int dpi)
    {
        WithTheme(theme, () =>
        {
            var model = new WorkspaceProbe();
            var workspace = new RetargetWorkspaceView { DataContext = model };
            var width = 1320d * 96 / dpi;
            var height = 840d * 96 / dpi;
            Layout(workspace, width, height);
            var preview = (FrameworkElement)workspace.FindName("PreviewArea");
            var spacing = Descendants<TextBox>(preview).Single(item =>
                item.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Rendering.VisualOffset");
            var toolbar = (WrapPanel)((FrameworkElement)spacing.Parent).Parent;
            var spacingBounds = new Rect(spacing.TranslatePoint(new Point(), toolbar), spacing.RenderSize);
            Assert.That(spacing.ActualHeight, Is.LessThanOrEqualTo(20), "预览间距输入框应适配紧凑菜单栏");
            Assert.That(spacingBounds.Top, Is.GreaterThanOrEqualTo(2));
            Assert.That(spacingBounds.Bottom, Is.LessThanOrEqualTo(toolbar.ActualHeight - 2));
            var contentHost = (FrameworkElement)spacing.Template.FindName("PART_ContentHost", spacing);
            Assert.That(contentHost.ActualHeight, Is.GreaterThanOrEqualTo(spacing.FontSize * spacing.FontFamily.LineSpacing),
                "数字内容必须有足够的行高，不能靠裁剪塞入菜单栏");
            AssertInside(spacing, toolbar);
            var inspector = (EditorView)workspace.FindName("Inspector");
            var tabs = (TabControl)inspector.FindName("Categories");
            Assert.That(tabs.Items.Cast<TabItem>().Select(item => item.Header), Is.EqualTo(new[] { "来源", "目标", "重定向调整" }));
            for (var index = 0; index < 3; index++)
            {
                tabs.SetCurrentValue(TabControl.SelectedIndexProperty, index);
                Layout(workspace, width, height);
                var apply = (Button)inspector.FindName("ApplyButton");
                var save = (Button)inspector.FindName("SaveButton");
                AssertInside(apply, workspace);
                AssertInside(save, workspace);
                Assert.That(apply.Command, Is.SameAs(model.UpdateAnimationCommand));
                Assert.That(save.Command, Is.SameAs(model.SaveManager.ShowSaveSettingsCommand));
                if (index < 2)
                {
                    var input = Descendants<SceneInputView>(inspector).Single();
                    var animation = (SourceAnimationView)input.FindName("AnimationSelection");
                    Assert.That(animation.DataContext, Is.SameAs(index == 0 ? model.SourceModel : model.TargetModel));
                    AssertInside((FrameworkElement)input.FindName("SkeletonTree"), workspace);
                    var inputScroll = (ScrollViewer)input.FindName("InputScroll");
                    inputScroll.ScrollToEnd();
                    Layout(workspace, width, height);
                    AssertInside((FrameworkElement)animation.FindName("AssociatedInformation"), inputScroll);
                    inputScroll.ScrollToTop();
                    Layout(workspace, width, height);
                }
                if (index == 2)
                {
                    var tree = Descendants<TreeView>(inspector).Single();
                    var scroll = Descendants<ScrollViewer>(inspector).Single(item => item.Name == "BonePropertiesScroll");
                    Assert.That(tree.ActualHeight, Is.EqualTo(scroll.ActualHeight).Within(1));
                    Assert.That(tree.ActualHeight, Is.GreaterThan(140));
                    Assert.That(tree.ActualWidth, Is.GreaterThanOrEqualTo(156));
                    Assert.That(scroll.ActualWidth, Is.GreaterThanOrEqualTo(240));
                    Assert.That(Descendants<ScrollViewer>(tree).Single().ScrollableHeight, Is.GreaterThan(0));
                    Assert.That(Descendants<TextBox>(scroll).All(item => item.HorizontalContentAlignment == HorizontalAlignment.Left), Is.True);
                }
                if (dpi == 96 || index == 2)
                    Snapshot(workspace, $"retarget-{theme}-{dpi}-category-{index}.png", dpi);
            }
            Assert.That(model.ApplyCount, Is.Zero);
            var player = (FrameworkElement)workspace.FindName("Playback");
            Assert.That(Descendants<SourceAnimationView>((FrameworkElement)workspace.FindName("PreviewArea")), Is.Empty);
            AssertInside(player, workspace);
        });
    }

    [Test]
    public void CategorySwitch_PreservesSelectionEditsAndScrollWithoutApplying()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var model = new WorkspaceProbe { SelectedCategoryIndex = 2 };
            var workspace = new RetargetWorkspaceView { DataContext = model };
            Layout(workspace, 1040, 600);
            var inspector = (EditorView)workspace.FindName("Inspector");
            var tabs = (TabControl)inspector.FindName("Categories");
            Assert.That(tabs.SelectedIndex, Is.EqualTo(2));
            var tree = Descendants<TreeView>(inspector).Single();
            var root = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
            var selected = (TreeViewItem)root.ItemContainerGenerator.ContainerFromIndex(8);
            selected.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
            var bone = model.BoneManager.SelectedBone!;
            Assert.That(bone.BoneIndex, Is.EqualTo(9));
            var lengthInput = Descendants<TextBox>(inspector).Single(item => item.Name == "BoneLengthInput");
            lengthInput.SetCurrentValue(TextBox.TextProperty, "1.75");
            lengthInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            bone.TranslationOffset.X.Value = 3;
            bone.FreezeRotationZ = true;
            bone.SelectedRelativeBone = model.BoneManager.FlatBoneList[1];
            model.Settings.AnimationSpeedMult = 1.25f;
            var scroll = Descendants<ScrollViewer>(inspector).Single(item => item.Name == "BonePropertiesScroll");
            scroll.ScrollToVerticalOffset(120);
            Layout(workspace, 1040, 600);
            var offset = scroll.VerticalOffset;
            Assert.That(offset, Is.GreaterThan(0));
            foreach (var index in new[] { 0, 1, 0, 2 })
            {
                tabs.SetCurrentValue(TabControl.SelectedIndexProperty, index);
                Layout(workspace, 1040, 600);
            }
            Assert.That(model.SelectedCategoryIndex, Is.EqualTo(2));
            Assert.That(Descendants<TreeView>(inspector).Single(), Is.SameAs(tree));
            Assert.That(tree.SelectedItem, Is.SameAs(bone));
            Assert.That(model.BoneManager.SelectedBone, Is.SameAs(bone));
            Assert.That(bone.BoneLengthMult, Is.EqualTo(1.75f));
            Assert.That(bone.TranslationOffset.X.Value, Is.EqualTo(3));
            Assert.That(bone.FreezeRotationZ, Is.True);
            Assert.That(bone.SelectedRelativeBone, Is.SameAs(model.BoneManager.FlatBoneList[1]));
            Assert.That(model.Settings.AnimationSpeedMult, Is.EqualTo(1.25f));
            Assert.That(scroll.VerticalOffset, Is.EqualTo(offset).Within(1));
            Assert.That(model.ApplyCount, Is.Zero);
            ((Button)inspector.FindName("ApplyButton")).Command.Execute(null);
            Assert.That(model.ApplyCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void MappingAndSave_KeepDirectionActionsAndSmallWindowScrolling()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var targetBone = new AnimatedBone(7, "target_root");
            var sourceBone = new AnimatedBone(2, "source_root");
            targetBone.IsUsedByCurrentModel.Value = true;
            sourceBone.IsUsedByCurrentModel.Value = true;
            var mappingModel = new BoneMappingViewModel();
            mappingModel.Initialize(new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = "humanoid01d",
                ParnetModelSkeletonName = "humanoid01e",
                MeshBones = [targetBone],
                ParentModelBones = [sourceBone],
            });
            var mapping = new BoneMappingWindow(mappingModel);
            var mappingContent = WindowContent(mapping);
            Layout(mappingContent, 960, 600);
            var mappingView = Descendants<MappingView>(mappingContent).Single();
            Assert.That(mappingView.LeftSkeletonHeader, Is.EqualTo("目标骨骼 · 接收动作"));
            Assert.That(mappingView.RightSkeletonHeader, Is.EqualTo("源骨骼 · 提供动作"));
            mappingModel.ParentModelBones.SelectedItem = sourceBone;
            Assert.That(mappingModel.MeshBones.SelectedItem, Is.SameAs(targetBone));
            Assert.That(targetBone.MappedBoneIndex.Value, Is.EqualTo(2));
            Layout(mappingContent, 960, 600);
            Assert.That(Descendants<Button>(mappingContent).Select(button => button.Content),
                Is.SupersetOf(new[] { "按名称自动映射", "按层级自动映射", "清除对应", "清除自身及子骨骼对应", "子骨骼使用同一对应" }));
            foreach (var button in Descendants<Button>(mappingContent))
                AssertInside(button, mappingContent);
            Snapshot(mappingContent, "retarget-mapping.png", 96);

            var defaultMappingView = new MappingView { DataContext = mappingModel };
            Layout(defaultMappingView, 960, 600);
            var defaultLabels = Descendants<TextBlock>(defaultMappingView).Select(item => item.Text).ToArray();
            Assert.That(defaultLabels, Does.Contain(LocalizationManager.Instance.Get("BoneMapping.CurrentModelBones")));
            Assert.That(defaultLabels, Does.Contain(LocalizationManager.Instance.Get("BoneMapping.TargetModelBones")));
            Assert.That(defaultLabels, Does.Contain(LocalizationManager.Instance.Get("BoneMapping.PairHint")));
            Assert.That(Descendants<Button>(defaultMappingView).Select(button => button.Content), Does.Contain("复制到所有子骨骼"));

            var save = new SaveWindow(new SaveSettings(), Mock.Of<IStandardDialogs>());
            var saveContent = WindowContent(save);
            Layout(saveContent, 560, 360);
            AssertInside((Button)save.FindName("SaveButton"), saveContent);
            AssertInside((Button)save.FindName("CloseButton"), saveContent);
            var optionsScroll = Descendants<ScrollViewer>(saveContent).First();
            Assert.That(optionsScroll.ScrollableHeight, Is.GreaterThan(0));
            Assert.That(((Button)save.FindName("BatchButton")).Content, Is.EqualTo("批量动画重定向"));
            optionsScroll.ScrollToEnd();
            Layout(saveContent, 560, 360);
            AssertInside((Button)save.FindName("BatchButton"), optionsScroll);
            Snapshot(saveContent, "retarget-save-compact.png", 96);
            Layout(saveContent, 660, 500);
            Snapshot(saveContent, "retarget-save.png", 96);
        });
    }

    private static void WithTheme(ThemeType theme, Action action) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try { ThemesController.SetTheme(theme); action(); }
            finally { ThemesController.SetTheme(previous); }
        });

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static Border WindowContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var root = new Border { Child = content, Background = window.Background, DataContext = window.DataContext };
        System.Windows.Documents.TextElement.SetFontFamily(root, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(root, window.FontSize);
        System.Windows.Documents.TextElement.SetFontWeight(root, window.FontWeight);
        System.Windows.Documents.TextElement.SetForeground(root, window.Foreground);
        return root;
    }

    private static void AssertInside(FrameworkElement item, FrameworkElement root)
    {
        var bounds = new Rect(item.TranslatePoint(new Point(), root), item.RenderSize);
        Assert.That(bounds.Width, Is.GreaterThan(0), item.Name);
        Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(root.ActualHeight + 1), item.Name);
        Assert.That(bounds.Right, Is.LessThanOrEqualTo(root.ActualWidth + 1), item.Name);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }

    private static void Snapshot(FrameworkElement element, string name, int dpi)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi / 96),
            (int)Math.Ceiling(element.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name));
        encoder.Save(stream);
    }

    public sealed class WorkspaceProbe : ObservableObject
    {
        private int _selectedCategoryIndex;
        public int SelectedCategoryIndex { get => _selectedCategoryIndex; set => SetProperty(ref _selectedCategoryIndex, value); }
        public GridLength LeftColumnWidth { get; set; } = new(1, GridUnitType.Star);
        public GridLength RightColumnWidth { get; set; } = new(520);
        public object? GameWorld => null;
        public SceneObjectViewModel SourceModel { get; } = CreateScene("Source", "humanoid01e");
        public SceneObjectViewModel TargetModel { get; } = CreateScene("Target", "humanoid01d");
        public AnimationGenerationSettings Settings { get; } = new();
        public BoneManager BoneManager { get; } = new(Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(), Mock.Of<ISkeletonAnimationLookUpHelper>());
        public AnimationPlayerViewModel Player { get; } = new();
        public RenderingProbe Rendering { get; } = new();
        public SaveProbe SaveManager { get; } = new();
        public RelayCommand UpdateAnimationCommand { get; }
        public RelayCommand FocusCameraCommand { get; } = new(() => { });
        public RelayCommand ResetCameraCommand { get; } = new(() => { });
        public RelayCommand OpenFactionColourSettingsCommand { get; } = new(() => { });
        public int ApplyCount { get; private set; }

        public WorkspaceProbe()
        {
            UpdateAnimationCommand = new(() => ApplyCount++);
            var root = new SkeletonBoneNode_new("root", 0, -1) { HasMapping = true };
            for (var index = 1; index < 45; index++)
                root.Children.Add(new SkeletonBoneNode_new($"bone_{index:00}", index, 0) { HasMapping = index < 40 });
            BoneManager.Bones = [root];
            BoneManager.SelectedBone = root.Children[0];
            BoneManager.MappingSummary = "40 / 45";
            Player.IsEnabled.Value = true;
            Player.PlayerControlsVisibility.Value = Visibility.Visible;
            Player.SelectedAnimationMaxTime.Value = 2;
            Player.SelectedAnimationFrameCount.Value = 60;
            Player.SelectedAnimationFps.Value = 30;
        }

        private static SceneObjectViewModel CreateScene(string id, string skeleton)
        {
            var data = new SceneObject(id)
            {
                SkeletonSceneNode = new SkeletonNode(null),
                Player = new GameWorld.Core.Animation.AnimationPlayer(),
            };
            data.MeshName.Value = $"{id.ToLowerInvariant()}_model.rigid_model_v2";
            data.SkeletonName.Value = $"animations/skeletons/{skeleton}.anim";
            var packFiles = Mock.Of<IPackFileService>();
            var sceneEditor = new SceneObjectEditor(null!, null!, packFiles, null!, null!, Mock.Of<IEventHub>());
            var model = new SceneObjectViewModel(Mock.Of<IUiCommandFactory>(), packFiles,
                Mock.Of<IStandardDialogs>(), data, id, sceneEditor, Mock.Of<ISkeletonAnimationLookUpHelper>(), null!);
            for (var index = 0; index < 30; index++)
                model.SkeletonInformation.Bones.Add(new PreviewBone { BoneName = $"bone_{index:00}", BoneIndex = index });
            model.FragAndSlotSelection.AnimationFileName = "animations/battle/humanoid01e/example.anim";
            model.FragAndSlotSelection.MetaDataName = "example.meta";
            model.FragAndSlotSelection.MetaDataPersistName = "example_persistent.meta";
            return model;
        }
    }

    public sealed class RenderingProbe
    {
        public bool ShowGeneratedMesh { get; set; } = true;
        public bool ShowGeneratedSkeleton { get; set; } = true;
        public float VisualOffset { get; set; } = 2;
    }

    public sealed class SaveProbe
    {
        public RelayCommand ShowSaveSettingsCommand { get; } = new(() => { });
    }
}
