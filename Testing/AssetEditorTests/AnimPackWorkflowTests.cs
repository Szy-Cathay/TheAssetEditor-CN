using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Services.Settings;
using CommonControls.Editors.AnimationPack;
using Editors.AnimationFragmentEditor.CampaignAnimBin;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Services;
using Moq;
using Shared.ByteParsing;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class AnimPackWorkflowTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestInitialize] public void LoadLanguage() => new LocalizationManager().LoadLanguage();

    [TestMethod]
    public void StandaloneCampaign_SaveAppliesChangesWithoutWrappingAnAnimPack()
    {
        var model = Campaign();
        var path = "animations/campaign/database/bin/sample.bin";
        var file = PackFile.CreateFromBytes(path, CampaignAnimationBinLoader.Write(model, "sample"));
        var editor = Create(file, path, out var saved);
        Assert.IsTrue(editor.IsStandaloneCampaign);
        Assert.IsNotNull(editor.CampaignEditorVM);
        Assert.IsFalse(editor.CreateAnimationSetCommand.CanExecute(null));
        editor.CampaignEditorVM.SkeletonName = "griffon";
        Assert.IsTrue(editor.CampaignEditorVM.IsStandaloneFile);
        Assert.AreEqual("保存战役文件", editor.CampaignEditorVM.SaveButtonText);
        editor.CampaignEditorVM.SaveCommand!.Execute(null);
        var reloaded = CampaignAnimationBinLoader.Load(new ByteChunk(saved()));
        Assert.AreEqual("griffon", reloaded.SkeletonName);
        Assert.IsFalse(editor.HasUnsavedChanges);
        Assert.IsFalse(editor.AnimationPackItems.SelectedItem!.IsChanged.Value);
    }

    [TestMethod]
    public void RenameAndDuplicateName_PreserveInternalNameAndMountedReferences()
    {
        var primary = Battle();
        var rider = Battle(); rider.FileName = "animations/database/battle/bin/rider.bin"; rider.Name = "rider"; rider.MountBin = "sample"; rider.Unknown = "sample";
        var database = new AnimationPackFileDatabase("sample.animpack"); database.AddFile(primary); database.AddFile(rider);
        var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
        var selected = editor.AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().First();
        Assert.IsFalse(editor.RenameAnimationSet(selected, "rider.bin"));
        Assert.IsFalse(editor.RenameAnimationSet(selected, "../bad.bin"));
        Assert.IsTrue(editor.RenameAnimationSet(selected, "renamed.bin"));
        Assert.AreEqual("renamed", selected.Name);
        var mounted = editor.AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Last();
        Assert.AreEqual("renamed", mounted.MountBin);
        Assert.AreEqual("renamed", mounted.Unknown);
    }

    [DataTestMethod]
    [DataRow(130d)]
    [DataRow(270d)]
    public void PreviewMetadata_NarrowParametersRemainAccessible(double width)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            Application.Current.Resources["BoolToCollapsedConverter"] = new Shared.Ui.Common.ValueConverters.BoolToVisibilityConverter { TrueValue = Visibility.Visible, FalseValue = Visibility.Collapsed };
            Application.Current.Resources["InvBoolToHiddenConverter"] = new Shared.Ui.Common.ValueConverters.BoolToVisibilityConverter { TrueValue = Visibility.Hidden, FalseValue = Visibility.Visible };
            Application.Current.Resources["InvBoolConverter"] = new Shared.Ui.Common.ValueConverters.InverseBooleanConverter();
            var entry = new Editors.AnimationMeta.Presentation.MetaDataEntry(
                new Shared.GameFormats.AnimationMeta.Definitions.FirePos_v10 { Name = "FIRE", Version = 10, StartTime = 1, EndTime = 2 },
                "", Mock.Of<IEventHub>(), true);
            var view = new Editors.AnimationMeta.Presentation.View.MetaDataAttributeView { DataContext = new { SelectedTag = entry } };
            var window = new Window { Content = view, Width = width, Height = 600, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var input = Descendants<TextBox>(view).Single(t => t.DataContext is Editors.AnimationMeta.Presentation.AttributeViewModel attribute && attribute.PropertyName == "StartTime");
                var scroll = Descendants<ScrollViewer>(view).Single(s => s.TemplatedParent is ListView);
                Assert.IsTrue(input.ActualWidth >= 80, "预览参数必须保留可用的输入宽度。");
                Assert.IsTrue(scroll.ScrollableWidth > 0, "窄参数区必须允许滚动到被遮挡的字段。");
                scroll.ScrollToRightEnd();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Assert.IsTrue(input.TranslatePoint(new Point(input.ActualWidth, 0), scroll).X <= scroll.ViewportWidth + 2, "滚动后必须能看见完整输入框。");
            }
            finally { window.Close(); }
        });
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProductionViews_RenderThemesWidthsAndScales(bool campaign)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
                foreach (var width in new[] { 820, 1280 })
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    ThemesController.SetTheme(theme);
                    var database = new AnimationPackFileDatabase("sample.animpack");
                    database.AddFile(campaign ? new CampaignAnimationPackFile("animations/campaign/database/bin/sample.bin", CampaignAnimationBinLoader.Write(Campaign(), "sample")) : Battle());
                    var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
                    editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.Single();
                    if (campaign) editor.CampaignEditorVM!.SelectedRow = editor.CampaignEditorVM.Rows.First();
                    else editor.TableEditorVM!.SelectedRow = editor.TableEditorVM.Rows.First();
                    var view = new AnimationPackView { DataContext = editor, LayoutTransform = new ScaleTransform(scale, scale) };
                    var canvas = new Border { Child = view, Background = (Brush)Application.Current.FindResource("AeBrush.Canvas") };
                    var window = new Window { Content = canvas, Width = width, Height = 780, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                    try
                    {
                        window.Show(); window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                        Assert.IsTrue(campaign ? editor.ShowCampaignTable : editor.ShowBattleTable);
                        var grid = Descendants<DataGrid>(view).Single(g => g.IsVisible);
                        Assert.AreEqual(1, grid.Items.Count);
                        Assert.AreEqual(4, grid.Columns.Count);
                        Assert.IsTrue(grid.ActualHeight > 60, "动画表必须有足够高度进行编辑。");
                        var directory = Path.Combine(TestContext.TestResultsDirectory!, "animpack-ui"); Directory.CreateDirectory(directory);
                        var filename = Path.Combine(directory, $"{(campaign ? "campaign" : "battle")}-{theme}-{width}-{scale}.png");
                        var image = new RenderTargetBitmap((int)Math.Ceiling(canvas.ActualWidth), (int)Math.Ceiling(canvas.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(canvas);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(filename); encoder.Save(stream);
                        TestContext.AddResultFile(filename);
                    }
                    finally { window.Close(); }
                }
            }
            finally { ThemesController.SetTheme(previous); }
        });
    }

    [TestMethod]
    public void Preview_UsesSelectedFragmentSkeletonAndEmbeddedMetadata()
    {
        const string animationPath = "animations/battle/stand.anim";
        const string metaPath = "animations/battle/stand.anm.meta";
        var animation = new AnimationFile
        {
            Header = new() { Version = 5, SkeletonName = "OtherSkeleton" },
            Bones = [], AnimationParts = [new()],
        };
        var fragment = new AnimationFragmentFile("sample.frg", null!, GameTypeEnum.Warhammer3)
        {
            Skeletons = new Shared.GameFormats.DB.StringArrayTable("RootSkeleton", "OtherSkeleton"),
            Fragments = [new() { Slot = DefaultAnimationSlotTypeHelper.GetFromId(1), Skeleton = "OtherSkeleton",
                AnimationFile = animationPath, MetaDataFile = metaPath }],
        };
        var database = new AnimationPackFileDatabase("sample.animpack");
        database.AddFile(fragment);
        database.AddFile(new UnknownAnimFile(animationPath, AnimationFile.ConvertToBytes(animation)));
        database.AddFile(new UnknownAnimFile("animations/skeletons/OtherSkeleton.anim", AnimationFile.ConvertToBytes(animation)));
        database.AddFile(new UnknownAnimFile(metaPath, new byte[] { 1, 2, 3 }));
        var file = PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database));
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(p => p.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>())).Returns("sample.animpack");
        pfs.Setup(p => p.GetAllPackfileContainers()).Returns([]);
        var viewer = new Mock<IEditorInterface>();
        var preview = viewer.As<IAnimationPreviewEditor>();
        var creator = new Mock<IEditorCreator>();
        creator.Setup(c => c.Create(EditorEnums.SuperView_Editor, null)).Returns(viewer.Object);
        var factory = new Mock<IUiCommandFactory>();
        factory.Setup(f => f.Create<OpenEditorCommand>(It.IsAny<Action<OpenEditorCommand>?>()))
            .Returns(new OpenEditorCommand(creator.Object, pfs.Object));
        var editor = new AnimPackViewModel(factory.Object, pfs.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3), Mock.Of<IFileSaveService>(),
            new MetaDataFileParser(Mock.Of<IMetaDataDatabase>()), Mock.Of<IStandardDialogs>());
        editor.LoadFile(file);
        editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.OfType<AnimationFragmentFile>().Single();
        editor.TableEditorVM!.SelectedRow = editor.TableEditorVM.Rows.Single();

        editor.TableEditorVM.PreviewAnimationCommand.Execute(null);

        preview.Verify(p => p.PreviewAnimation(It.IsAny<AnimationFile>(),
            It.Is<AnimationFile>(s => s.Header.SkeletonName == "OtherSkeleton"), animationPath,
            It.Is<PackFile>(m => m.DataSource.ReadData().SequenceEqual(new byte[] { 1, 2, 3 })), null), Times.Once);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VisibleView_SelectingAFileDoesNotReenterInputCommit(bool campaign)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var database = new AnimationPackFileDatabase("sample.animpack");
            database.AddFile(Battle());
            database.AddFile(new CampaignAnimationPackFile("animations/campaign/database/bin/sample.bin", CampaignAnimationBinLoader.Write(Campaign(), "sample")));
            var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
            var view = new AnimationPackView { DataContext = editor };
            var commit = editor.CommitPendingEdits!;
            int depth = 0, maximumDepth = 0, commits = 0;
            editor.CommitPendingEdits = () =>
            {
                maximumDepth = Math.Max(maximumDepth, ++depth);
                try { return ++commits < 8 && commit(); }
                finally { depth--; }
            };
            var window = new Window { Content = view, Width = 1280, Height = 780, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                var list = Descendants<ListView>(view).Single();
                var selected = editor.AnimationPackItems.PossibleValues.Single(f => campaign ? f is CampaignAnimationPackFile : f is AnimationBinWh3);
                list.SelectedItem = selected;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                Assert.AreEqual(1, maximumDepth, "选择文件时不能通过刷新文件列表再次提交输入。");
                Assert.AreSame(selected, editor.AnimationPackItems.SelectedItem);
                Assert.IsTrue(campaign ? editor.ShowCampaignTable : editor.ShowBattleTable);
                Assert.IsFalse(editor.HasUnsavedChanges);
            }
            finally { window.Close(); }
        });
    }

    [DataTestMethod]
    [DataRow("<Bin")]
    [DataRow("<Bin>\r\n")]
    [DataRow("")]
    [DataRow("<Bin bogus=\"value\" />")]
    [DataRow("<Bin><Unsupported /></Bin>")]
    public void VisibleView_InvalidXmlCannotSave(string xml)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var original = new AnimationBin("classic.bin");
            var originalBytes = original.ToByteArray();
            var database = new AnimationPackFileDatabase("sample.animpack");
            database.AddFile(original);
            var dialogs = new Mock<IStandardDialogs>();
            var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out var saved, dialogs.Object);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.Single();
            editor.IsTableView = false;
            var view = new AnimationPackView { DataContext = editor };
            var window = new Window { Content = view, Width = 1280, Height = 780, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                var input = Descendants<ICSharpCode.AvalonEdit.TextEditor>(view).Single();
                input.Text = xml;
                Assert.IsFalse(editor.Save(), "错误 XML 必须阻止写入。");
                Assert.AreEqual(xml, editor.SelectedItemViewModel.Text, "校验失败必须保留用户输入。");
                Assert.IsTrue(editor.HasUnsavedChanges);
                CollectionAssert.AreEqual(originalBytes, editor.AnimationPackItems.SelectedItem!.ToByteArray());
                Assert.ThrowsException<InvalidOperationException>(() => saved());
                dialogs.Verify(d => d.ShowDialogBox(It.Is<string>(message => message.Contains("XML") && !message.Contains("Unsuported")), It.IsAny<string>()), Times.Once);
            }
            finally { window.Close(); }
        });
    }

    [DataTestMethod]
    [DataRow("<Bin", false, false)]
    [DataRow("<Bin", true, false)]
    [DataRow("<Bin />\r\n", true, true)]
    public void VisibleView_SwitchingFilesKeepsSelectionAlignedWithTheEditor(string xml, bool apply, bool switches)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var original = new AnimationBin("classic.bin");
            var originalBytes = original.ToByteArray();
            var database = new AnimationPackFileDatabase("sample.animpack");
            database.AddFile(original); database.AddFile(Battle());
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(d => d.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    return apply ? ShowMessageBoxResult.OK : ShowMessageBoxResult.Cancel;
                });
            dialogs.Setup(d => d.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle));
            var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out var saved, dialogs.Object);
            var current = editor.AnimationPackItems.PossibleValues.OfType<AnimationBin>().Single();
            var target = editor.AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Single();
            editor.AnimationPackItems.SelectedItem = current;
            editor.IsTableView = false;
            var activeEditor = editor.SelectedItemViewModel;
            var view = new AnimationPackView { DataContext = editor };
            var window = new Window { Content = view, Width = 960, Height = 600, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                Descendants<ICSharpCode.AvalonEdit.TextEditor>(view).Single().Text = xml;
                var scroll = Descendants<ScrollViewer>(view).Single(s => Grid.GetColumn(s) == 2 && Grid.GetRow(s) == 1);
                scroll.ScrollToEnd();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var originalOffset = scroll.VerticalOffset;
                Assert.IsTrue(originalOffset > 0);
                var list = Descendants<ListView>(view).Single();
                list.SelectedIndex = list.Items.IndexOf(target);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                Assert.AreSame(switches ? target : current, editor.AnimationPackItems.SelectedItem);
                Assert.AreEqual(switches ? 0d : originalOffset, scroll.VerticalOffset, "只有成功切换文件才重置滚动位置。");
                Assert.AreSame(editor.AnimationPackItems.SelectedItem, list.SelectedItem, "文件列表高亮必须与实际编辑内容一致。");
                CollectionAssert.AreEqual(originalBytes, current.ToByteArray());
                Assert.ThrowsException<InvalidOperationException>(() => saved());
                if (!switches)
                {
                    Assert.AreSame(activeEditor, editor.SelectedItemViewModel);
                    Assert.AreEqual(xml, activeEditor.Text, "取消切换或校验失败必须保留输入。");
                    Assert.IsTrue(editor.HasUnsavedChanges);
                }
                dialogs.Verify(d => d.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
                dialogs.Verify(d => d.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), apply && !switches ? Times.Once() : Times.Never());
            }
            finally { window.Close(); }
        });
    }

    [DataTestMethod]
    [DataRow(2, ThemeType.DarkTheme)]
    [DataRow(2, ThemeType.LightTheme)]
    [DataRow(2, ThemeType.HighContrastDark)]
    [DataRow(2, ThemeType.HighContrastLight)]
    [DataRow(3, ThemeType.DarkTheme)]
    [DataRow(3, ThemeType.LightTheme)]
    [DataRow(3, ThemeType.HighContrastDark)]
    [DataRow(3, ThemeType.HighContrastLight)]
    public void VisibleView_CampaignFieldTypesDoNotThrowOrChangeDataWhenSwitchingCategories(int version, ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var model = Campaign();
            model.Version = version;
            model.Status[0].Poses = [new() { Animation = "", AnimationMeta = "", SoundMeta = "", BlendTime = 0.4f, Weight = 1, PoseId = 7 }];
            model.Status[0].Docks = [new() { Animation = "", AnimationMeta = "", SoundMeta = "", BlendTime = 0.5f, Weight = 1, Dock = "weapon_1" }];
            model.Status[1].Transitions = [new() { Animation = "", AnimationMeta = "", SoundMeta = "", Type = "global", BlendTime = 0.6f, TransitionTo = "status_stance_march" }];
            model.Status[1].Action = [new() { Animation = "", Meta = "", SoundMeta = "", Type = "global", BlendTime = 0.7f, ActionType = "battle_draw", ActionId = 8, Unknown = true, HasExtraString = true, ExtraString = "custom_value" }];
            var editor = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<IStandardDialogs>());
            editor.LoadFromBinary(CampaignAnimationBinLoader.Write(model, "sample"), "sample.bin");
            foreach (var state in editor.States.ToArray())
            {
                editor.SelectedState = state;
                foreach (var category in editor.Categories.ToArray())
                {
                    editor.SelectedCategory = category;
                    if (editor.Rows.Count == 0) editor.AddEntryCommand.Execute(null);
                }
            }
            var prepared = editor.SaveToBinary("sample.bin", out var error);
            Assert.IsNull(error);
            Assert.IsNotNull(prepared);
            editor.LoadFromBinary(prepared, "sample.bin");
            var original = editor.BuildXmlString();
            var previousTheme = ThemesController.CurrentTheme;
            ThemesController.SetTheme(theme);
            var view = new CampaignTableEditorView { DataContext = editor };
            var window = new Window { Content = view, Width = 1280, Height = 780, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            var failures = new List<Exception>();
            var threadId = Environment.CurrentManagedThreadId;
            void OnFirstChance(object? sender, FirstChanceExceptionEventArgs args)
            {
                if (Environment.CurrentManagedThreadId == threadId && args.Exception is InvalidCastException
                    && args.Exception.StackTrace?.Contains(nameof(CampaignFieldViewModel), StringComparison.Ordinal) == true)
                    failures.Add(args.Exception);
            }
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            try
            {
                window.Show();
                foreach (var state in editor.States.ToArray())
                {
                    editor.SelectedState = state;
                    foreach (var category in editor.Categories.ToArray())
                    {
                        editor.SelectedCategory = category;
                        foreach (var row in editor.Rows)
                        {
                            editor.SelectedRow = row;
                            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                            window.UpdateLayout();
                            Assert.AreEqual(0, failures.Count, failures.FirstOrDefault()?.ToString());
                            foreach (var field in row.Fields)
                            {
                                var textInputs = Descendants<TextBox>(view).Where(c => ReferenceEquals(c.DataContext, field) && c.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == nameof(CampaignFieldViewModel.Value)).ToArray();
                                var checkInputs = Descendants<CheckBox>(view).Where(c => ReferenceEquals(c.DataContext, field) && c.GetBindingExpression(CheckBox.IsCheckedProperty)?.ParentBinding.Path?.Path == nameof(CampaignFieldViewModel.BooleanValue)).ToArray();
                                var stateInputs = Descendants<ComboBox>(view).Where(c => ReferenceEquals(c.DataContext, field) && c.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path?.Path == nameof(CampaignFieldViewModel.Value)).ToArray();
                                Assert.AreEqual(field.IsText ? 1 : 0, textInputs.Length, field.Label);
                                Assert.AreEqual(field.IsBoolean ? 1 : 0, checkInputs.Length, field.Label);
                                Assert.AreEqual(field.IsStateReference ? 1 : 0, stateInputs.Length, field.Label);
                            }
                        }
                    }
                }
                Assert.AreEqual(original, editor.BuildXmlString(), "仅切换参数类别不能改写数据。");
                Assert.IsFalse(editor.IsDirty);
            }
            finally
            {
                window.Close();
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    [TestMethod]
    public void CampaignBooleanFieldIgnoresOtherTypesAndRecordsOnlyRealChanges()
    {
        var entry = new CampaignAnimationBin.ActionEntry { BlendTime = 0.375f, ActionId = 7, ActionType = "battle_draw", Unknown = true };
        var snapshots = 0;
        var changes = 0;
        foreach (var name in new[] { nameof(entry.BlendTime), nameof(entry.ActionId), nameof(entry.ActionType) })
        {
            var field = new CampaignFieldViewModel(entry, entry.GetType().GetProperty(name)!, () => snapshots++, () => changes++);
            var original = field.Value;
            Assert.IsFalse(field.BooleanValue);
            field.BooleanValue = true;
            Assert.AreEqual(original, field.Value);
        }
        Assert.AreEqual(0, snapshots);
        Assert.AreEqual(0, changes);
        Assert.AreEqual(0.375f, entry.BlendTime);
        Assert.AreEqual(7, entry.ActionId);
        Assert.AreEqual("battle_draw", entry.ActionType);
        var toggle = new CampaignFieldViewModel(entry, entry.GetType().GetProperty(nameof(entry.Unknown))!, () => snapshots++, () => changes++);
        toggle.BooleanValue = true;
        Assert.AreEqual(0, snapshots);
        toggle.BooleanValue = false;
        Assert.IsFalse(entry.Unknown);
        Assert.AreEqual("False", toggle.Value);
        Assert.AreEqual(1, snapshots);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void VisibleView_CampaignTypedEditsSupportUndoRedoAndBinaryReload()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var model = Campaign();
            model.Status[1].Action = [new() { Animation = "", Meta = "", SoundMeta = "", Type = "global", BlendTime = 0.7f, ActionType = "battle_draw", ActionId = 8, Unknown = true }];
            var editor = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<IStandardDialogs>());
            editor.LoadFromBinary(CampaignAnimationBinLoader.Write(model, "sample"), "sample.bin");
            editor.SelectedCategory = editor.Categories.Single(c => c.Name == "Action");
            editor.SelectedRow = editor.Rows.Single();
            var original = editor.BuildXmlString();
            var view = new CampaignTableEditorView { DataContext = editor };
            var window = new Window { Content = view, Width = 1280, Height = 780, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                foreach (var (name, value) in new[] { ("BlendTime", "0.625"), ("ActionId", "29"), ("ActionType", "custom_action") })
                {
                    var field = editor.SelectedRow.Fields.Single(f => f.Label == LocalizationManager.Instance.Get($"AnimPack.Campaign.Field.{name}"));
                    var input = Descendants<TextBox>(view).Single(c => ReferenceEquals(c.DataContext, field) && c.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == nameof(CampaignFieldViewModel.Value));
                    input.Text = value;
                    input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                }
                var toggle = Descendants<CheckBox>(view).Single(c => c.GetBindingExpression(CheckBox.IsCheckedProperty)?.ParentBinding.Path?.Path == nameof(CampaignFieldViewModel.BooleanValue));
                Assert.IsTrue(toggle.IsChecked);
                toggle.IsChecked = false;
                var changed = editor.BuildXmlString();
                var entry = (CampaignAnimationBin.ActionEntry)editor.SelectedRow.Entry;
                Assert.AreEqual(0.625f, entry.BlendTime);
                Assert.AreEqual(29, entry.ActionId);
                Assert.AreEqual("custom_action", entry.ActionType);
                Assert.IsFalse(entry.Unknown);
                for (var i = 0; i < 4; i++) editor.UndoCommand.Execute(null);
                Assert.AreEqual(original, editor.BuildXmlString());
                Assert.IsFalse(editor.IsDirty);
                for (var i = 0; i < 4; i++) editor.RedoCommand.Execute(null);
                editor.SelectedRow = editor.Rows.Single();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(changed, editor.BuildXmlString());
                toggle = Descendants<CheckBox>(view).Single(c => c.GetBindingExpression(CheckBox.IsCheckedProperty)?.ParentBinding.Path?.Path == nameof(CampaignFieldViewModel.BooleanValue));
                Assert.IsFalse(toggle.IsChecked);
                var bytes = editor.SaveToBinary("sample.bin", out var error);
                Assert.IsNull(error);
                Assert.IsNotNull(bytes);
                var reloaded = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<IStandardDialogs>());
                reloaded.LoadFromBinary(bytes, "sample.bin");
                Assert.AreEqual(changed, reloaded.BuildXmlString(), "四种参数的修改必须完整保存并重新读出。");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void VisibleView_CampaignTargetSelectionPreservesCustomNamesAndSupportsUndo()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var model = Campaign();
            model.Status[1].Transitions = [new() { Animation = "", AnimationMeta = "", SoundMeta = "", Type = "global", TransitionTo = "custom_target" }];
            var editor = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<IStandardDialogs>());
            editor.LoadFromBinary(CampaignAnimationBinLoader.Write(model, "sample"), "sample.bin");
            editor.SelectedCategory = editor.Categories.Single(c => c.Name == "Transitions");
            editor.SelectedRow = editor.Rows.Single();
            var view = new CampaignTableEditorView { DataContext = editor };
            var window = new Window { Content = view, Width = 960, Height = 600, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                var field = editor.SelectedRow.Fields.Single(f => f.IsStateReference);
                var input = Descendants<ComboBox>(view).Single(c => ReferenceEquals(c.ItemsSource, field.StateOptions));
                Assert.AreEqual("custom_target", input.SelectedItem, "旧的自定义目标必须显示并保留。");
                CollectionAssert.Contains(field.StateOptions.ToArray(), "status_normal");
                input.SelectedItem = "status_stance_march";
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual("status_stance_march", ((CampaignAnimationBin.TransitionEntry)editor.SelectedRow.Entry).TransitionTo);
                editor.UndoCommand.Execute(null);
                editor.SelectedRow = editor.Rows.Single();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.AreEqual("custom_target", editor.SelectedRow.Fields.Single(f => f.IsStateReference).Value);
                Assert.IsFalse(editor.IsDirty);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CampaignStateRenameRefreshesCachedTransitionTargets()
    {
        var model = Campaign();
        model.Status[1].Transitions = [new() { Animation = "", AnimationMeta = "", SoundMeta = "", Type = "global", TransitionTo = "status_stance_march" }];
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(d => d.ShowTextInputDialog(It.IsAny<string>(), It.IsAny<string>())).Returns(new TextInputDialogResult(true, "status_custom"));
        var editor = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), dialogs.Object);
        editor.LoadFromBinary(CampaignAnimationBinLoader.Write(model, "sample"), "sample.bin");
        editor.SelectedCategory = editor.Categories.Single(c => c.Name == "Transitions");
        editor.SelectedRow = editor.Rows.Single();
        editor.SelectedState = editor.States.Single(s => s.Name == "status_stance_march");
        editor.RenameStateCommand.Execute(null);
        editor.SelectedState = editor.States.Single(s => s.Name == "status_normal");
        editor.SelectedCategory = editor.Categories.Single(c => c.Name == "Transitions");
        var field = editor.Rows.Single().Fields.Single(f => f.IsStateReference);
        Assert.AreEqual("status_custom", field.Value);
        CollectionAssert.Contains(field.StateOptions.ToArray(), "status_custom");
        CollectionAssert.DoesNotContain(field.StateOptions.ToArray(), "status_stance_march");
        Assert.AreEqual("status_custom", ((CampaignAnimationBin.TransitionEntry)editor.Rows.Single().Entry).TransitionTo);
    }

    [TestMethod]
    public void BattleResourceActionsRequireTheCorrespondingReferences()
    {
        var database = new AnimationPackFileDatabase("sample.animpack"); database.AddFile(Battle());
        var pack = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
        pack.AnimationPackItems.SelectedItem = pack.AnimationPackItems.PossibleValues.Single();
        var editor = pack.TableEditorVM!;
        editor.AddEntryCommand.Execute(null);
        var row = editor.SelectedRow!;
        Assert.IsFalse(editor.PreviewAnimationCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenAnimationCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenMetaCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenSoundCommand.CanExecute(null));
        var notifications = 0;
        editor.OpenAnimationCommand.CanExecuteChanged += (_, _) => notifications++;
        row.AnimationFile = "idle.anim"; row.MetaFile = "idle.anm.meta"; row.SoundFile = "idle.snd.meta";
        Assert.IsTrue(notifications > 0, "输入资源路径后，界面按钮必须立即刷新。");
        Assert.IsTrue(editor.PreviewAnimationCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenAnimationCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenMetaCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenSoundCommand.CanExecute(null));
        editor.SkeletonName = "";
        Assert.IsFalse(editor.PreviewAnimationCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenAnimationCommand.CanExecute(null));
        editor.MountBin = ""; editor.UnmountBin = "";
        Assert.IsFalse(editor.OpenMountCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenUnmountCommand.CanExecute(null));
    }

    [TestMethod]
    public void CampaignResourceActionsRequireTheCorrespondingReferences()
    {
        var editor = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<IStandardDialogs>());
        editor.LoadFromBinary(CampaignAnimationBinLoader.Write(Campaign(), "sample"), "sample.bin");
        editor.AddEntryCommand.Execute(null);
        var row = editor.SelectedRow!;
        Assert.IsFalse(editor.PreviewAnimationCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenAnimationCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenMetaCommand.CanExecute(null));
        Assert.IsFalse(editor.OpenSoundCommand.CanExecute(null));
        row.Animation = "idle.anim"; row.Meta = "idle.anm.meta"; row.Sound = "idle.snd.meta";
        Assert.IsTrue(editor.PreviewAnimationCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenAnimationCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenMetaCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenSoundCommand.CanExecute(null));
        editor.SkeletonName = "";
        Assert.IsFalse(editor.PreviewAnimationCommand.CanExecute(null));
        Assert.IsTrue(editor.OpenAnimationCommand.CanExecute(null));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VisibleView_SwitchingFilesReturnsToTheHeader(bool selectInModel)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var database = new AnimationPackFileDatabase("sample.animpack");
            database.AddFile(Battle());
            database.AddFile(new CampaignAnimationPackFile("animations/campaign/database/bin/sample.bin", CampaignAnimationBinLoader.Write(Campaign(), "sample")));
            var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Single();
            var view = new AnimationPackView { DataContext = editor };
            var window = new Window { Content = view, Width = 960, Height = 600, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                var scroll = Descendants<ScrollViewer>(view).Single(s => Grid.GetColumn(s) == 2 && Grid.GetRow(s) == 1);
                scroll.ScrollToEnd();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsTrue(scroll.VerticalOffset > 0, "小窗口必须实际滚动后再切换文件。");
                var target = editor.AnimationPackItems.PossibleValues.OfType<CampaignAnimationPackFile>().Single();
                if (selectInModel) editor.AnimationPackItems.SelectedItem = target;
                else Descendants<ListView>(view).Single().SelectedItem = target;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                Assert.AreSame(target, editor.AnimationPackItems.SelectedItem);
                Assert.AreEqual(0d, scroll.VerticalOffset, "切换文件后必须能立即看见新文件的标题和保存入口。");
            }
            finally { window.Close(); }
        });
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VisibleView_NewEntryIsScrolledIntoView(bool campaign)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var database = new AnimationPackFileDatabase("sample.animpack");
            database.AddFile(campaign ? new CampaignAnimationPackFile("animations/campaign/database/bin/sample.bin", CampaignAnimationBinLoader.Write(Campaign(), "sample")) : Battle());
            var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.Single();
            var add = campaign ? editor.CampaignEditorVM!.AddEntryCommand : editor.TableEditorVM.AddEntryCommand;
            for (var i = 0; i < 80; i++) add.Execute(null);
            if (campaign) editor.CampaignEditorVM!.SelectedRow = editor.CampaignEditorVM.Rows.First();
            else editor.TableEditorVM.SelectedRow = editor.TableEditorVM.Rows.First();
            var view = new AnimationPackView { DataContext = editor };
            var window = new Window { Content = view, Width = 1280, Height = 780, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var grid = Descendants<DataGrid>(view).Single(g => g.IsVisible);
                add.Execute(null);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var row = (DataGridRow?)grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem);
                Assert.IsNotNull(row, "新建的动作必须自动进入可见区域。");
                var position = row.TranslatePoint(new Point(), grid);
                Assert.IsTrue(position.Y >= 0 && position.Y < grid.ActualHeight, "新条目不能停留在表格可视范围之外。");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CommittingUnchangedFilters_PreservesFileSelectionAndTemplate()
    {
        var database = new AnimationPackFileDatabase("sample.animpack"); database.AddFile(Battle());
        var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
        var selected = editor.AnimationPackItems.PossibleValues.Single();
        editor.AnimationPackItems.SelectedItem = selected;
        editor.NewTemplate = selected;
        var visibleFiles = editor.AnimationPackItems.Values;

        editor.FileFilterText = editor.FileFilterText;
        editor.UseRegexFilter = editor.UseRegexFilter;
        editor.OnlyEditableFiles = editor.OnlyEditableFiles;
        editor.NewFormat = editor.NewFormat;

        Assert.AreSame(visibleFiles, editor.AnimationPackItems.Values, "提交未变化的搜索条件不能重建列表并触发选择回调。");
        Assert.AreSame(selected, editor.AnimationPackItems.SelectedItem);
        Assert.AreSame(selected, editor.NewTemplate);
    }

    [TestMethod]
    public void SelectionNotificationDuringInputCommit_DoesNotReplaceTheActiveEditor()
    {
        var database = new AnimationPackFileDatabase("sample.animpack"); database.AddFile(Battle());
        var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _);
        var selected = editor.AnimationPackItems.PossibleValues.Single();
        editor.AnimationPackItems.SelectedItem = selected;
        var table = editor.TableEditorVM;
        editor.CommitPendingEdits = () => { editor.AnimationPackItems.SelectedItem = null; return true; };

        Assert.IsTrue(editor.SaveActiveFile());
        Assert.AreSame(selected, editor.AnimationPackItems.SelectedItem);
        Assert.AreSame(table, editor.TableEditorVM);
    }

    [TestMethod]
    public void Save_CommitsTheVisibleInputAndRejectsInvalidNumbers()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var database = new AnimationPackFileDatabase("sample.animpack"); database.AddFile(Battle());
            var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out var saved);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.Single();
            editor.TableEditorVM!.SelectedRow = editor.TableEditorVM.Rows[0];
            var view = new AnimationPackView { DataContext = editor };
            var window = new Window { Content = view, Width = 1280, Height = 780, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                var input = Descendants<TextBox>(view).Single(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "SelectedRow.BlendInTime");
                input.Text = "0.75";
                Assert.IsTrue(editor.Save());
                var pfs = new Mock<IPackFileService>(); pfs.Setup(p => p.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>())).Returns("sample.animpack");
                var result = AnimationPackSerializer.Load(PackFile.CreateFromBytes("sample.animpack", saved()), pfs.Object);
                Assert.AreEqual(0.75f, result.Files.OfType<AnimationBinWh3>().Single().AnimationTableEntries[0].BlendIn);
                input.Text = "invalid";
                Assert.IsFalse(editor.Save());
                Assert.IsTrue(editor.HasUnsavedChanges);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CreateFromFragmentTemplate_PreservesAdditionalSkeletons()
    {
        var source = new AnimationFragmentFile("sample.frg", null!, GameTypeEnum.Warhammer3)
        {
            Skeletons = new Shared.GameFormats.DB.StringArrayTable("RootSkeleton", "OtherSkeleton", "RootSkeleton"),
            Fragments = [new() { Slot = DefaultAnimationSlotTypeHelper.GetFromId(1), Skeleton = "OtherSkeleton", AnimationFile = "Case.anim" }],
        };
        var database = new AnimationPackFileDatabase("sample.animpack"); database.AddFile(source);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(d => d.ShowTextInputDialog(It.IsAny<string>(), It.IsAny<string>())).Returns(new TextInputDialogResult(true, "created.frg"));
        var editor = Create(PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database)), "sample.animpack", out _, dialogs.Object);
        editor.NewFormat = AnimPackViewModel.NewFormats.Single(f => f.Id == "Fragment");
        editor.NewTemplate = editor.AnimationPackItems.PossibleValues.Single();
        editor.NewSkeletonName = "NewRoot";

        editor.CreateAnimationSetCommand.Execute(null);

        var created = (AnimationFragmentFile)editor.AnimationPackItems.SelectedItem!;
        CollectionAssert.AreEqual(new[] { "NewRoot", "OtherSkeleton", "NewRoot" }, created.Skeletons.Values);
        Assert.AreEqual("OtherSkeleton", created.Fragments.Single().Skeleton);
        Assert.AreEqual("Case.anim", created.Fragments.Single().AnimationFile);
        Assert.IsTrue(editor.ShowBattleTable);
    }

    internal static AnimPackViewModel Create(PackFile file, string path, out Func<byte[]> saved, IStandardDialogs? dialogs = null)
    {
        byte[]? bytes = null;
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(p => p.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>())).Returns(path);
        pfs.Setup(p => p.GetAllPackfileContainers()).Returns([]);
        var save = new Mock<IFileSaveService>();
        save.Setup(s => s.Save(path, It.IsAny<byte[]>(), false)).Callback<string,byte[],bool>((_, data, _) => bytes = data).Returns(file);
        var editor = new AnimPackViewModel(Mock.Of<IUiCommandFactory>(), pfs.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3), save.Object, new MetaDataFileParser(Mock.Of<IMetaDataDatabase>()), dialogs ?? Mock.Of<IStandardDialogs>());
        editor.LoadFile(file);
        saved = () => bytes ?? throw new InvalidOperationException("没有写入保存内容。");
        return editor;
    }
    internal static AnimationBinWh3 Battle()
    {
        var bin = new AnimationBinWh3("animations/database/battle/bin/sample.bin") { Name = "sample", SkeletonName = "humanoid01", LocomotionGraph = "animations/locomotion_graphs/entity_locomotion_graph.xml", MountBin = "griffon", Unknown = "griffon_unmount", UnknownValue1 = 257 };
        bin.AnimationTableEntries.Add(new() { AnimationId = 1, BlendIn = 0.3f, SelectionWeight = 1, AnimationRefs = [new() { AnimationFile = "animations/battle/griffon/stand/griffon_stand_01.anim", AnimationMetaFile = "animations/battle/griffon/stand/griffon_stand_01.anm.meta", AnimationSoundMetaFile = "animations/audio/griffon_stand_01.snd.meta" }] });
        return bin;
    }
    internal static CampaignAnimationBin Campaign() => new()
    {
        Version = 3, Reference = "sample", SkeletonName = "humanoid01",
        Status = [new() { Name = "global" }, new() { Name = "status_normal", Idle = [new() { Animation = "animations/campaign/griffon/idle.anim", Type = "global", MetaFile = "animations/campaign/griffon/idle.anm.meta", SoundMeta = "", BlendTime = 0.3f, Weight = 1 }] }, new() { Name = "status_stance_march" }],
    };
    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
