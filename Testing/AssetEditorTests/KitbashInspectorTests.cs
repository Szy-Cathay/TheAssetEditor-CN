using System.Dynamic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.Utility.UserInterface;
using KitbasherEditor.Views;
using KitbasherEditor.Views.EditorViews;
using KitbasherEditor.Views.EditorViews.Rmv2;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common.DataTemplates;
using static AssetEditorTests.PlaybackAndViewportRefinementTests;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class KitbashInspectorTests
{
    public static IEnumerable<TestCaseData> InspectorScales =>
        from theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme }
        from scale in new[] { 1d, 1.25d, 1.5d }
        select new TestCaseData(theme, scale);

    [Test]
    public void CategorySelection_SurvivesEditorReplacementAndUnavailableCategories()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var view = Create(Mesh(), "First mesh");
            Select(view, "Animation");
            Replace(view, Mesh(), "Second mesh");
            Assert.That(Selected(view), Is.EqualTo("Animation"));
            Assert.That(Descendants<Expander>(view), Is.Empty);
            Assert.That(Descendants<DataGrid>(view).Single().Items.Count, Is.EqualTo(1));

            Select(view, "Diagnostics");
            var unsupported = Mesh();
            unsupported.Material = null;
            Replace(view, unsupported, "Without weighted diagnostics");
            Assert.That(Selected(view), Is.EqualTo("Model"));
            Assert.That(Categories(view).Items.OfType<TabItem>().Single(item => Equals(item.Tag, "Diagnostics")).Visibility,
                Is.EqualTo(Visibility.Collapsed));
            Replace(view, Mesh(), "Weighted mesh again");
            Assert.That(Selected(view), Is.EqualTo("Diagnostics"));

            Replace(view, MainModel(), "可编辑模型");
            Assert.That(Selected(view), Is.EqualTo("General"));
            Select(view, "AttachmentPoints");
            Replace(view, Mesh(), "Mesh again");
            Assert.That(Selected(view), Is.EqualTo("Diagnostics"));
            Replace(view, MainModel(), "可编辑模型");
            Assert.That(Selected(view), Is.EqualTo("AttachmentPoints"));

            // A second workspace must not inherit the first workspace's choices.
            Assert.That(Selected(Create(Mesh(), "Independent")), Is.EqualTo("Model"));
        });
    }

    [Test]
    public void CategoryRestoration_CompletesBeforeFirstLayout()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var view = Create(Mesh(), "First");
            Select(view, "Animation");
            var root = (RootProbe)view.DataContext;
            var original = root.CurrentEditor;
            var originalTabs = Categories(view);
            root.CurrentEditor = original;
            Layout(view, 364, 524);
            Assert.That(Categories(view), Is.SameAs(originalTabs));
            Check(Mesh(), "Animation");
            Check(MainModel(), "General");
            Select(view, "AttachmentPoints");
            Check(Mesh(), "Animation");
            Check(MainModel(), "AttachmentPoints");
            foreach (var editor in new[]
            {
                new EditorProbe { ViewType = typeof(GroupView), GroupName = "Group" },
                new EditorProbe { ViewType = typeof(SkeletonView), BoneScale = 1 },
            })
            {
                root.CurrentEditor = editor;
                Layout(view, 364, 524);
                Assert.That(Descendants<TabControl>(view), Is.Empty);
                Check(Mesh(), "Animation");
            }
            foreach (var count in new[] { 0, 2, 3, 0 })
            {
                root.CurrentEditor = null;
                root.EmptyStateText = count == 0 ? "请选择一个节点" : $"已选择 {count} 个节点";
                Layout(view, 364, 524);
                Assert.That(Descendants<TabControl>(view), Is.Empty);
                Check(Mesh(), "Animation");
            }
            Select(view, "Diagnostics");
            Check(new EditorProbe { Mesh = Mesh().Mesh, Animation = Mesh().Animation }, "Model");
            Check(Mesh(), "Diagnostics");

            void Check(EditorProbe editor, string expected)
            {
                root.CurrentEditor = editor;
                Layout(view, 364, 524);
                Assert.That(Selected(view), Is.EqualTo(expected), "The first layout must already show the correct category.");
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.That(Selected(view), Is.EqualTo(expected), "No delayed correction may change the first-layout category.");
            }
        });
    }

    [Test]
    public void ScrollableContent_LeavesCategoryAndObjectIdentityVisible()
    {
        WithTheme(ThemeType.LightTheme, () =>
        {
            var view = Create(Mesh(), "long_mesh_name_that_identifies_the_selected_object", 250);
            Select(view, "Material", 250);
            var identity = Identity(view);
            var origin = identity.TranslatePoint(new Point(), view);
            var scroll = Descendants<ScrollViewer>(view).Single(item => item.Style == view.FindResource("Kitbash.InspectorScroll"));
            Assert.That(scroll.ScrollableHeight, Is.GreaterThan(100));
            scroll.ScrollToBottom();
            Refresh(view, 250);
            Assert.That(scroll.VerticalOffset, Is.GreaterThan(0));
            Assert.That(identity.TranslatePoint(new Point(), view), Is.EqualTo(origin));
            Assert.That(Categories(view).Items.OfType<TabItem>().All(item => item.ActualHeight >= 24), Is.True);
            Assert.That(Descendants<ScrollBar>(scroll).Any(), Is.True);
        });
    }

    [TestCaseSource(nameof(InspectorScales))]
    public void AllMaterialFields_AreDirectlyVisibleAndFitNarrowPanels(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var width = scale == 1 ? 300 : scale == 1.25 ? 360 : 460;
            var gallery = new StackPanel { Orientation = Orientation.Horizontal, Background = (Brush)Application.Current.FindResource("AeBrush.Surface1") };
            foreach (var variant in new[] { "metalrough", "specgloss", "advanced", "emissive" })
            {
                var editor = Mesh();
                editor.WsMaterial = MaterialData(variant);
                var view = Create(editor, variant);
                Select(view, "Material");
                VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
                Layout(view, width, 500);
                VerifySectionLines(view);
                Assert.That(Descendants<Expander>(view), Is.Empty, variant);
                var expectedTextures = variant switch { "metalrough" => 5, "specgloss" => 5, "advanced" => 9, _ => 7 };
                Assert.That(Descendants<ShaderTextureView>(view).Count(), Is.EqualTo(expectedTextures), variant);
                foreach (var texture in Descendants<ShaderTextureView>(view))
                {
                    Assert.That(texture.ActualHeight, Is.EqualTo(28).Within(1), variant);
                    Assert.That(texture.ActualWidth, Is.GreaterThanOrEqualTo(width - 24), variant);
                    Assert.That(Descendants<Button>(texture).Count(), Is.EqualTo(3), variant);
                    Assert.That(Descendants<TextBox>(texture).Single().ActualWidth, Is.GreaterThan(130), variant);
                    Assert.That(Descendants<CheckBox>(texture).Single().Visibility, Is.EqualTo(Visibility.Collapsed));
                    var row = (StackPanel)texture.Parent;
                    var toggle = row.Children.OfType<CheckBox>().Single();
                    Assert.That(toggle.ActualHeight, Is.EqualTo(24).Within(1), variant);
                    var pathInput = Descendants<TextBox>(texture).Single();
                    Assert.That(pathInput.TranslatePoint(new Point(), texture).X, Is.Zero.Within(1), variant);
                    Assert.That(texture.TranslatePoint(new Point(), row).Y - toggle.TranslatePoint(new Point(0, toggle.ActualHeight), row).Y,
                        Is.EqualTo(4).Within(1), variant);
                    Assert.That(toggle.DataContext, Is.SameAs(texture.DataContext));
                    toggle.IsChecked = false;
                    Assert.That(((IDictionary<string, object?>)texture.DataContext)["ShouldRenderTexture"], Is.False);
                    toggle.IsChecked = true;
                }
                var scroll = Descendants<ScrollViewer>(view).Single(item => item.Style == view.FindResource("Kitbash.InspectorScroll"));
                Assert.That(scroll.ExtentWidth, Is.LessThanOrEqualTo(scroll.ViewportWidth + 1), variant);
                var material = Descendants<Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.MeshSubViews.WsModelMaterial.ModelMaterialView>(view).Single();
                foreach (var input in Descendants<TextBox>(material))
                    Assert.That(input.ActualWidth, Is.GreaterThanOrEqualTo(48), variant);
                if (scale == 1.25)
                {
                    var full = Create(editor, variant);
                    Select(full, "Material");
                    full.Width = width;
                    gallery.Children.Add(full);
                }
            }
            if (scale == 1.25)
            {
                VisualTreeHelper.SetRootDpi(gallery, new DpiScale(scale, scale));
                Layout(gallery, width * 4);
                SettleAnimations();
                Render(gallery, $"inspector-all-materials-{theme}", scale);
            }
        });
    }

    [Test]
    public void TextureToggle_DefaultRemainsAvailableForOtherEditors()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var view = new ShaderTextureView { DataContext = Texture("body_base_colour.dds") };
            Layout(view, 360, 40);
            Assert.That(view.ShowRenderToggle, Is.True);
            var toggle = Descendants<CheckBox>(view).Single();
            Assert.That(toggle.Visibility, Is.EqualTo(Visibility.Visible));
            toggle.IsChecked = false;
            Assert.That(((IDictionary<string, object?>)view.DataContext)["ShouldRenderTexture"], Is.False);
            Assert.That(Descendants<Button>(view).Count(), Is.EqualTo(3));
            Assert.That(Descendants<Button>(view).All(button => button.Margin == new Thickness(1)), Is.True);
            Assert.That(((Grid)Descendants<TextBox>(view).Single().Parent).Margin, Is.EqualTo(new Thickness(2)));
        });
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void InspectorVectorFields_PreserveLiveEditsAndCallbacks(int dimensions)
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var edits = 0;
            object vector = dimensions switch
            {
                2 => new Vector2ViewModel(0, 0, _ => edits++),
                3 => new Vector3ViewModel(0, 0, 0, _ => edits++),
                _ => new Vector4ViewModel(0, 0, 0, 0, _ => edits++),
            };
            var editor = Mesh();
            if (dimensions == 3)
                ((IDictionary<string, object?>)editor.Mesh!)["Pivot"] = vector;
            else
            {
                editor.WsMaterial = MaterialData("advanced");
                var material = (IDictionary<string, object?>)editor.WsMaterial;
                var advanced = (IDictionary<string, object?>)material["AdvanceRvmMaterial"]!;
                advanced[dimensions == 2 ? "UvScale" : "TextureTransform"] = vector;
                advanced["ShowTextureTransform"] = true;
            }
            var view = Create(editor, "Vector editing");
            Select(view, dimensions == 3 ? "Model" : "Material");
            var field = Descendants<ContentControl>(view).Single(item => ReferenceEquals(item.Content, vector));
            var inputs = Descendants<TextBox>(field).ToArray();
            Assert.That(inputs, Has.Length.EqualTo(dimensions));
            edits = 0;
            for (var index = 0; index < inputs.Length; index++)
            {
                Assert.That(BindingOperations.GetBinding(inputs[index], TextBox.TextProperty)!.UpdateSourceTrigger,
                    Is.EqualTo(UpdateSourceTrigger.PropertyChanged));
                inputs[index].Text = (index + 2.5).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var values = vector switch
            {
                Vector2ViewModel v => new double[] { v.X.Value, v.Y.Value },
                Vector3ViewModel v => [v.X.Value, v.Y.Value, v.Z.Value],
                Vector4ViewModel v => [v.X.Value, v.Y.Value, v.Z.Value, v.W.Value],
                _ => throw new InvalidOperationException(),
            };
            Assert.That(values, Is.EqualTo(Enumerable.Range(0, dimensions).Select(index => index + 2.5)));
            Assert.That(edits, Is.EqualTo(dimensions), "Each edit must reach the original model callback once.");
        });
    }

    [Test]
    public void NameEditor_UsesOneSharedValueAcrossEveryCategory()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var view = Create(Mesh(), "First");
            var root = (RootProbe)view.DataContext;
            foreach (var category in new[] { "Model", "Material", "Animation", "Diagnostics", "Model" })
            {
                Select(view, category);
                var input = Identity(view);
                Assert.That(input.Text, Is.EqualTo(root.SelectedNodeName));
                Assert.That(input.IsReadOnly, Is.False);
                Assert.That(BindingOperations.GetBinding(input, TextBox.TextProperty)!.UpdateSourceTrigger, Is.EqualTo(UpdateSourceTrigger.LostFocus));
                input.Text = category + " renamed";
                BindingOperations.GetBindingExpression(input, TextBox.TextProperty)!.UpdateSource();
                Assert.That(root.SelectedNodeName, Is.EqualTo(category + " renamed"));
                Assert.That(Descendants<Expander>(view), Is.Empty);
            }
            root.SelectedNodeName = "Renamed from scene tree";
            Refresh(view);
            Assert.That(Identity(view).Text, Is.EqualTo(root.SelectedNodeName));
            Assert.That(Descendants<TextBox>(view).Count(item => item.Text == root.SelectedNodeName), Is.EqualTo(1));
        });
    }

    [TestCaseSource(nameof(InspectorScales))]
    public void RealInspectorViews_RenderWithChineseContentWithoutWindowsOrInput(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var windows = Application.Current.Windows.Count;
            var focus = Keyboard.FocusedElement;
            var captured = Mouse.Captured;
            var gallery = new Grid { Background = (Brush)Application.Current.FindResource("AeBrush.Surface2") };
            for (var i = 0; i < 3; i++) gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            for (var i = 0; i < 2; i++) gallery.RowDefinitions.Add(new RowDefinition { Height = new GridLength(540) });
            VisualTreeHelper.SetRootDpi(gallery, new DpiScale(scale, scale));
            var categories = new[] { "Model", "Material", "Animation", "Diagnostics", "General", "AttachmentPoints" };
            for (var index = 0; index < categories.Length; index++)
            {
                var view = Create(index < 4 ? Mesh() : MainModel(), index < 4 ? "empire_general_body" : "可编辑模型");
                Select(view, categories[index]);
                var card = new Border { Padding = new Thickness(4), Margin = new Thickness(4), Child = view };
                Grid.SetColumn(card, index % 3);
                Grid.SetRow(card, index / 3);
                gallery.Children.Add(card);
                Refresh(view);
                Assert.That(Identity(view).Text, Is.EqualTo(index < 4 ? "empire_general_body" : "可编辑模型"));
                Assert.That(Categories(view).Items.OfType<TabItem>().Select(item => item.Header), Is.EqualTo(index < 4
                    ? new[] { "模型", "材质", "动画", "诊断" } : new[] { "常规", "附着点" }));
                Assert.That(Descendants<Expander>(view), Is.Empty, categories[index]);
                var labels = Descendants<TextBlock>(view).Select(item => item.Text).ToArray();
                var inputs = Descendants<TextBox>(view).Select(item => item.Text).ToArray();
                switch (categories[index])
                {
                    case "Model":
                        Assert.That(inputs, Does.Contain("empire_general_body"));
                        Assert.That(labels, Does.Contain("8420"));
                        Assert.That(labels, Does.Contain("24468"));
                        break;
                    case "Material":
                        Assert.That(labels, Does.Contain("标准材质"));
                        break;
                    case "Animation":
                        Assert.That(labels, Does.Contain("humanoid01"));
                        Assert.That(labels, Does.Contain("bip_head"));
                        break;
                    case "Diagnostics":
                        Assert.That(labels, Does.Contain("2骨骼蒙皮"));
                        Assert.That(labels, Does.Contain("基础颜色 - body_base_colour.dds"));
                        break;
                    case "General":
                        Assert.That(inputs, Does.Contain("humanoid01"));
                        break;
                    case "AttachmentPoints":
                        Assert.That(labels, Does.Contain("weapon_1"));
                        Assert.That(labels, Does.Contain("weapon_2"));
                        break;
                }
            }
            Layout(gallery, 1140, 1080);
            VerifySectionLines(gallery);
            SettleAnimations();
            if (scale == 1.25)
                Render(gallery, $"inspector-categories-{theme}-{scale:0.00}", scale);

            var simple = new Grid { Background = gallery.Background };
            for (var i = 0; i < 3; i++) simple.ColumnDefinitions.Add(new ColumnDefinition());
            var group = Create(new EditorProbe { ViewType = typeof(GroupView), GroupName = "披风组件组" }, "披风组件组", 300);
            var skeleton = Create(new EditorProbe
            {
                ViewType = typeof(SkeletonView), BoneScale = 1,
                Bones = new[] { Bag(("BoneName", "root"), ("BoneIndex", 0), ("Children", Array.Empty<object>())) }
            }, "humanoid01", 300);
            var empty = new SceneItemEditorView { DataContext = new RootProbe { EmptyStateText = "已选择 3 个节点，请选择单个节点查看详细信息" } };
            var simpleViews = new[] { group, skeleton, empty };
            for (var i = 0; i < simpleViews.Length; i++)
            {
                Grid.SetColumn(simpleViews[i], i);
                simple.Children.Add(simpleViews[i]);
            }
            VisualTreeHelper.SetRootDpi(simple, new DpiScale(scale, scale));
            Layout(simple, 1140, 300);
            VerifySectionLines(simple);
            Assert.That(Descendants<TabControl>(simple), Is.Empty);
            Assert.That(Descendants<TextBox>(group).Single().Text, Is.EqualTo("披风组件组"));
            Assert.That(Descendants<TreeView>(skeleton).Single().Items.Count, Is.EqualTo(1));
            if (scale == 1.25)
                Render(simple, $"inspector-other-states-{theme}-{scale:0.00}", scale);
            Assert.Multiple(() =>
            {
                Assert.That(Application.Current.Windows.Count, Is.EqualTo(windows));
                Assert.That(Keyboard.FocusedElement, Is.SameAs(focus));
                Assert.That(Mouse.Captured, Is.SameAs(captured));
            });
        });
    }

    private static SceneItemEditorView Create(EditorProbe editor, string name, double height = 524)
    {
        var view = new SceneItemEditorView { DataContext = new RootProbe { CurrentEditor = editor, SelectedNodeName = name } };
        Refresh(view, height);
        return view;
    }

    private static void Replace(SceneItemEditorView view, EditorProbe editor, string name)
    {
        var root = (RootProbe)view.DataContext;
        root.CurrentEditor = editor;
        root.SelectedNodeName = name;
        Refresh(view);
    }

    private static void Refresh(FrameworkElement view, double height = 524)
    {
        Layout(view, 364, height);
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Layout(view, 364, height);
    }

    private static void SettleAnimations()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void VerifySectionLines(DependencyObject view)
    {
        VerifyTypographyAndInputs(view);
        VerifyCheckBoxLabels(view);
        foreach (var line in Descendants<Separator>(view).Where(item => item.Parent is DockPanel))
        {
            var row = (DockPanel)line.Parent;
            var caption = (FrameworkElement)row.Children[0];
            var start = line.TranslatePoint(new Point(), row);
            var captionEnd = caption.TranslatePoint(new Point(caption.ActualWidth, 0), row);
            Assert.That(line.ActualWidth, Is.GreaterThan(8), "Every heading must leave room for its rule.");
            Assert.That(line.ActualHeight, Is.InRange(0.5, 1.5));
            Assert.That(start.X - captionEnd.X, Is.EqualTo(8).Within(1));
            Assert.That(start.X + line.ActualWidth, Is.EqualTo(row.ActualWidth).Within(1));
            Assert.That(start.Y + line.ActualHeight / 2, Is.EqualTo(row.ActualHeight / 2).Within(1));
            Assert.That(row.Margin.Bottom, Is.EqualTo(4));
            Assert.That(row.Margin.Top, Is.AnyOf(0d, 4d));
            Assert.That(((SolidColorBrush)line.Background).Color,
                Is.EqualTo(((SolidColorBrush)Application.Current.FindResource("AeBrush.Border")).Color));
            foreach (var text in Descendants<TextBlock>(caption).Prepend(caption).OfType<TextBlock>())
            {
                Assert.That(text.FontStyle, Is.EqualTo(FontStyles.Italic), text.Text);
                Assert.That(text.FontWeight, Is.EqualTo(FontWeights.Normal), text.Text);
                Assert.That(((SolidColorBrush)text.Foreground).Color,
                    Is.EqualTo(((SolidColorBrush)Application.Current.FindResource("AeBrush.TextSecondary")).Color), text.Text);
                Assert.That(text.IsEnabled, Is.True, "Static headings must not disable their content.");
                Assert.That(text.Margin.Top + text.Margin.Bottom, Is.Zero, text.Text);
            }
            if (row.Parent is Border wrapper)
                Assert.That(wrapper.Margin.Top + wrapper.Margin.Bottom + wrapper.Padding.Top + wrapper.Padding.Bottom, Is.Zero);
        }
    }

    private static void VerifyCheckBoxLabels(DependencyObject view)
    {
        foreach (var toggle in Descendants<CheckBox>(view).Where(item => item.Style == item.TryFindResource("Kitbash.InspectorCheckBox")))
        {
            var box = (Border)toggle.Template.FindName("Box", toggle);
            var label = Descendants<TextBlock>(toggle).Single();
            Assert.That(label.Text, Is.Not.Empty);
            Assert.That(toggle.TranslatePoint(new Point(), (UIElement)toggle.Parent).X, Is.Zero.Within(1), label.Text);
            Assert.That(label.TranslatePoint(new Point(), toggle).X - box.TranslatePoint(new Point(box.ActualWidth, 0), toggle).X,
                Is.EqualTo(8).Within(1), label.Text);
            Assert.That(label.TranslatePoint(new Point(0, label.ActualHeight / 2), toggle).Y,
                Is.EqualTo(box.TranslatePoint(new Point(0, box.ActualHeight / 2), toggle).Y).Within(1), label.Text);
            Assert.That(label.TranslatePoint(new Point(label.ActualWidth, 0), toggle).X,
                Is.LessThanOrEqualTo(toggle.ActualWidth + 1), label.Text);
            if (!toggle.IsEnabled)
                continue;

            var hit = VisualTreeHelper.HitTest(toggle,
                label.TranslatePoint(new Point(label.ActualWidth / 2, label.ActualHeight / 2), toggle))?.VisualHit;
            Assert.That(hit, Is.Not.Null, "The label must have an offscreen hit area.");
            Assert.That(hit == toggle || toggle.IsAncestorOf(hit), Is.True, "The label must belong to the checkbox hit area.");
            var source = (object)toggle.DataContext;
            var path = BindingOperations.GetBinding(toggle, ToggleButton.IsCheckedProperty)!.Path.Path.Split('.');
            foreach (var segment in path.SkipLast(1))
                source = source is IDictionary<string, object?> fields
                    ? fields[segment]!
                    : source.GetType().GetProperty(segment)!.GetValue(source)!;
            var values = (IDictionary<string, object?>)source;
            var previous = toggle.IsChecked;
            var peer = new CheckBoxAutomationPeer(toggle);
            var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
            provider.Toggle();
            Assert.That(toggle.IsChecked, Is.EqualTo(!previous), label.Text);
            Assert.That(values[path[^1]], Is.EqualTo(!previous), "The original binding must receive the toggle.");
            provider.Toggle();
            Assert.That(values[path[^1]], Is.EqualTo(previous), label.Text);
        }
    }

    private static void VerifyTypographyAndInputs(DependencyObject view)
    {
        var family = ((FontFamily)Application.Current.FindResource("AppFontFamily")).Source;
        Assert.Multiple(() =>
        {
            foreach (var text in Descendants<TextBlock>(view).Where(item => item.ActualHeight > 0 && !string.IsNullOrWhiteSpace(item.Text)))
            {
                Assert.That(text.FontFamily.Source, Is.EqualTo(family), text.Text);
                Assert.That(text.FontSize, Is.EqualTo(12), text.Text);
                Assert.That(text.FontWeight, Is.EqualTo(FontWeights.Normal), text.Text);
                var heading = text.Style == text.TryFindResource("Kitbash.InspectorHeading");
                Assert.That(text.FontStyle, Is.EqualTo(heading ? FontStyles.Italic : FontStyles.Normal), text.Text);
            }
            foreach (var control in Descendants<Control>(view).Where(item => item.ActualHeight > 0 &&
                item is TextBox or ComboBox or Button or Label or TabItem or DataGridColumnHeader))
            {
                var context = $"{control.GetType().Name} {control.Name}";
                Assert.That(control.FontFamily.Source, Is.EqualTo(family), context);
                Assert.That(control.FontSize, Is.EqualTo(12), context);
                Assert.That(control.FontWeight, Is.EqualTo(FontWeights.Normal), context);
                Assert.That(control.FontStyle, Is.EqualTo(FontStyles.Normal), context);
            }
            foreach (var input in Descendants<TextBox>(view).Where(item => item.ActualHeight > 0 && item.Name != "PART_EditableTextBox"))
            {
                var text = new FormattedText("中文Aa09", System.Globalization.CultureInfo.CurrentUICulture,
                    input.FlowDirection, new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch),
                    input.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(input).PixelsPerDip);
                Assert.That(input.ActualHeight, Is.EqualTo(28).Within(1), input.Text);
                Assert.That(input.ActualHeight - input.Padding.Top - input.Padding.Bottom - input.BorderThickness.Top - input.BorderThickness.Bottom,
                    Is.GreaterThanOrEqualTo(text.Height), input.Text);
                Assert.That(input.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Center), input.Text);
                Assert.That(input.HorizontalContentAlignment, Is.EqualTo(HorizontalAlignment.Left), input.Text);
                Assert.That(input.TextAlignment, Is.EqualTo(TextAlignment.Left), input.Text);
                var path = BindingOperations.GetBinding(input, TextBox.TextProperty)?.Path.Path;
                if (path == "BoneScale" || path?.StartsWith("Emissive", StringComparison.Ordinal) == true)
                    Assert.That(input.ActualWidth, Is.EqualTo(100).Within(1), path);
            }
            foreach (var combo in Descendants<ComboBox>(view).Where(item => item.ActualHeight > 0))
            {
                Assert.That(combo.ActualHeight, Is.EqualTo(28).Within(1));
                Assert.That(combo.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Center));
            }
            foreach (var row in Descendants<Grid>(view).Where(item => item.ColumnDefinitions.Count == 2 &&
                item.ColumnDefinitions[0].Width.IsAbsolute && item.Children.OfType<Label>().Any()))
            {
                Assert.That(row.ColumnDefinitions[0].ActualWidth, Is.EqualTo(136).Within(1));
                foreach (var value in row.Children.OfType<FrameworkElement>().Where(item => Grid.GetColumn(item) == 1))
                    Assert.That(value.TranslatePoint(new Point(), row).X, Is.EqualTo(136).Within(1), value.GetType().Name);
            }
            foreach (var axis in Descendants<DockPanel>(view).Where(item => item.Children.OfType<TextBox>().Any() && item.Children.OfType<TextBlock>().Any()))
            {
                var input = axis.Children.OfType<TextBox>().Single();
                var label = axis.Children.OfType<TextBlock>().Single();
                Assert.That(input.TranslatePoint(new Point(0, input.ActualHeight / 2), axis).Y,
                    Is.EqualTo(label.TranslatePoint(new Point(0, label.ActualHeight / 2), axis).Y).Within(1));
                Assert.That(input.TranslatePoint(new Point(), axis).X - label.TranslatePoint(new Point(label.ActualWidth, 0), axis).X,
                    Is.EqualTo(8).Within(1));
            }
            foreach (var label in Descendants<Label>(view).Where(item => item.Style == item.TryFindResource("Kitbash.InspectorStackedLabel")))
            {
                if (label.Parent is not Panel parent)
                    continue;
                var next = parent.Children.OfType<FrameworkElement>().SkipWhile(item => item != label).Skip(1).First();
                Assert.That(next.TranslatePoint(new Point(), parent).Y - label.TranslatePoint(new Point(0, label.ActualHeight), parent).Y,
                    Is.EqualTo(4).Within(1), label.Content?.ToString());
            }
        });
    }

    private static TabControl Categories(DependencyObject view) => Descendants<TabControl>(view).Single();
    private static string Selected(DependencyObject view) => (string)((TabItem)Categories(view).SelectedItem).Tag;
    private static TextBox Identity(SceneItemEditorView view) => Descendants<TextBox>(view).Single(item =>
        BindingOperations.GetBinding(item, TextBox.TextProperty)?.Path.Path == "DataContext.SelectedNodeName");
    private static void Select(SceneItemEditorView view, string category, double height = 524)
    {
        Categories(view).SelectedItem = Categories(view).Items.OfType<TabItem>().Single(item => Equals(item.Tag, category));
        Refresh(view, height);
    }

    private static readonly ICommand NoAction = new RelayCommand(() => { });

    private static EditorProbe MainModel() => new()
    {
        ViewType = typeof(MainEditableNodeView), SkeletonName = "humanoid01", SkeletonNameList = ["humanoid01", "hu1"],
        AttachmentPointList = [Bag(("BoneIndex", 12), ("Name", "weapon_1"), ("IsIdentiy", true)), Bag(("BoneIndex", 18), ("Name", "weapon_2"), ("IsIdentiy", false))]
    };

    private static EditorProbe Mesh()
    {
        var none = Bag(("BoneIndex", Box(-1)), ("Name", Box("无")));
        var bone = Bag(("BoneIndex", Box(12)), ("Name", Box("bip_head")));
        return new EditorProbe
        {
            ViewType = typeof(MeshEditorView),
            Mesh = Bag(("ModelName", "empire_general_body"), ("VertexCount", 8420), ("IndexCount", 24468),
                ("PossibleVertexTypes", Enum.GetValues<UiVertexFormat>()), ("VertexType", UiVertexFormat.Weighted),
                ("ReduceMeshOnLodGeneration", true), ("DrawBoundingBox", false), ("DrawPivotPoint", false),
                ("Pivot", new Vector3ViewModel(0, 0, 0, null)), ("CopyPivotToAllMeshesCommand", NoAction)),
            Animation = Bag(("SkeletonName", "humanoid01"), ("AnimationMatrixOverride", -1), ("AnimatedBones", new List<object> { bone }),
                ("AttachableBones", Bag(("Filter", "无"), ("Values", new[] { none, bone }), ("SelectedItem", none)))),
            WsMaterial = Bag(("PossibleMaterialTypes", new[] { CapabilityMaterialsEnum.MetalRoughPbr_Default, CapabilityMaterialsEnum.MetalRoughPbr_Emissive }),
                ("CurrentMaterialType", CapabilityMaterialsEnum.MetalRoughPbr_Default),
                ("MetalRough", Bag(("UseAlpha", false), ("BaseColour", Texture("body_base_colour.dds")), ("MaterialMap", Texture("body_material.dds")), ("NormalMap", Texture("body_normal.dds")), ("Mask", Texture("body_mask.dds")))),
                ("SpecGloss", null), ("AdvanceRvmMaterial", null), ("Emissive", null),
                ("Blood", Bag(("UseBlood", true), ("BloodUvScale", new Vector2ViewModel(1, 1, null)), ("BloodMap", Texture("blood.dds")))),
                ("Tint", Bag(("ApplyFactionColours", true), ("FactionColour0", Colour(Colors.Red)), ("FactionColour1", Colour(Colors.Green)), ("FactionColour2", Colour(Colors.Blue)), ("SaveFactionColourSettingsCommand", NoAction)))),
            Material = Bag(("Filters", Box("0")), ("MatrixIndex", Box(-1)), ("ParentMatrixIndex", Box(-1)),
                ("BinaryVertexFormat", Box("2骨骼蒙皮")), ("MaterialId", Box("蒙皮")),
                ("TransformInfo", Box("轴心为单位变换：是；矩阵为单位变换：是")), ("StringParameters", Array.Empty<string>()),
                ("FloatParameters", new[] { "(0, 1)" }), ("IntParameters", Array.Empty<string>()),
                ("TextureParameters", new[] { "基础颜色 - body_base_colour.dds", "法线 - body_normal.dds" }),
                ("AttachmentPointParameters", Array.Empty<string>()), ("VectorParameters", Array.Empty<string>()))
        };
    }

    private static object Texture(string path) => Bag(("Path", path), ("FileName", path), ("ShouldRenderTexture", true),
        ("HandleClearTextureCommand", NoAction), ("HandleBrowseTextureCommand", NoAction), ("HandlePreviewTextureCommand", NoAction));
    private static object Box(object value) => Bag(("Value", value));
    private static object MaterialData(string variant)
    {
        var specular = variant is "specgloss" or "advanced";
        var types = specular
            ? new[] { CapabilityMaterialsEnum.SpecGlossPbr_Default, CapabilityMaterialsEnum.SpecGlossPbr_Advanced }
            : new[] { CapabilityMaterialsEnum.MetalRoughPbr_Default, CapabilityMaterialsEnum.MetalRoughPbr_Emissive };
        var maps = Bag(("UseAlpha", false), ("BaseColour", Texture("body_base_colour.dds")), ("MaterialMap", Texture("body_material.dds")),
            ("DiffuseMap", Texture("body_diffuse.dds")), ("SpecularMap", Texture("body_specular.dds")), ("GlossMap", Texture("body_gloss.dds")),
            ("NormalMap", Texture("body_normal.dds")), ("Mask", Texture("body_mask.dds")));
        return Bag(("PossibleMaterialTypes", types), ("CurrentMaterialType", types[variant is "advanced" or "emissive" ? 1 : 0]),
            ("MetalRough", specular ? null : maps), ("SpecGloss", specular ? maps : null),
            ("AdvanceRvmMaterial", variant == "advanced" ? Bag(("UseDirt", true), ("UseDecal", false), ("UseSkin", true),
                ("ShowDirtMap", true), ("ShowDirtMask", true), ("ShowSkinMask", true), ("ShowDecalMask", false), ("ShowUvScale", true), ("ShowTextureTransform", false),
                ("DirtMap", Texture("dirt.dds")), ("DirtMask", Texture("dirt_mask.dds")), ("SkinMask", Texture("skin_mask.dds")), ("DecalMask", Texture("decal.dds")),
                ("UvScale", new Vector2ViewModel(1, 1, null)), ("TextureTransform", new Vector4ViewModel(1, 1, 0, 0, null))) : null),
            ("Blood", specular ? null : Bag(("UseBlood", true), ("BloodUvScale", new Vector2ViewModel(1, 1, null)), ("BloodMap", Texture("blood.dds")))),
            ("Tint", Bag(("ApplyFactionColours", true), ("FactionColour0", Colour(Colors.Red)), ("FactionColour1", Colour(Colors.Green)),
                ("FactionColour2", Colour(Colors.Blue)), ("SaveFactionColourSettingsCommand", NoAction))),
            ("Emissive", variant == "emissive" ? Bag(("EmissiveTexture", Texture("glow.dds")), ("EmissiveTint", Colour(Colors.Orange)),
                ("EmissiveStrength", 1d), ("EmissiveFresnelStrength", 0d), ("EmissiveTiling", new Vector2ViewModel(1, 1, null)),
                ("Gradient0", Colour(Colors.Black)), ("Gradient1", Colour(Colors.Red)), ("Gradient2", Colour(Colors.Orange)), ("Gradient3", Colour(Colors.White)),
                ("GradientTime0", 0d), ("GradientTime1", .33d), ("GradientTime2", .66d), ("GradientTime3", 1d),
                ("EmissiveDistortionTexture", Texture("distortion.dds")), ("EmissiveDirection", new Vector2ViewModel(1, 1, null)),
                ("EmissiveDistortStrength", .1d), ("EmissiveSpeed", 1d), ("EmissivePulseSpeed", 1d), ("EmissivePulseStrength", .5d)) : null));
    }
    private static object Colour(Color colour) => Bag(("PickedColor", colour), ("HandleColourChangedCommand", NoAction));
    private static object Bag(params (string Name, object? Value)[] values)
    {
        IDictionary<string, object?> result = new ExpandoObject();
        foreach (var (name, value) in values) result[name] = value;
        return result;
    }

    public sealed class RootProbe : ObservableObject
    {
        private object? _currentEditor;
        private string _selectedNodeName = string.Empty;
        public object? CurrentEditor { get => _currentEditor; set => SetProperty(ref _currentEditor, value); }
        public string SelectedNodeName { get => _selectedNodeName; set => SetProperty(ref _selectedNodeName, value); }
        public string EmptyStateText { get; set; } = string.Empty;
        public bool IsSelectedNodeNameReadOnly => CurrentEditor is not EditorProbe { ViewType: var type } ||
            (type != typeof(MeshEditorView) && type != typeof(GroupView));
    }

    public sealed class EditorProbe : IViewProviderGeneric
    {
        public Type ViewType { get; set; } = typeof(MeshEditorView);
        public object? Mesh { get; set; }
        public object? Animation { get; set; }
        public object? Material { get; set; }
        public object? WsMaterial { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string SkeletonName { get; set; } = string.Empty;
        public string[] SkeletonNameList { get; set; } = [];
        public object? FilterByFullPath { get; set; }
        public List<object> AttachmentPointList { get; set; } = [];
        public ICommand ResetAttachmentPointListCommand => NoAction;
        public void CopyTexturesToOutputPack() { }
        public double BoneScale { get; set; }
        public object? SelectedBone { get; set; }
        public object[] Bones { get; set; } = [];
    }
}
