using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommonControls.MathViews;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.Presentation.View;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Inspection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Assert = NUnit.Framework.Assert;
using MetadataPanel = Editors.AnimationMeta.SuperView.EditorView;
using WorkspaceModel = AssetEditorTests.UiAnimationMetadataFamilyGallery.MetadataSuperViewGalleryModel;
using GalleryModel = AssetEditorTests.UiAnimationMetadataFamilyGallery.GalleryModel;

namespace AssetEditorTests;

[NonParallelizable]
public class SuperViewWorkspaceLayoutTests
{
    public static IEnumerable<TestCaseData> LayoutCases =>
        from theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight }
        from dpi in new[] { 96, 120, 144 }
        select new TestCaseData(theme, dpi);

    [TestCaseSource(nameof(LayoutCases))]
    public void FlagWindInputs_AreScopedToSupportedPreviewAndBindWorldXZ(ThemeType theme, int dpi)
    {
        WithTheme(theme, () =>
        {
            var model = new WorkspaceModel(false, true, 1)
            {
                CanPreviewFlagWind = true, SelectedInspectorTabIndex = 2,
            };
            var view = new SuperViewWorkspaceView { DataContext = model };
            Layout(view, 1440d * 96 / dpi, 900d * 96 / dpi);
            var wind = (Grid)view.FindName("FlagWindControls");
            Assert.That(wind.Visibility, Is.EqualTo(Visibility.Visible));
            ScrollToElement((ScrollViewer)view.FindName("PreviewSettingsScroll"), wind, view, 1440d * 96 / dpi, 900d * 96 / dpi);
            AssertInside(wind, view);
            var inputs = Descendants<TextBox>(wind).ToArray();
            Assert.That(inputs, Has.Length.EqualTo(2));
            inputs[0].SetCurrentValue(TextBox.TextProperty, ".12");
            inputs[0].GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            inputs[1].SetCurrentValue(TextBox.TextProperty, "-.07");
            inputs[1].GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.That(model.PreviewWindX, Is.EqualTo(".12"));
            Assert.That(model.PreviewWindZ, Is.EqualTo("-.07"));
            view.DataContext = new WorkspaceModel(false, true, 1) { CanPreviewFlagWind = false };
            Layout(view, 1440d * 96 / dpi, 900d * 96 / dpi);
            Assert.That(wind.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCaseSource(nameof(LayoutCases))]
    public void EffectPreviewControls_BindAllSceneInputsWithoutChangingDocumentsOrPlayback(ThemeType theme, int dpi)
    {
        WithTheme(theme, () =>
        {
            var model = new WorkspaceModel(true, true, 1) { SelectedInspectorTabIndex = 2 };
            var persistentEditor = model.PersistentMetaEditor;
            var animationEditor = model.MetaEditor;
            var documents = new[] { (ParsedMetadataFile)persistentEditor.ParsedFile!, (ParsedMetadataFile)animationEditor.ParsedFile! };
            var sourceLists = documents.Select(document => document.Attributes).ToArray();
            var sources = sourceLists.SelectMany(attributes => attributes).Cast<SplashAttack_v10>().ToArray();
            var originalFields = sources.Select(source => (source.StartTime, source.EndTime, source.StartPosition, source.EndPosition)).ToArray();
            var originalData = sources.Select(source => source.Data).ToArray();
            var originalBytes = sources.Select(source => source.Data?.ToArray()).ToArray();
            var persistentTag = persistentEditor.SelectedTag;
            var animationTag = animationEditor.SelectedTag;
            var player = model.Player;
            var playerFrame = (GalleryModel)player.SelectedAnimationCurrentFrame!;
            var playerTime = (GalleryModel)player.SelectedAnimationCurrentTime!;
            var frame = playerFrame.Value;
            var time = playerTime.Value;
            var timeline = model.MetaDataTimeline;
            var problems = model.MetaDataProblems;
            var view = new SuperViewWorkspaceView { DataContext = model };
            var width = 1440d * 96 / dpi;
            var height = 900d * 96 / dpi;
            Layout(view, width, height);
            Assert.That(Window.GetWindow(view), Is.Null, "This regression must remain an unhosted offscreen visual.");
            var panel = (StackPanel)view.FindName("EffectPreviewControls");
            var settings = (ScrollViewer)view.FindName("PreviewSettingsScroll");
            var terrain = (ComboBox)view.FindName("EffectPreviewTerrain");
            var distance = (CheckBox)view.FindName("EffectPreviewDistanceCulling");
            var filter = (CheckBox)view.FindName("EffectPreviewIdFilter");
            var id = (TextBox)view.FindName("EffectPreviewId");
            var paths = Descendants<TextBox>(panel).Where(input => !ReferenceEquals(input, id)).ToArray();
            Assert.That(paths, Has.Length.EqualTo(2));
            var aura = paths.Single(input => BindingOperations.GetBinding(input, TextBox.TextProperty)!.Path.Path == nameof(model.PreviewAuraResource));
            var weapon = paths.Single(input => BindingOperations.GetBinding(input, TextBox.TextProperty)!.Path.Path == nameof(model.PreviewWeaponResource));
            var buttons = Descendants<Button>(panel).ToDictionary(button => BindingOperations.GetBinding(button, Button.CommandProperty)!.Path.Path);
            Assert.Multiple(() =>
            {
                Assert.That(terrain.ItemsSource, Is.SameAs(model.EffectTerrainChoices));
                Assert.That(terrain.Items.Cast<EffectTerrainChoice>().Select(choice => choice.Value), Is.EqualTo(Enumerable.Range(-1, 18)));
                Assert.That(terrain.Items.Cast<EffectTerrainChoice>().Select(choice => choice.Label),
                    Is.EqualTo(Enumerable.Range(-1, 18).Select(value => LocalizationManager.Instance.Get("SuperView.Effects.Terrain." + value))));
                Assert.That(terrain.SelectedValue, Is.EqualTo(-1));
                Assert.That(aura.IsReadOnly && weapon.IsReadOnly, Is.True);
                Assert.That(BindingOperations.GetBinding(aura, TextBox.TextProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
                Assert.That(BindingOperations.GetBinding(weapon, TextBox.TextProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
                Assert.That(buttons, Has.Count.EqualTo(4));
                Assert.That(buttons[nameof(model.BrowseAuraResourceCommand)].Command, Is.SameAs(model.BrowseAuraResourceCommand));
                Assert.That(buttons[nameof(model.BrowseWeaponResourceCommand)].Command, Is.SameAs(model.BrowseWeaponResourceCommand));
                Assert.That(buttons[nameof(model.ClearAuraResourceCommand)].Command, Is.SameAs(model.ClearAuraResourceCommand));
                Assert.That(buttons[nameof(model.ClearWeaponResourceCommand)].Command, Is.SameAs(model.ClearWeaponResourceCommand));
                Assert.That(distance.IsChecked, Is.False);
                Assert.That(filter.IsChecked, Is.False);
                Assert.That(id.IsEnabled, Is.False);
            });
            foreach (var choice in Enumerable.Range(-1, 18))
            {
                terrain.SetCurrentValue(Selector.SelectedValueProperty, choice);
                terrain.GetBindingExpression(Selector.SelectedValueProperty)!.UpdateSource();
                Assert.That(model.PreviewTerrainType, Is.EqualTo(choice));
            }
            model.PreviewTerrainType = 6;
            buttons[nameof(model.BrowseAuraResourceCommand)].Command!.Execute(null);
            buttons[nameof(model.BrowseWeaponResourceCommand)].Command!.Execute(null);
            Layout(view, width, height);
            Assert.That(terrain.SelectedValue, Is.EqualTo(6));
            Assert.That(model.AuraBrowseRequests, Is.EqualTo(1));
            Assert.That(model.WeaponBrowseRequests, Is.EqualTo(1));
            Assert.That(aura.Text, Is.EqualTo(model.PreviewAuraResource).And.Not.Empty);
            Assert.That(weapon.Text, Is.EqualTo(model.PreviewWeaponResource).And.Not.Empty);
            var originalAura = model.PreviewAuraResource;
            aura.SetCurrentValue(TextBox.TextProperty, "attempted metadata edit");
            Assert.That(model.PreviewAuraResource, Is.EqualTo(originalAura));
            aura.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            distance.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            distance.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            filter.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            filter.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Layout(view, width, height);
            Assert.That(model.PreviewDistanceCulling, Is.True);
            Assert.That(model.PreviewEffectIdFilter, Is.True);
            Assert.That(id.IsEnabled, Is.True);
            Assert.That(Microsoft.Xaml.Behaviors.Interaction.GetBehaviors(id), Is.Empty,
                "Signed native IDs must use integer binding validation without an unsigned-only input filter.");
            foreach (var valid in new[] { int.MinValue, -1, int.MaxValue })
            {
                SetEffectIdText(valid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.That(model.PreviewEffectId, Is.EqualTo(valid));
                Assert.That(Validation.GetHasError(id), Is.False);
            }
            SetEffectIdText("420");
            Assert.That(model.PreviewEffectId, Is.EqualTo(420));
            Assert.That(Validation.GetHasError(id), Is.False);
            foreach (var invalid in new[] { "not-an-id", "2147483648", "-2147483649", "1.5" })
            {
                SetEffectIdText(invalid);
                Assert.That(Validation.GetHasError(id), Is.True);
                Assert.That(model.PreviewEffectId, Is.EqualTo(420), "Conversion errors must not overwrite the last valid ID.");
            }
            SetEffectIdText("17");
            Assert.That(model.PreviewEffectId, Is.EqualTo(17));
            Assert.That(Validation.GetHasError(id), Is.False);

            var presenter = Descendants<ScrollContentPresenter>(settings).First();
            foreach (var control in new FrameworkElement[] { terrain, aura, weapon, distance, filter, id }.Concat(buttons.Values))
            {
                ScrollToElement(settings, control, view, width, height);
                AssertHorizontalInside(control, panel);
                AssertInside(control, presenter);
            }
            Assert.That(settings.ScrollableWidth, Is.EqualTo(0), "The inspector must not hide overflow behind horizontal scrolling.");
            ScrollToElement(settings, panel, view, width, height);
            Snapshot(view, $"superview-effect-preview-{theme}-{dpi}-populated.png", dpi);
            buttons[nameof(model.ClearAuraResourceCommand)].Command!.Execute(null);
            buttons[nameof(model.ClearWeaponResourceCommand)].Command!.Execute(null);
            model.PreviewDistanceCulling = false;
            model.PreviewEffectIdFilter = false;
            Layout(view, width, height);
            Assert.That(aura.Text, Is.Empty);
            Assert.That(weapon.Text, Is.Empty);
            Assert.That(distance.IsChecked, Is.False);
            Assert.That(filter.IsChecked, Is.False);
            Assert.That(id.IsEnabled, Is.False);
            foreach (var category in new[] { 1, 3, 2 })
            {
                ((TabControl)view.FindName("InspectorCategories")).SetCurrentValue(Selector.SelectedIndexProperty, category);
                Layout(view, width, height);
            }
            Assert.Multiple(() =>
            {
                Assert.That(model.PersistentMetaEditor, Is.SameAs(persistentEditor));
                Assert.That(model.MetaEditor, Is.SameAs(animationEditor));
                Assert.That(persistentEditor.ParsedFile, Is.SameAs(documents[0]));
                Assert.That(animationEditor.ParsedFile, Is.SameAs(documents[1]));
                Assert.That(persistentEditor.IsDirty || animationEditor.IsDirty, Is.False);
                Assert.That(persistentEditor.SelectedTag, Is.SameAs(persistentTag));
                Assert.That(animationEditor.SelectedTag, Is.SameAs(animationTag));
                Assert.That(model.Player, Is.SameAs(player));
                Assert.That(model.MetaDataTimeline, Is.SameAs(timeline));
                Assert.That(model.MetaDataProblems, Is.SameAs(problems));
                Assert.That(playerFrame.Value, Is.EqualTo(frame));
                Assert.That(playerTime.Value, Is.EqualTo(time));
                Assert.That(model.SelectedTabControllerIndex, Is.EqualTo(1));
                Assert.That(documents[0].Attributes, Is.SameAs(sourceLists[0]));
                Assert.That(documents[1].Attributes, Is.SameAs(sourceLists[1]));
                Assert.That(documents.SelectMany(document => document.Attributes), Is.EqualTo(sources));
                Assert.That(sources.Select(source => (source.StartTime, source.EndTime, source.StartPosition, source.EndPosition)),
                    Is.EqualTo(originalFields));
                for (var index = 0; index < sources.Length; index++)
                {
                    Assert.That(sources[index].Data, Is.SameAs(originalData[index]));
                    Assert.That(sources[index].Data, Is.EqualTo(originalBytes[index]));
                }
            });

            void SetEffectIdText(string text)
            {
                ScrollToElement(settings, id, view, width, height);
                id.SetCurrentValue(TextBox.TextProperty, text);
                id.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Layout(view, width, height);
            }
        });
    }

    [TestCaseSource(nameof(LayoutCases))]
    public void Workspace_AlignsScenePlaybackAndCategoriesWithoutChangingDocumentState(ThemeType theme, int dpi)
    {
        WithTheme(theme, () =>
        {
            var model = new WorkspaceModel(true, true, 1);
            var persistentTag = model.PersistentMetaEditor.SelectedTag;
            var animationTag = model.MetaEditor.SelectedTag;
            var problems = model.MetaDataProblems;
            var player = model.Player;
            var view = new SuperViewWorkspaceView { DataContext = model };
            var width = 1440d * 96 / dpi;
            var height = 900d * 96 / dpi;
            Layout(view, width, height);
            var preview = (Grid)view.FindName("PreviewArea");
            var viewport = (ContentControl)view.FindName("Viewport");
            var playback = (FrameworkElement)view.FindName("Playback");
            var toolbar = (WrapPanel)view.FindName("SceneToolbar");
            var tabs = (TabControl)view.FindName("InspectorCategories");
            Assert.Multiple(() =>
            {
                Assert.That(tabs.Items.Cast<TabItem>().Select(item => item.Header), Is.EqualTo(new[] { "参考资源", "元数据", "预览设置", "诊断" }));
                Assert.That(playback.DataContext, Is.SameAs(player));
                Assert.That(viewport.ActualWidth, Is.EqualTo(playback.ActualWidth).Within(1));
                Assert.That(toolbar.ActualHeight, Is.LessThanOrEqualTo(28));
                Assert.That(tabs.ActualHeight, Is.EqualTo(view.ActualHeight).Within(1));
                Assert.That(Grid.GetRow(playback), Is.EqualTo(2));
                Assert.That(Descendants<MetaDataTimelineView>(playback).Single().DataContext, Is.SameAs(model.MetaDataTimeline));
            });
            AssertInside(preview, view);
            AssertInside(playback, view);
            AssertInside(tabs, view);
            var playbackExpander = Descendants<Expander>(playback).Single(expander => expander.Name == "AnimationPlayerExpander");
            var playerEnabled = (GalleryModel)model.Player.IsEnabled!;
            playbackExpander.SetCurrentValue(Expander.IsExpandedProperty, false);
            Layout(view, width, height);
            Assert.That(playerEnabled.Value, Is.EqualTo(false));
            playbackExpander.SetCurrentValue(Expander.IsExpandedProperty, true);
            Layout(view, width, height);
            Assert.That(playerEnabled.Value, Is.EqualTo(true));
            Assert.That(Descendants<MetaDataTimelineView>(playback).Single().DataContext, Is.SameAs(model.MetaDataTimeline));

            foreach (var category in new[] { 1, 0, 2, 3, 1 })
            {
                tabs.SetCurrentValue(TabControl.SelectedIndexProperty, category);
                Layout(view, width, height);
                if (category == 0)
                {
                    var resources = (ListView)view.FindName("ReferenceResources");
                    Assert.That(resources.ItemsSource, Is.SameAs(model.SceneObjects));
                    AssertInside(resources, view);
                }
                else if (category == 1)
                {
                    var metadata = (MetadataPanel)view.FindName("MetadataPanel");
                    var save = Descendants<Button>(metadata).Single(button => Equals(button.Content, "保存"));
                    AssertInside(save, metadata);
                    Assert.That(save.Command, Is.SameAs(model.MetaEditor.SaveActionCommand));
                    Assert.That(metadata.DataContext, Is.SameAs(model));
                    Assert.That(Descendants<MetaDataProblemListView>(metadata), Is.Empty);
                    AssertFieldVectorsFitHorizontally(metadata, view, width, height);
                }
                else if (category == 2)
                {
                    var settings = (ScrollViewer)view.FindName("PreviewSettingsScroll");
                    var toggles = Descendants<CheckBox>(settings)
                        .Where(toggle => BindingOperations.GetBinding(toggle, ToggleButton.IsCheckedProperty)?.Path.Path.StartsWith("Show", StringComparison.Ordinal) == true).ToArray();
                    Assert.That(toggles, Has.Length.EqualTo(4));
                    Assert.That(toggles.All(toggle => toggle.IsChecked == true), Is.True);
                    AssertInside(settings, view);
                }
                else
                {
                    var diagnostics = (MetaDataProblemListView)view.FindName("DiagnosticsPanel");
                    var list = (ListBox)diagnostics.FindName("ProblemList");
                    Assert.That(diagnostics.DataContext, Is.SameAs(problems));
                    Assert.That(list.ItemsSource, Is.SameAs(problems.Problems));
                    Assert.That(list.SelectedItem, Is.SameAs(problems.SelectedProblem));
                    Assert.That(System.Windows.Automation.AutomationProperties.GetName(list), Is.EqualTo("SuperView META 问题列表"));
                    var labels = Descendants<TextBlock>(diagnostics).Select(text => text.Text).ToArray();
                    Assert.That(labels, Does.Contain("错误"));
                    Assert.That(labels, Does.Contain("警告"));
                    Assert.That(list.ActualHeight, Is.GreaterThan(176));
                    Assert.That(VirtualizingPanel.GetIsVirtualizing(list), Is.True);
                    AssertInside(diagnostics, view);
                    AssertInside(list, diagnostics);
                }
                Snapshot(view, $"superview-workspace-{theme}-{dpi}-category-{category}.png", dpi);
            }
            Assert.Multiple(() =>
            {
                Assert.That(model.SelectedInspectorTabIndex, Is.EqualTo(1));
                Assert.That(model.SelectedTabControllerIndex, Is.EqualTo(1));
                Assert.That(model.PersistentMetaEditor.SelectedTag, Is.SameAs(persistentTag));
                Assert.That(model.MetaEditor.SelectedTag, Is.SameAs(animationTag));
                Assert.That(model.MetaDataProblems, Is.SameAs(problems));
                Assert.That(model.Player, Is.SameAs(player));
                Assert.That(playback.DataContext, Is.SameAs(player));
            });

            view.DataContext = new WorkspaceModel(false, false, 0) { SelectedInspectorTabIndex = 3 };
            Layout(view, width, height);
            var emptyDiagnostics = (MetaDataProblemListView)view.FindName("DiagnosticsPanel");
            Assert.That(((ListBox)emptyDiagnostics.FindName("ProblemList")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Descendants<TextBlock>(emptyDiagnostics).Select(text => text.Text), Does.Contain("未发现问题"));
            AssertInside(emptyDiagnostics, view);
            Snapshot(view, $"superview-workspace-{theme}-{dpi}-diagnostics-empty.png", dpi);
        });
    }

    [Test]
    public void Diagnostics_ReactivatesAlreadySelectedProblemAfterReturningFromMetadata()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var problem = new WorkspaceModel(true, true, 1).MetaDataProblems.Problems.First();
            var navigations = new List<MetaDataInspectionProblem>();
            var model = new MetaDataProblemListViewModel(navigations.Add);
            model.Problems.Add(problem);
            model.UpdateSelection(problem.Problem);
            var view = new MetaDataProblemListView { DataContext = model };
            Layout(view, 480, 600);
            var list = (ListBox)view.FindName("ProblemList");
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(problem);
            Assert.That(row.IsSelected, Is.True);
            Assert.That(navigations, Is.Empty);

            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            });
            Assert.That(navigations, Is.EqualTo(new[] { problem.Problem }));
            var activate = view.InputBindings.OfType<KeyBinding>().Single(binding => binding.Key == Key.Return);
            activate.Command.Execute(activate.CommandParameter);
            Assert.That(navigations, Is.EqualTo(new[] { problem.Problem, problem.Problem }));
            Assert.That(model.SelectedProblem, Is.SameAs(problem));
        });
    }

    internal static void AssertShortWorkspaceLayout(ThemeType theme, double height)
    {
        WithTheme(theme, () =>
        {
            var model = new WorkspaceModel(false, true, 1);
            var view = new SuperViewWorkspaceView { DataContext = model };
            Layout(view, 960, height);
            var metadata = (MetadataPanel)view.FindName("MetadataPanel");
            var tags = Descendants<ListView>(metadata).Single(list => list.DisplayMemberPath == "DisplayName");
            var save = Descendants<Button>(metadata).Single(button => Equals(button.Content, "保存"));
            Assert.That(tags.ActualHeight, Is.GreaterThanOrEqualTo(48));
            AssertInside(tags, metadata);
            AssertInside(save, metadata);
            AssertInside((FrameworkElement)view.FindName("Playback"), view);
            Assert.That(save.Command, Is.SameAs(model.MetaEditor.SaveActionCommand));
        });
    }

    private static void AssertFieldVectorsFitHorizontally(MetadataPanel metadata, FrameworkElement workspace, double width, double height)
    {
        var attributes = Descendants<MetaDataAttributeView>(metadata).Single();
        var fields = Descendants<ListView>(attributes).Single();
        var scroll = Descendants<ScrollViewer>(fields).First();
        var presenter = Descendants<ScrollContentPresenter>(fields).First();
        var originalOffset = scroll.VerticalOffset;
        var vectors = fields.Items.Cast<object>()
            .Where(item => item is VectorAttributeViewModel or OrientationAttributeViewModel)
            .ToArray();
        Assert.That(vectors, Is.Not.Empty, "此布局样本必须实际覆盖三轴字段");
        Assert.That(ScrollViewer.GetHorizontalScrollBarVisibility(fields), Is.EqualTo(ScrollBarVisibility.Disabled));
        foreach (var vector in vectors)
        {
            fields.ScrollIntoView(vector);
            Layout(workspace, width, height);
            var editor = Descendants<Vector3View>(fields).Single(item => ReferenceEquals(item.DataContext, vector));
            AssertHorizontalInside(editor, presenter);
            var axes = Descendants<TextBox>(editor).ToArray();
            Assert.That(axes, Has.Length.EqualTo(3));
            foreach (var axis in axes)
            {
                AssertHorizontalInside(axis, editor);
                AssertHorizontalInside(axis, presenter);
            }
            Assert.That(scroll.ScrollableWidth, Is.EqualTo(0), "字段区不能用横向滚动隐藏三轴裁切");
        }
        scroll.ScrollToVerticalOffset(originalOffset);
        Layout(workspace, width, height);
    }

    private static void AssertHorizontalInside(FrameworkElement item, FrameworkElement root)
    {
        var bounds = new Rect(item.TranslatePoint(new Point(), root), item.RenderSize);
        Assert.That(bounds.Width, Is.GreaterThan(0));
        Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(-1));
        Assert.That(bounds.Right, Is.LessThanOrEqualTo(root.ActualWidth + 1));
    }

    private static void ScrollToElement(ScrollViewer scroll, FrameworkElement item, FrameworkElement workspace, double width, double height)
    {
        var content = (FrameworkElement)scroll.Content;
        var top = item.TranslatePoint(new Point(), content).Y;
        scroll.ScrollToVerticalOffset(Math.Max(0, top - 4));
        Layout(workspace, width, height);
    }

    internal static void WithTheme(ThemeType theme, Action action) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            new LocalizationManager().LoadLanguage();
            var previous = ThemesController.CurrentTheme;
            try { ThemesController.SetTheme(theme); action(); }
            finally { ThemesController.SetTheme(previous); }
        });

    internal static void Layout(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    internal static void AssertInside(FrameworkElement item, FrameworkElement root)
    {
        var bounds = new Rect(item.TranslatePoint(new Point(), root), item.RenderSize);
        Assert.That(bounds.Width, Is.GreaterThan(0), item.Name);
        Assert.That(bounds.Height, Is.GreaterThan(0), item.Name);
        Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(-1), item.Name);
        Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(-1), item.Name);
        Assert.That(bounds.Right, Is.LessThanOrEqualTo(root.ActualWidth + 1), item.Name);
        Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(root.ActualHeight + 1), item.Name);
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }

    internal static void Snapshot(FrameworkElement element, string name, int dpi)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi / 96),
            (int)Math.Ceiling(element.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name));
        encoder.Save(stream);
    }
}
