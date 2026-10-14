using CommonControls.Editors.AnimationBatchExporter;
using GameWorld.Core.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class AnimationBatchExportTests
{
    [SetUp]
    public void LoadChineseResources()
    {
        Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "Language_Cn.json")), Is.True);
        new LocalizationManager().LoadLanguage();
    }

    [Test]
    public void Sources_ExcludeEditableTargetAndStartUnchecked()
    {
        var editable = new PackFileContainer("editable");
        var reference = new PackFileContainer("reference.pack");
        var service = new Mock<IPackFileService>();
        service.Setup(x => x.GetEditablePack()).Returns(editable);
        service.Setup(x => x.GetAllPackfileContainers()).Returns([editable, reference]);
        var model = new AnimationBatchExportViewModel(service.Object,
            Mock.Of<ISkeletonAnimationLookUpHelper>(), Mock.Of<IStandardDialogs>());

        Assert.That(model.PackfileList.Select(x => x.Container),
            Is.EquivalentTo(new[] { reference }));
        Assert.That(model.PackfileList.Single().Process.Value, Is.False);
    }

    [Test]
    public void FilterAndBulkSelection_KeepHiddenSelectionsAndClearAll()
    {
        var harness = new Harness();
        harness.SelectSource();
        Assert.That(harness.Model.Animations.All(x => !x.IsSelected.Value), Is.True);
        harness.Model.FilterText.Value = "walk";
        harness.Model.SelectFiltered();
        Assert.That(harness.Model.Animations.Count(x => x.IsSelected.Value), Is.EqualTo(1));
        harness.Model.FilterText.Value = "run";
        Assert.That(harness.Model.SelectionSummary.Value, Does.Contain("筛选外 1"));
        harness.Model.SelectFiltered();
        Assert.That(harness.Model.Animations.Count(x => x.IsSelected.Value), Is.EqualTo(2));
        harness.Model.ClearSelection();
        Assert.That(harness.Model.Animations.All(x => !x.IsSelected.Value), Is.True);
        Assert.That(harness.Model.CanExport.Value, Is.False);
    }

    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    [TestCase(8u)]
    public async Task Export_WritesOnlySelectedAnimationAndItsMatrix(uint version)
    {
        var harness = new Harness();
        var sourceBytes = harness.Source.FileList[Harness.Walk].DataSource.ReadData();
        harness.SelectSource();
        harness.Model.Animations.Single(x => x.Path == Harness.Walk).IsSelected.Value = true;
        harness.Model.SelectedOutputFormat.Value = version;

        var errors = await harness.Model.Process();

        Assert.That(errors, Is.Not.Null, harness.Model.Status.Value);
        Assert.That(errors!.HasData, Is.False);
        Assert.That(harness.Writes.Select(x => x.Path), Is.EquivalentTo(new[] { Harness.Walk, Harness.Matrix }));
        var written = harness.Writes.Single(x => x.Path == Harness.Walk);
        var animation = AnimationFile.Create(PackFile.CreateFromBytes("walk.anim", written.Content));
        Assert.That(animation.Header.Version, Is.EqualTo(version));
        Assert.That(animation.AnimationParts[0].DynamicFrames[0].Transforms[0].X, Is.EqualTo(2).Within(0.001));
        Assert.That(harness.Source.FileList[Harness.Walk].DataSource.ReadData(), Is.EqualTo(sourceBytes));
        Assert.That(harness.Model.Status.Value, Does.Contain("已写入 1 个动画，失败 0 个"));
        harness.Dialogs.Verify(x => x.ShowYesNoBox(
            It.Is<string>(text => text.Contains("1 个动画") && text.Contains("同路径文件会被覆盖") && text.Contains("output.pack")),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task NoSelection_DoesNotConfirmOrWrite()
    {
        var harness = new Harness();
        harness.SelectSource();
        Assert.That(await harness.Model.Process(), Is.Null);
        harness.Dialogs.Verify(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(harness.Writes, Is.Empty);
    }

    [Test]
    public async Task ShiftRange_ExportsVisibleRangeAndRetainsPreviouslyCheckedHiddenItems()
    {
        var harness = CreateSelectionHarness();
        var model = harness.Model;
        var visible = model.FilteredAnimations.Value;
        model.Animations.Single(x => x.Path == Harness.Walk).IsSelected.Value = true;
        Assert.That(model.BeginAnimationSelection(visible[1], false), Is.True);
        model.EndAnimationSelection();
        Assert.That(model.BeginAnimationSelection(visible[3], true), Is.True);
        model.EndAnimationSelection();
        Assert.That(model.SelectionSummary.Value, Does.Contain("筛选外 1"));

        var errors = await model.Process();

        Assert.That(errors!.HasData, Is.False);
        Assert.That(harness.Writes.Where(x => x.Path.EndsWith(".anim")).Select(x => x.Path),
            Is.EquivalentTo(visible.Skip(1).Take(3).Select(x => x.Path).Append(Harness.Walk)));
    }

    [Test]
    public void DragSelection_FillsSkippedRowsAndDoesNotToggleRepeatedly()
    {
        var model = CreateSelectionHarness().Model;
        var visible = model.FilteredAnimations.Value;
        model.BeginAnimationSelection(visible[0], false);
        model.ContinueAnimationSelection(visible[4]);
        model.ContinueAnimationSelection(visible[1]);
        model.ContinueAnimationSelection(visible[4]);
        model.EndAnimationSelection();
        Assert.That(visible.All(x => x.IsSelected.Value), Is.True);
        Assert.That(model.ContinueAnimationSelection(visible[0]), Is.False);

        model.BeginAnimationSelection(visible[3], false);
        model.ContinueAnimationSelection(visible[0]);
        model.ContinueAnimationSelection(visible[2]);
        model.EndAnimationSelection();
        Assert.That(visible.Where(x => x.IsSelected.Value), Is.EqualTo(new[] { visible[4] }));
    }

    [Test]
    public void FilterChangeAndClear_ResetRangeAnchorAndEndDragging()
    {
        var model = CreateSelectionHarness().Model;
        var previous = model.FilteredAnimations.Value;
        model.BeginAnimationSelection(previous[0], false);
        model.FilterText.Value = "range_04";
        Assert.That(model.ContinueAnimationSelection(previous[4]), Is.False);
        Assert.That(model.BeginAnimationSelection(previous[0], true), Is.False);
        model.BeginAnimationSelection(previous[4], true);
        model.EndAnimationSelection();
        Assert.That(previous.Where(x => x.IsSelected.Value), Is.EquivalentTo(new[] { previous[0], previous[4] }));

        model.FilterText.Value = "range_";
        model.ClearSelection();
        model.BeginAnimationSelection(previous[2], true);
        model.EndAnimationSelection();
        Assert.That(previous.Where(x => x.IsSelected.Value), Is.EqualTo(new[] { previous[2] }));
        model.Close();
        Assert.That(model.BeginAnimationSelection(previous[3], false), Is.False);
    }

    [TestCase("release")]
    [TestCase("leave")]
    [TestCase("capture")]
    [TestCase("unload")]
    [TestCase("context")]
    public void View_EndingPointerGestureStopsSelectionWithoutClearingChecks(string reason)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var model = CreateSelectionHarness().Model;
            var view = new AnimationBatchExportView { DataContext = model };
            var list = (ListBox)view.FindName("AnimationsList");
            var visible = model.FilteredAnimations.Value;
            model.BeginAnimationSelection(visible[0], false);
            if (reason == "release")
                list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            else if (reason == "leave" || reason == "capture")
                list.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                    { RoutedEvent = reason == "leave" ? Mouse.MouseLeaveEvent : Mouse.LostMouseCaptureEvent });
            else if (reason == "unload")
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            else
                view.DataContext = null;

            Assert.That(model.ContinueAnimationSelection(visible[4]), Is.False);
            Assert.That(visible.Where(x => x.IsSelected.Value), Is.EqualTo(new[] { visible[0] }));
        });
    }

    private static Harness CreateSelectionHarness()
    {
        var harness = new Harness();
        for (var index = 0; index < 5; index++)
            AddFile(harness.Source, $@"animations\units\range_{index:D2}.anim", CreateAnimation());
        harness.Model.RefreshSources();
        harness.SelectSource();
        harness.Model.FilterText.Value = "range_";
        return harness;
    }

    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    public async Task ExportToEight_ConvertsOlderFileLayoutAndRoundTrips(uint sourceVersion)
    {
        var harness = new Harness();
        var animation = AnimationFile.Create(PackFile.CreateFromBytes("walk.anim", CreateAnimation()));
        animation.Header.Version = sourceVersion;
        AddFile(harness.Source, Harness.Walk, AnimationFile.ConvertToBytes(animation));
        harness.Model.RefreshSources();
        harness.SelectSource();
        harness.Model.Animations.Single(x => x.Path == Harness.Walk).IsSelected.Value = true;
        harness.Model.SelectedOutputFormat.Value = 8;

        var errors = await harness.Model.Process();

        Assert.That(errors!.HasData, Is.False);
        var actual = AnimationFile.Create(PackFile.CreateFromBytes("output.anim", harness.Writes.Single(x => x.Path == Harness.Walk).Content));
        Assert.That(actual.Header.Version, Is.EqualTo(8));
        Assert.That(actual.AnimationParts.Single().DynamicFrames.Single().Transforms.Single().X, Is.EqualTo(2));
    }

    [Test]
    public async Task ExportToEight_PreservesStaticAndAbsentTracksInMultiplePartsWithoutSkeleton()
    {
        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 8, SkeletonName = "missing", UnknownValue_v8 = 6,
                FlagVariables = ["flag_a"], AnimationTotalPlayTimeInSec = 0.1f
            },
            Bones = [new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = -1 }],
            AnimationParts =
            [
                new AnimationFile.AnimationPart
                {
                    TranslationMappings = [new(10000)], RotationMappings = [new(-1)],
                    StaticFrame = new AnimationFile.Frame { Transforms = [new RmvVector3(1, 2, 3)] }
                },
                new AnimationFile.AnimationPart
                {
                    TranslationMappings = [new(-1)], RotationMappings = [new(0)],
                    DynamicFrames = [new AnimationFile.Frame { Quaternion = [new RmvVector4(0, 0, 0, 1)] }]
                }
            ]
        };
        var harness = new Harness();
        AddFile(harness.Source, Harness.Walk, AnimationFile.ConvertToBytes(animation));
        harness.Model.RefreshSources();
        harness.SelectSource();
        harness.Model.Animations.Single(x => x.Path == Harness.Walk).IsSelected.Value = true;
        harness.Model.SelectedOutputFormat.Value = 8;

        var errors = await harness.Model.Process();

        Assert.That(errors!.HasData, Is.False);
        var actual = AnimationFile.Create(PackFile.CreateFromBytes("output.anim", harness.Writes.Single().Content));
        Assert.That(actual.Header.Version, Is.EqualTo(8));
        Assert.That(actual.Header.UnknownValue_v8, Is.EqualTo(6));
        Assert.That(actual.Header.FlagVariables, Is.EqualTo(new[] { "flag_a" }));
        Assert.That(actual.AnimationParts, Has.Count.EqualTo(2));
        Assert.That(actual.AnimationParts[0].StaticFrame.Transforms.Single().Y, Is.EqualTo(2));
        Assert.That(actual.AnimationParts[0].RotationMappings.Single().HasValue, Is.False);
        Assert.That(actual.AnimationParts[1].TranslationMappings.Single().HasValue, Is.False);
        Assert.That(actual.AnimationParts[1].DynamicFrames.Single().Quaternion.Single().W, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelledConfirmation_DoesNotWrite()
    {
        var harness = new Harness();
        harness.SelectSource();
        harness.Model.SelectFiltered();
        harness.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.Cancel);
        Assert.That(await harness.Model.Process(), Is.Null);
        Assert.That(harness.Writes, Is.Empty);
    }

    [Test]
    public async Task MultipleSourcesWithSamePath_BlockAmbiguousOverwrite()
    {
        var harness = new Harness();
        var second = new PackFileContainer("second.pack");
        AddFile(second, Harness.Walk, CreateAnimation());
        harness.Containers.Add(second);
        harness.Model.RefreshSources();
        foreach (var source in harness.Model.PackfileList)
            source.Process.Value = true;
        harness.Model.SelectFiltered();
        Assert.That(await harness.Model.Process(), Is.Null);
        Assert.That(harness.Model.Status.Value, Does.Contain("同一输出路径"));
        Assert.That(harness.Writes, Is.Empty);
    }

    [Test]
    public async Task BrokenAnimation_DoesNotBlockValidSelectionOrCopyUnrelatedMatrices()
    {
        var harness = new Harness();
        AddFile(harness.Source, Harness.Run, [1, 2, 3]);
        harness.Model.RefreshSources();
        harness.SelectSource();
        harness.Model.SelectFiltered();
        var errors = await harness.Model.Process();
        Assert.That(errors!.Errors.Count, Is.EqualTo(1));
        Assert.That(harness.Writes.Select(x => x.Path), Is.EquivalentTo(new[] { Harness.Walk, Harness.Matrix }));
        Assert.That(harness.Model.Status.Value, Does.Contain("已写入 1 个动画，失败 1 个"));
    }

    [Test]
    public async Task ChangedTargetDuringConfirmation_StopsWithoutWriting()
    {
        var harness = new Harness();
        harness.SelectSource();
        harness.Model.SelectFiltered();
        harness.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => harness.Service.Setup(x => x.GetEditablePack()).Returns(harness.Source))
            .Returns(ShowMessageBoxResult.OK);
        var errors = await harness.Model.Process();
        Assert.That(errors!.HasData, Is.True);
        Assert.That(harness.Writes, Is.Empty);
    }

    [Test]
    public async Task WriteFailure_IsReportedAndAllowsRetry()
    {
        var harness = new Harness();
        harness.SelectSource();
        harness.Model.SelectFiltered();
        harness.Service.Setup(x => x.ApplyFileWritesAsync(It.IsAny<PackFileContainer>(),
                It.IsAny<IReadOnlyCollection<PackFileWrite>>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("write failed"));
        var errors = await harness.Model.Process();
        Assert.That(errors!.HasData, Is.True);
        Assert.That(harness.Model.Status.Value, Does.StartWith("导出未完成"));
        Assert.That(harness.Model.IsBusy.Value, Is.False);
        Assert.That(harness.Model.CanExport.Value, Is.True);
    }

    [Test]
    public async Task FolderTarget_WritesSelectedResourceAtOriginalRelativePath()
    {
        var root = Path.Combine(NUnit.Framework.TestContext.CurrentContext.WorkDirectory, "batch-export-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "output" });
            var service = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
            service.AddEditableFolderProject(project);
            var source = new PackFileContainer("reference.pack");
            AddFile(source, Harness.Walk, CreateAnimation());
            AddFile(source, Harness.Run, CreateAnimation());
            service.AddReferencePack(source);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);
            var model = new AnimationBatchExportViewModel(service, Mock.Of<ISkeletonAnimationLookUpHelper>(), dialogs.Object);
            model.PackfileList.Single().Process.Value = true;
            model.Animations.Single(x => x.Path == Harness.Walk).IsSelected.Value = true;
            model.SelectedOutputFormat.Value = 5;

            var errors = await model.Process();

            Assert.That(errors!.HasData, Is.False);
            Assert.That(File.Exists(Path.Combine(root, Harness.Walk)), Is.True);
            Assert.That(File.Exists(Path.Combine(root, Harness.Run)), Is.False);
            Assert.That(AnimationFile.Create(service.FindFile(Harness.Walk, project)!).Header.Version, Is.EqualTo(5));
            dialogs.Verify(x => x.ShowYesNoBox(
                It.Is<string>(text => text.Contains(root) && text.Contains("保留原资源相对路径")),
                It.IsAny<string>()), Times.Once);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase(ThemeType.DarkTheme, 1080, 720)]
    [TestCase(ThemeType.LightTheme, 1080, 720)]
    [TestCase(ThemeType.HighContrastDark, 760, 520)]
    [TestCase(ThemeType.HighContrastLight, 760, 520)]
    public void View_RendersChineseWithoutWhiteGroupBoxAndVirtualizesAnimations(ThemeType theme, int width, int height)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                ThemesController.SetTheme(theme);
                var harness = new Harness();
                for (var index = 0; index < 5000; index++)
                    AddFile(harness.Source, $@"animations\units\sample_{index:D5}.anim", [1]);
                harness.Model.RefreshSources();
                harness.SelectSource();
                harness.Model.Animations.First().IsSelected.Value = true;
                var view = new AnimationBatchExportView { DataContext = harness.Model, Width = width, Height = height };
                view.Measure(new Size(width, height));
                view.Arrange(new Rect(0, 0, width, height));
                view.UpdateLayout();
                var list = (ListBox)view.FindName("AnimationsList");
                var button = (Button)view.FindName("ExportButton");
                var status = (TextBlock)view.FindName("StatusText");
                var formatSelector = (ComboBox)view.FindName("OutputFormat");
                Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.Not.Null);
                Assert.That(list.ItemContainerGenerator.ContainerFromIndex(4999), Is.Null);
                Assert.That(Descendants<GroupBox>(view), Is.Empty);
                Assert.That(button.Content, Is.EqualTo("导出所选（1）"));
                Assert.That(button.IsEnabled, Is.True);
                Assert.That(button.TranslatePoint(new Point(0, button.ActualHeight), view).Y, Is.LessThanOrEqualTo(height));
                var textBlocks = Descendants<TextBlock>(view).ToArray();
                Assert.That(textBlocks.Any(x => x.Text == "选择来源"), Is.True);
                Assert.That(textBlocks.Any(x => x.Text == "选择动画"), Is.True);
                Assert.That(textBlocks.Any(x => x.Text == "使用说明"), Is.False);
                Assert.That(Descendants<TextBox>(view).Any(x => x.IsReadOnly), Is.False);
                Assert.That(status.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(list.ActualHeight, Is.GreaterThan(height - 180));
                list.SelectedIndex = 0;
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                view.UpdateLayout();
                Assert.That(row.IsSelected, Is.True);
                Assert.That(row.IsMouseOver, Is.False);
                Assert.That(((SolidColorBrush)row.Background).Color.A, Is.Zero);
                Assert.That(row.Focusable, Is.True);
                Assert.That(row.FocusVisualStyle, Is.SameAs(view.FindResource("AeFocus.Keyboard")));
                Assert.That(Descendants<CheckBox>(row).Single().Focusable, Is.True);
                Assert.That(harness.Model.Animations.First().IsSelected.Value, Is.True);
                Assert.That(formatSelector.ToolTip, Is.EqualTo(harness.Model.PossibleOutputFormats.Single(x => x.Version == 7).Description));
                formatSelector.SelectedValue = 8u;
                Assert.That(harness.Model.SelectedOutputFormat.Value, Is.EqualTo(8));
                Assert.That(formatSelector.ToolTip, Is.EqualTo(harness.Model.PossibleOutputFormats.Single(x => x.Version == 8).Description));
                foreach (var option in harness.Model.PossibleOutputFormats)
                {
                    var optionView = new ComboBoxItem { DataContext = option, Style = formatSelector.ItemContainerStyle };
                    Assert.That(optionView.ToolTip, Is.EqualTo(option.Description));
                    Assert.That(option.Description, Does.Not.StartWith("AnimBatchExport."));
                }
                harness.Model.Status.Value = LocalizationManager.Instance.GetFormat("AnimBatchExport.Completed", 1, 0, harness.Output.Name);
                view.UpdateLayout();
                Assert.That(status.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(status.Text, Does.Contain("output.pack"));
                harness.Model.Status.Value = "";
                view.UpdateLayout();
                foreach (var dpi in new[] { 96, 120, 144 })
                {
                    var bitmap = new RenderTargetBitmap(width * dpi / 96, height * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var pixels = new byte[4];
                    bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
                    Assert.That(pixels[3], Is.EqualTo(255));
                    var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        using var stream = File.Create(Path.Combine(output, $"batch-export-{theme}-{dpi}.png"));
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                    }
                }
            }
            finally
            {
                ThemesController.SetTheme(previous);
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static void AddFile(PackFileContainer container, string path, byte[] bytes)
    {
        container.FileList[path] = PackFile.CreateFromBytes(Path.GetFileName(path), bytes);
    }

    private static byte[] CreateAnimation()
    {
        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { Version = 7, SkeletonName = "test", AnimationTotalPlayTimeInSec = 0.05f },
            Bones = [new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = -1 }],
            AnimationParts =
            [
                new AnimationFile.AnimationPart
                {
                    TranslationMappings = [new(0)], RotationMappings = [new(0)],
                    DynamicFrames = [new AnimationFile.Frame
                    {
                        Transforms = [new RmvVector3(2, 3, 4)], Quaternion = [new RmvVector4(0, 0, 0, 1)]
                    }]
                }
            ]
        };
        return AnimationFile.ConvertToBytes(animation);
    }

    private sealed class Harness
    {
        public const string Walk = @"animations\units\walk.anim";
        public const string Run = @"animations\units\run.anim";
        public const string Matrix = @"animations\skeletons\test.bone_inv_trans_mats";
        public PackFileContainer Source { get; } = new("reference.pack");
        public PackFileContainer Output { get; } = new("output.pack");
        public List<PackFileContainer> Containers { get; }
        public List<PackFileWrite> Writes { get; } = [];
        public Mock<IPackFileService> Service { get; } = new();
        public Mock<IStandardDialogs> Dialogs { get; } = new();
        public AnimationBatchExportViewModel Model { get; }

        public Harness()
        {
            AddFile(Source, Walk, CreateAnimation());
            AddFile(Source, Run, CreateAnimation());
            AddFile(Source, Matrix, [1, 2]);
            AddFile(Source, @"animations\skeletons\unrelated.bone_inv_trans_mats", [3, 4]);
            Containers = [Output, Source];
            Service.Setup(x => x.GetEditablePack()).Returns(Output);
            Service.Setup(x => x.GetAllPackfileContainers()).Returns(Containers);
            Service.Setup(x => x.FindFile(It.IsAny<string>(), It.IsAny<PackFileContainer>()))
                .Returns((string path, PackFileContainer container) => container.FileList.GetValueOrDefault(path));
            Service.Setup(x => x.ApplyFileWritesAsync(It.IsAny<PackFileContainer>(),
                    It.IsAny<IReadOnlyCollection<PackFileWrite>>(), true, It.IsAny<CancellationToken>()))
                .Callback((PackFileContainer _, IReadOnlyCollection<PackFileWrite> writes, bool _, CancellationToken _) => Writes.AddRange(writes))
                .ReturnsAsync(Array.Empty<PackFile>());
            Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);
            Model = new AnimationBatchExportViewModel(Service.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(), Dialogs.Object);
        }

        public void SelectSource() => Model.PackfileList.Single().Process.Value = true;
    }
}
