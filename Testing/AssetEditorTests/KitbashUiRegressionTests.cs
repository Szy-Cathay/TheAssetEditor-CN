using Moq;
using Editors.KitbasherEditor.Core;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.BaseDialogs.StandardDialog.PackFile;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Rendering.Materials.Capabilities.Utility;
using GameWorld.Core.Utility.UserInterface;
using Editors.KitbasherEditor.ViewModels.SaveDialog;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.MetalRough;
using KitbasherEditor.Views;
using NUnit.Framework;
using Shared.Core.Misc;
using Shared.Core.Settings;
using Shared.EmbeddedResources;
using Shared.GameFormats.RigidModel.Types;
using Shared.Ui.Common;
using Shared.Ui.Common.Behaviors;
using Shared.Ui.Common.DataTemplates;
using Shared.Ui.Common.MenuSystem;
using Shared.Ui.Common.ValueConverters;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class KitbashUiRegressionTests
{
    [TestCaseSource(nameof(Themes))]
    public void EmphasizedButtonText_UsesTheButtonsForeground(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            foreach (var styleKey in new[] { "AeButton.Primary", "AeButton.Danger" })
            {
                foreach (var content in new[] { "应用", "_应用" })
                {
                    var button = new Button
                    {
                        Content = content,
                        Style = (Style)Application.Current.FindResource(styleKey),
                    };
                    WithWindow(button, 320, window =>
                    {
                        foreach (var enabled in new[] { true, false })
                        {
                            button.IsEnabled = enabled;
                            window.UpdateLayout();
                            var labels = Descendants<TextBlock>(button).ToArray();
                            NUnitAssert.That(labels, Is.Not.Empty);
                            foreach (var label in labels)
                            {
                                NUnitAssert.That(((SolidColorBrush)label.Foreground).Color,
                                    Is.EqualTo(((SolidColorBrush)button.Foreground).Color),
                                    $"{theme}, {styleKey}, {content}, enabled={enabled}");
                            }
                            if (enabled)
                            {
                                var foreground = ((SolidColorBrush)labels[0].Foreground).Color;
                                NUnitAssert.That(Contrast(foreground, ((SolidColorBrush)button.Background).Color),
                                    Is.GreaterThanOrEqualTo(4.5), $"{theme}, {styleKey}");
                                if (styleKey == "AeButton.Primary")
                                {
                                    var hover = (SolidColorBrush)Application.Current.FindResource("AeBrush.AccentHover");
                                    NUnitAssert.That(Contrast(foreground, hover.Color),
                                        Is.GreaterThanOrEqualTo(4.5), $"{theme}, hovered {styleKey}");
                                }
                            }
                        }
                    });
                }
            }
        });
    }

    [TestCase(360)]
    [TestCase(640)]
    [TestCase(900)]
    public void Toolbar_ReservesSpaceForViewportControls(double width)
    {
        WithTheme(ThemeType.LightTheme, () =>
        {
            var view = new MenuBarView
            {
                DataContext = new
                {
                    MenuItems = Array.Empty<object>(),
                    CustomButtons = Enumerable.Range(0, 20).Select(_ =>
                        new MenuBarButton(new MenuAction { ToolTip = "操作" })
                        {
                            Image = IconLibrary.SaveFileIcon,
                        }).ToArray(),
                    SelectionSettings = new KitbashSelectionSettings(),
                    ProportionalEditing = new ToolbarSettings(),
                    ViewportShading = new ToolbarSettings(),
                },
            };
            WithWindow(view, width, _ =>
            {
                var toolbar = Descendants<ToolBar>(view).Single();
                var xray = (FrameworkElement)view.FindName("XRaySelectionButton");
                var toolbarBounds = Bounds(toolbar, view);
                var xrayBounds = Bounds(xray, view);
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(toolbarBounds.Right, Is.LessThanOrEqualTo(xrayBounds.Left));
                    NUnitAssert.That(toolbar.ActualWidth, Is.GreaterThan(0));
                    NUnitAssert.That(xrayBounds.Right, Is.LessThanOrEqualTo(view.ActualWidth));
                });
                var save = Descendants<Button>(toolbar).First(button => Equals(button.ToolTip, "操作"));
                var bitmap = new RenderTargetBitmap(26, 26, 96, 96, PixelFormats.Pbgra32);
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                    context.DrawRectangle(new VisualBrush(save), null, new Rect(0, 0, 26, 26));
                bitmap.Render(drawing);
                var pixels = new byte[26 * 26 * 4];
                bitmap.CopyPixels(pixels, 26 * 4, 0);
                var visibleGlyphPixels = 0;
                for (var y = 5; y < 21; y++)
                {
                    for (var x = 5; x < 21; x++)
                    {
                        var offset = (y * 26 + x) * 4;
                        if (pixels[offset + 3] > 180 && pixels[offset] < 128
                            && pixels[offset + 1] < 128 && pixels[offset + 2] < 128)
                            visibleGlyphPixels++;
                    }
                }
                NUnitAssert.That(visibleGlyphPixels, Is.GreaterThan(20),
                    "The save glyph must remain visible against the light toolbar.");
            });
        });
    }

    [Test]
    public void EmptySearchBox_RefreshesItsWatermarkWhenTheThemeChanges()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var input = new TextBox { Style = (Style)Application.Current.FindResource("AeInput.TextBox") };
            TextBoxExtensions.SetWatermark(input, "搜索资源");
            WithWindow(input, 320, window =>
            {
                foreach (var theme in Enum.GetValues<ThemeType>())
                {
                    ThemesController.SetTheme(theme);
                    window.UpdateLayout();
                    var background = (Grid)((VisualBrush)input.Background).Visual;
                    var expected = (SolidColorBrush)Application.Current.FindResource("AeBrush.Surface2");
                    NUnitAssert.That(((SolidColorBrush)background.Background).Color,
                        Is.EqualTo(expected.Color), theme.ToString());
                }
            });
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void ToolbarOverflow_OnlyAppearsWhenNeededAndKeepsButtonsReachable(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var toolbar = new ToolBar();
            for (var index = 0; index < 12; index++)
                toolbar.Items.Add(new Button { Content = $"工具 {index}", Width = 70 });
            WithWindow(toolbar, 1000, window =>
            {
                var arrow = (ToggleButton)toolbar.Template.FindName("OverflowButton", toolbar);
                NUnitAssert.That(toolbar.HasOverflowItems, Is.False);
                NUnitAssert.That(arrow.IsVisible, Is.False, "An empty overflow must not advertise an action.");

                window.Width = 260;
                window.UpdateLayout();
                NUnitAssert.That(toolbar.HasOverflowItems, Is.True);
                NUnitAssert.That(arrow.IsVisible, Is.True);
                arrow.IsChecked = true;
                window.UpdateLayout();
                var popup = (Popup)toolbar.Template.FindName("OverflowPopup", toolbar);
                NUnitAssert.That(popup.IsOpen, Is.True);
                var hiddenButton = toolbar.Items.Cast<Button>().First(ToolBar.GetIsOverflowItem);
                NUnitAssert.That(hiddenButton.IsVisible, Is.True);
                var invoked = false;
                hiddenButton.Click += (_, _) => invoked = true;
                hiddenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                NUnitAssert.That(invoked, Is.True);
                window.Width = 1000;
                window.UpdateLayout();
                NUnitAssert.That(toolbar.HasOverflowItems, Is.False);
                NUnitAssert.That(arrow.IsVisible, Is.False);
                NUnitAssert.That(popup.IsOpen, Is.False);
            });
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void Sidebar_CollapsesWithoutLeavingAStripOrChangingTheViewport(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var move = new MenuBarGroupButton(new MenuAction { ToolTip = "移动" }, "Gizmo")
            {
                Image = IconLibrary.Gizmo_MoveIcon,
            };
            var viewportBackground = new NotifyAttr<Color>(Color.FromRgb(50, 50, 50));
            var model = new WorkspaceModel
            {
                MenuBar = new
                {
                    MenuItems = Array.Empty<object>(),
                    CustomButtons = Array.Empty<object>(),
                    SidebarButtons = new[] { move },
                    TransformTool = new { IsVisible = false },
                    ViewportBackground = viewportBackground,
                    CanUseMeshSelectionTools = new NotifyAttr<bool>(false),
                    SelectionSettings = new KitbashSelectionSettings(),
                },
            };
            var scene = (Border)model.Scene;
            scene.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            var view = new KitbasherView { DataContext = model };
            WithWindow(view, 900, window =>
            {
                var toggle = view.FindName("SidebarToggle") as ToggleButton;
                NUnitAssert.That(toggle, Is.Not.Null, "The sidebar needs a reachable collapse/expand control.");
                var tools = (ItemsControl)view.FindName("SidebarTools");
                var panel = (FrameworkElement)view.FindName("SidebarOverlay");
                var sceneBounds = Bounds(scene, view);
                var belowTools = new Point(sceneBounds.Left + 12, sceneBounds.Bottom - 10);
                NUnitAssert.That(view.InputHitTest(belowTools), Is.SameAs(scene), "The unused strip must belong to the viewport.");
                NUnitAssert.That(panel.ActualHeight, Is.LessThan(scene.ActualHeight));
                NUnitAssert.That(Bounds(panel, view).Left, Is.GreaterThanOrEqualTo(sceneBounds.Left));
                var glyph = Descendants<System.Windows.Shapes.Rectangle>(tools).Single();
                NUnitAssert.That(Contrast(((SolidColorBrush)glyph.Fill).Color, ((SolidColorBrush)scene.Background).Color),
                    Is.GreaterThanOrEqualTo(3), "Transparent toolbar icons must contrast with the viewport in every theme.");
                var chevron = Descendants<System.Windows.Shapes.Path>(toggle!).Single();
                NUnitAssert.That(Contrast(((SolidColorBrush)chevron.Stroke).Color, ((SolidColorBrush)scene.Background).Color),
                    Is.GreaterThanOrEqualTo(3), "The expanded sidebar arrow must remain readable on the viewport.");
                viewportBackground.Value = Colors.White;
                scene.Background = Brushes.White;
                window.UpdateLayout();
                NUnitAssert.That(Contrast(((SolidColorBrush)glyph.Fill).Color, Colors.White),
                    Is.GreaterThanOrEqualTo(3), "Changing the viewport background must refresh the toolbar contrast.");
                NUnitAssert.That(Contrast(((SolidColorBrush)chevron.Stroke).Color, Colors.White), Is.GreaterThanOrEqualTo(3));

                toggle!.IsChecked = false;
                window.UpdateLayout();
                NUnitAssert.That(tools.IsVisible, Is.False);
                NUnitAssert.That(toggle.IsVisible, Is.True);
                NUnitAssert.That(Bounds(scene, view), Is.EqualTo(sceneBounds));
                NUnitAssert.That(panel.ActualHeight, Is.EqualTo(toggle.ActualHeight));

                move.IsChecked.Value = true;
                toggle.IsChecked = true;
                window.UpdateLayout();
                NUnitAssert.That(tools.IsVisible, Is.True);
                NUnitAssert.That(Descendants<RadioButton>(tools).Single().IsChecked, Is.True);
                NUnitAssert.That(Bounds(scene, view), Is.EqualTo(sceneBounds));
                NUnitAssert.That(toggle.Focusable, Is.False);

                tools.ItemTemplateSelector = null;
                tools.ItemTemplate = (DataTemplate)tools.Resources["sidebarSelectionTemplate"];
                window.UpdateLayout();
                var circle = Descendants<ToggleButton>(tools).Single(button => button.Name == "CircleSelectionButton");
                var circleSurface = (Border)circle.Template.FindName("Chrome", circle);
                NUnitAssert.That(circle.IsEnabled, Is.False);
                NUnitAssert.That(((SolidColorBrush)circleSurface.Background).Color.A, Is.Zero,
                    "A disabled circle-selection tool must not leave an opaque block on the viewport.");
                NUnitAssert.That(((SolidColorBrush)circleSurface.BorderBrush).Color.A, Is.Zero);
            });
        });
    }

    [TestCase(760)]
    [TestCase(1280)]
    public void MaterialPaths_KeepUsableWidthInTheWorkspace(double width)
    {
        WithTheme(ThemeType.LightTheme, () =>
        {
            var material = new MetalRoughView();
            var view = new KitbasherView
            {
                DataContext = new WorkspaceModel
                {
                    SceneNodeEditor = new { CurrentEditor = material },
                },
            };
            WithWindow(view, width, _ =>
            {
                var paths = Descendants<TextBox>(material).ToArray();
                NUnitAssert.That(paths.Length, Is.EqualTo(4));
                foreach (var path in paths)
                    NUnitAssert.That(path.ActualWidth, Is.GreaterThanOrEqualTo(80),
                        "Texture paths must remain visible beside their action buttons.");
            });
        });
    }

    [Test]
    public void SidebarTools_DoNotTakeViewportKeyboardFocus()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var view = new KitbasherView
            {
                DataContext = new WorkspaceModel
                {
                    MenuBar = new
                    {
                        MenuItems = Array.Empty<object>(),
                        CustomButtons = Array.Empty<object>(),
                        SidebarButtons = new[]
                        {
                            new MenuBarGroupButton(new MenuAction { ToolTip = "移动" }, "Gizmo")
                            {
                                Image = IconLibrary.Gizmo_MoveIcon,
                            },
                        },
                        TransformTool = new { IsVisible = false },
                    },
                },
            };
            WithWindow(view, 900, _ =>
            {
                var sidebarButton = Descendants<RadioButton>(view)
                    .Single(button => button.GroupName == "Gizmo");
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(sidebarButton.Focusable, Is.False);
                    NUnitAssert.That(System.Windows.Automation.AutomationProperties.GetName(sidebarButton),
                        Is.EqualTo("移动"));
                });
            });
        });
    }

    [TestCase(760)]
    [TestCase(1100)]
    public void SaveOptions_AlignWithTheOutputPath(double width)
    {
        WithTheme(ThemeType.LightTheme, () =>
        {
            var window = new SaveDialogWindow(null!)
            {
                Width = width,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var path = Descendants<TextBox>(window).First();
                var pathLeft = Bounds(path, window).Left;
                var options = Descendants<ComboBox>(window).ToArray();
                NUnitAssert.That(options.Length, Is.EqualTo(4));
                foreach (var option in options)
                    NUnitAssert.That(Bounds(option, window).Left, Is.EqualTo(pathLeft).Within(1));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.DarkTheme)]
    public void MaterialSubgroups_RespectTheirBorderAndBackground(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var material = new MetalRoughView();
            var workspace = new KitbasherView { DataContext = new WorkspaceModel { SceneNodeEditor = new { CurrentEditor = material } } };
            WithWindow(workspace, 900, _ =>
            {
                var subgroups = Descendants<Expander>(material)
                    .Where(expander => expander.ReadLocalValue(Control.BorderThicknessProperty) is Thickness thickness && thickness == new Thickness(0)).ToArray();
                NUnitAssert.That(subgroups, Is.Not.Empty);
                foreach (var subgroup in subgroups)
                {
                    var header = (ToggleButton)subgroup.Template.FindName("HeaderSite", subgroup);
                    NUnitAssert.That(header.BorderThickness, Is.EqualTo(subgroup.BorderThickness));
                    NUnitAssert.That(header.BorderThickness, Is.EqualTo(new Thickness(0)));
                }
            });
        });
    }

    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.DarkTheme)]
    public void PlaybackSlider_UsesTheThemeTemplate(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/Shared.Ui;component/Common/Styles/EditorWorkspaceStyles.xaml") };
            var slider = new Slider { Style = (Style)resources["AeEditor.PlaybackSlider"] };
            WithWindow(slider, 320, _ =>
            {
                NUnitAssert.That(slider.Template, Is.SameAs(Application.Current.FindResource("SliderHorizontal")));
                NUnitAssert.That(slider.Template.FindName("TrackBackground", slider), Is.Not.Null);
            });
        });
    }

    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.DarkTheme)]
    public void TexturePath_ShowsFileNameWhileKeepingTheFullEditablePath(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            const string fullPath = @"variantmeshes\wh_variantmodels\emp\karl\karl_body_base_colour.dds";
            var input = new TextureInput(TextureType.Diffuse) { TexturePath = fullPath };
            var model = new ShaderTextureViewModel(input, null!, null!, null!, null!);
            var view = new ShaderTextureView { DataContext = model };
            WithWindow(view, 280, _ =>
            {
                var editor = Descendants<TextBox>(view).Single();
                var label = (TextBlock)view.FindName("PathFileName");
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(label.Text, Does.EndWith("base_colour.dds"));
                    NUnitAssert.That(label.IsVisible, Is.True);
                    NUnitAssert.That(editor.Text, Is.EqualTo(fullPath));
                    NUnitAssert.That(editor.ToolTip, Is.EqualTo(fullPath));
                    NUnitAssert.That(input.TexturePath, Is.EqualTo(fullPath));
                    NUnitAssert.That(editor.GetBindingExpression(TextBox.TextProperty)!.ParentBinding.UpdateSourceTrigger,
                        Is.EqualTo(UpdateSourceTrigger.LostFocus));
                });
                const string edited = @"textures\edited.dds";
                editor.Text = edited;
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                NUnitAssert.That(model.FileName, Is.EqualTo("edited.dds"));
                NUnitAssert.That(input.TexturePath, Is.EqualTo(edited));
                NUnitAssert.That(label.Text, Is.EqualTo("edited.dds"));
            });
        });
    }

    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.DarkTheme)]
    public void SaveGrid_UsesThemedCheckBoxesInDisplayAndEditModes(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var window = new SaveDialogWindow(null!) { ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var grid = Descendants<DataGrid>(window).Single();
                var row = new SaveCheckBoxRow();
                grid.ItemsSource = new[] { row };
                window.UpdateLayout();
                var themedStyle = window.FindResource("AeInput.CheckBox");
                foreach (var column in grid.Columns.OfType<DataGridCheckBoxColumn>())
                {
                    NUnitAssert.That(column.ElementStyle.BasedOn, Is.SameAs(themedStyle));
                    var displayBounds = Bounds(column.GetCellContent(row), grid);
                    grid.CurrentCell = new DataGridCellInfo(row, column);
                    NUnitAssert.That(grid.BeginEdit(), Is.True);
                    window.UpdateLayout();
                    var editor = (CheckBox)column.GetCellContent(row);
                    NUnitAssert.That(Bounds(editor, grid).Left, Is.EqualTo(displayBounds.Left).Within(1));
                    NUnitAssert.That(editor.IsHitTestVisible, Is.True);
                    grid.CancelEdit();
                    window.UpdateLayout();
                }
            }
            finally { window.Close(); }
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void MaterialSource_SearchIsLiteralAndConfirmationRequiresOneVisibleSource(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var mesh = new GameWorld.Core.SceneNodes.Rmv2MeshNode(null!, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!)
            {
                Name = "body[0]",
                Material = new GameWorld.Core.Rendering.Materials.Shaders.MetalRough.DefaultMaterial(null!),
            };
            using var dialog = new Editors.KitbasherEditor.ChildEditors.MaterialSelection.MaterialSourceWindow();
            dialog.Initialize([mesh], 1);
            var list = (ListBox)dialog.FindName("SourceList");
            var search = (TextBox)dialog.FindName("SearchBox");
            var confirm = (Button)dialog.FindName("ConfirmButton");
            NUnitAssert.That(list.SelectionMode, Is.EqualTo(SelectionMode.Single));
            NUnitAssert.That(confirm.IsEnabled, Is.False);
            search.Text = "[";
            NUnitAssert.That(list.Items.Count, Is.EqualTo(1));
            list.SelectedIndex = 0;
            NUnitAssert.That(dialog.SelectedMesh, Is.SameAs(mesh));
            NUnitAssert.That(confirm.IsEnabled, Is.True);
            search.Text = "no matching mesh";
            NUnitAssert.That(dialog.SelectedMesh, Is.Null);
            NUnitAssert.That(confirm.IsEnabled, Is.False);
            NUnitAssert.That(((TextBlock)dialog.FindName("EmptyMessage")).Visibility, Is.EqualTo(Visibility.Visible));
            search.Text = "";
            NUnitAssert.That(list.Items.Count, Is.EqualTo(1));
            NUnitAssert.That(confirm.IsEnabled, Is.False);
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    public void ShadingPopup_InsideClicksStayOpenAndOutsideClicksClose(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var view = new MenuBarView();
            WithWindow(view, 900, window =>
            {
                var toggle = (ToggleButton)view.FindName("ShadingArrowBtn");
                var popup = (Popup)view.FindName("ShadingPopup");
                var panel = (FrameworkElement)view.FindName("ShadingPanel");
                toggle.IsChecked = true;
                window.UpdateLayout();
                NUnitAssert.That(popup.IsOpen, Is.True);
                var handler = typeof(MenuBarView).GetMethod("HostWindow_PreviewMouseDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var click = new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                {
                    RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
                    Source = Descendants<ComboBox>(panel).First(),
                };
                handler.Invoke(view, [window, click]);
                NUnitAssert.That(popup.IsOpen, Is.True);
                var outside = new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                {
                    RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
                    Source = window,
                };
                handler.Invoke(view, [window, outside]);
                NUnitAssert.That(popup.IsOpen, Is.False);
            });
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void ColourPicker_PopupFollowsThemeAndPreservesColourCallback(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var changed = false;
            var model = new Shared.Ui.BaseDialogs.ColourPickerButton.ColourPickerViewModel(
                Microsoft.Xna.Framework.Vector3.One, _ => changed = true);
            var view = new Shared.Ui.BaseDialogs.ColourPickerButton.ColourPickerButtonView { DataContext = model };
            WithWindow(view, 300, window =>
            {
                var toggle = (ToggleButton)view.FindName("PickerButton");
                var popup = (Popup)view.FindName("PickerPopup");
                var picker = (ColorPicker.StandardColorPicker)view.FindName("Picker");
                toggle.IsChecked = true;
                window.UpdateLayout();
                NUnitAssert.That(popup.IsOpen, Is.True);
                changed = false;
                picker.SelectedColor = Colors.Coral;
                NUnitAssert.That(changed, Is.True);
                NUnitAssert.That(model.PickedColor, Is.EqualTo(Colors.Coral));
                var border = (Border)popup.Child;
                NUnitAssert.That(Contrast(((SolidColorBrush)picker.Foreground).Color,
                    ((SolidColorBrush)border.Background).Color), Is.GreaterThanOrEqualTo(4.5));
                border.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0,
                    System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                NUnitAssert.That(popup.IsOpen, Is.False);
            });
        });
    }

    [Test]
    public void VertexStatistics_AppearInTheWindowAndHandleUvsOutsideTheNormalRange()
    {
        WithTheme(ThemeType.VSCodeLight, () =>
        {
            var events = Moq.Mock.Of<Shared.Core.Events.IEventHub>();
            using var model = new Editors.KitbasherEditor.ChildEditors.VertexDebugger.VertexDebuggerViewModel(
                null!, new GameWorld.Core.Components.Selection.SelectionManager(events), events);
            using var dialog = new Editors.KitbasherEditor.ChildEditors.VertexDebugger.VertexDebuggerWindow(
                model, Moq.Mock.Of<Shared.Core.Services.IWpfGame>());
            model.VertexList.Add(new()
            {
                Uv0 = new Microsoft.Xna.Framework.Vector2(20000, 30000),
                Uv1 = new Microsoft.Xna.Framework.Vector2(-20000, -30000),
            });
            typeof(Editors.KitbasherEditor.ChildEditors.VertexDebugger.VertexDebuggerViewModel)
                .GetMethod("ShowStatistics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(model, null);
            NUnitAssert.That(model.StatisticsText, Does.Contain("20000").And.Contain("-30000"));
            var table = (DataGrid)dialog.FindName("VertexTable");
            NUnitAssert.That(table.AutoGenerateColumns, Is.False);
            NUnitAssert.That(table.HorizontalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto));
            NUnitAssert.That(table.Columns.Count, Is.EqualTo(14));
            model.Refresh();
            NUnitAssert.That(model.StatisticsText, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PackBrowser_ConfirmationFollowsTheRequestedSelectionType(bool foldersOnly)
    {
        WithTheme(ThemeType.VSCodeLight, () =>
        {
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
            var contextMenu = new Mock<IContextMenuBuilder>();
            contextMenu.SetupGet(builder => builder.Type).Returns(ContextMenuType.None);
            contextMenu.Setup(builder => builder.Build(It.IsAny<IReadOnlyList<TreeNode>>())).Returns([]);
            var factory = new PackFileTreeViewFactory(new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                files.Object, Mock.Of<Shared.Core.Events.IEventHub>(), new ContextMenuFactory([contextMenu.Object]));
            using var dialog = new PackFileBrowserWindow(factory, null, false, foldersOnly);
            var confirm = (Button)dialog.FindName("ConfirmButton");
            NUnitAssert.That(confirm.IsEnabled, Is.False);
            dialog.ViewModel.SelectedItem = new TreeNode("pack", NodeType.Root, null!, null);
            NUnitAssert.That(confirm.IsEnabled, Is.False);
            dialog.ViewModel.SelectedItem = new TreeNode("folder", NodeType.Directory, null!, null);
            NUnitAssert.That(confirm.IsEnabled, Is.EqualTo(foldersOnly));
            dialog.ViewModel.SelectedItem = new TreeNode("mesh", NodeType.File, null!, null, new PackFile("mesh", null!));
            NUnitAssert.That(confirm.IsEnabled, Is.EqualTo(!foldersOnly));
            dialog.ViewModel.SelectedItem = null!;
            NUnitAssert.That(confirm.IsEnabled, Is.False);
        });
    }

    [Test]
    public void PinTool_EmptyPreviewWindowCanClose()
    {
        WithTheme(ThemeType.VSCodeLight, () =>
        {
            using var dialog = new Editors.KitbasherEditor.ViewModels.PinTool.PinToolWindow(null!)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -10000,
                Top = -10000,
            };
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    public void MeshFitter_OnlyOffersOneCommitAction(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var scene = new GameWorld.Core.Components.SceneManager(null!, null!, Mock.Of<IEventHub>());
            using var model = new Editors.KitbasherEditor.ChildEditors.MeshFitter.MeshFitterViewModel(null!, null!, scene);
            var dialog = new Editors.KitbasherEditor.ChildEditors.MeshFitter.MeshFitterWindow(model)
            {
                ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000,
            };
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var buttons = Descendants<Button>(dialog).Where(x => x.IsVisible).ToArray();
                NUnitAssert.That(buttons.Count(x => Equals(x.Content, LocalizationManager.Instance.Get("General.Ok"))), Is.EqualTo(1));
                NUnitAssert.That(buttons.Count(x => Equals(x.Content, LocalizationManager.Instance.Get("General.Cancel"))), Is.EqualTo(1));
                NUnitAssert.That(buttons.Any(x => Equals(x.Content, LocalizationManager.Instance.Get("General.Apply"))), Is.False);
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(dialog);
                NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(900));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    public void BoneSearch_ClearButtonFollowsEmptyAndEnteredText(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var model = new Shared.Ui.Editors.BoneMapping.BoneMappingViewModel();
            model.Initialize(new Shared.Ui.Editors.BoneMapping.RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = "current",
                MeshBones = [new(0, "spine")],
                ParnetModelSkeletonName = "target",
                ParentModelBones = [new(0, "spine")],
            });
            var view = new CommonControls.Editors.BoneMapping.View.BoneMappingView { DataContext = model };
            WithWindow(view, 900, window =>
            {
                var buttons = Descendants<Button>(view).Where(button => Equals(button.Content,
                    LocalizationManager.Instance.Get("AnimReTarget.Clear"))).ToArray();
                NUnitAssert.That(buttons, Has.Length.EqualTo(2));
                NUnitAssert.That(buttons.All(button => !button.IsEnabled), Is.True);
                model.MeshBones.Filter = "spine";
                window.UpdateLayout();
                NUnitAssert.That(buttons[0].IsEnabled, Is.True);
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                NUnitAssert.That(model.MeshBones.Filter, Is.Empty);
                NUnitAssert.That(buttons[0].IsEnabled, Is.False);
            });
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void OperationNotice_IsReadableBelowStatisticsAndDoesNotBlockTheViewport(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var hub = CreateFeedbackHub();
            using var feedback = new OperationFeedbackViewModel(hub.Object);
            var executor = new CommandExecutor(hub.Object);
            var model = new WorkspaceModel { OperationFeedback = feedback };
            var scene = (Border)model.Scene;
            scene.Background = Brushes.Gray;
            var view = new KitbasherView { DataContext = model };
            WithWindow(view, 900, window =>
            {
                feedback.Activate();
                executor.ExecuteCommand(CreateFeedbackCommand());
                window.UpdateLayout();
                var notice = (Border)view.FindName("OperationNotice");
                var label = (TextBlock)view.FindName("OperationNoticeText");
                var sceneBounds = Bounds(scene, view);
                var noticeBounds = Bounds(notice, view);
                NUnitAssert.That(feedback.IsVisible, Is.True);
                NUnitAssert.That(label.Text, Is.EqualTo("已完成：场景操作"));
                NUnitAssert.That(noticeBounds.Top, Is.GreaterThan(sceneBounds.Top + sceneBounds.Height / 2));
                NUnitAssert.That(sceneBounds.Contains(noticeBounds), Is.True);
                NUnitAssert.That(Contrast(((SolidColorBrush)label.Foreground).Color,
                    ((SolidColorBrush)notice.Background).Color), Is.GreaterThanOrEqualTo(4.5));
                NUnitAssert.That(view.InputHitTest(new Point(noticeBounds.Left + 8, noticeBounds.Top + 8)), Is.SameAs(scene));
            });
        });
    }

    [Test]
    public void OperationNotice_UsesLatestActionAndClearsOnExpiryDeactivationAndDisposal()
    {
        WithTheme(ThemeType.VSCodeLight, () =>
        {
            var hub = CreateFeedbackHub();
            using var feedback = new OperationFeedbackViewModel(hub.Object);
            var executor = new CommandExecutor(hub.Object);
            executor.ExecuteCommand(CreateFeedbackCommand());
            NUnitAssert.That(feedback.IsVisible, Is.False);
            feedback.Activate();
            executor.ExecuteCommand(CreateFeedbackCommand());
            NUnitAssert.That(feedback.Text, Is.EqualTo("已完成：场景操作"));
            executor.Undo();
            NUnitAssert.That(feedback.Text, Is.EqualTo("已撤销：场景操作"));
            executor.Redo();
            NUnitAssert.That(feedback.Text, Is.EqualTo("已重做：场景操作"));

            var frame = new System.Windows.Threading.DispatcherFrame();
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.3) };
            timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
            timeout.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            NUnitAssert.That(feedback.IsVisible, Is.False);
            NUnitAssert.That(feedback.Text, Is.Empty);

            executor.ExecuteCommand(CreateFeedbackCommand());
            feedback.Deactivate();
            NUnitAssert.That(feedback.IsVisible, Is.False);
            executor.Undo();
            NUnitAssert.That(feedback.IsVisible, Is.False);
            feedback.Activate();
            executor.Redo();
            feedback.Dispose();
            executor.ExecuteCommand(CreateFeedbackCommand());
            NUnitAssert.That(feedback.IsVisible, Is.False);
            hub.Verify(value => value.UnRegister(feedback), Times.Once);
        });
    }

    [Test]
    public void OperationDescriptions_CoverExistingSceneCommandsWithoutShowingImplementationNames()
    {
        WithTheme(ThemeType.VSCodeLight, () =>
        {
            var assemblies = new[] { typeof(GameWorld.Core.Commands.Object.ObjectSelectionCommand).Assembly,
                typeof(OperationFeedbackViewModel).Assembly };
            var commands = assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(type => !type.IsAbstract && typeof(GameWorld.Core.Commands.ICommand).IsAssignableFrom(type))
                .ToArray();
            foreach (var type in commands)
            {
                var description = OperationFeedbackViewModel.GetDescription(type);
                NUnitAssert.That(description, Is.Not.EqualTo("场景操作"), type.FullName);
                NUnitAssert.That(description, Does.Not.Contain("Kitbash."), type.FullName);
                NUnitAssert.That(description.Any(character => character >= '\u4e00' && character <= '\u9fff'), Is.True, type.FullName);
            }
            NUnitAssert.That(OperationFeedbackViewModel.GetDescription(null), Is.EqualTo("场景操作"));
        });
    }


    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void SidebarBoneTables_RenderNamesAndIndexesInsteadOfObjectTypes(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var views = new UserControl[]
            {
                new KitbasherEditor.Views.EditorViews.MainEditableNodeView
                {
                    DataContext = new
                    {
                        AttachmentPointList = new[] { new { BoneIndex = 17, Name = "bip_spine_1", IsIdentiy = true } },
                    },
                },
                new KitbasherEditor.Views.EditorViews.Rmv2.AnimationView
                {
                    DataContext = new
                    {
                        Animation = new
                        {
                            AnimatedBones = new[] { new Shared.Ui.Editors.BoneMapping.AnimatedBone(17, "bip_spine_1") },
                        },
                    },
                },
            };
            foreach (var view in views)
            {
                view.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/{typeof(KitbasherView).Assembly.GetName().Name};component/KitbashUiStyles.xaml"),
                });
                WithWindow(view, 400, window =>
                {
                    for (var depth = 0; depth < 3; depth++)
                    {
                        foreach (var expander in Descendants<Expander>(view))
                            expander.IsExpanded = true;
                        window.UpdateLayout();
                    }
                    var table = Descendants<ItemsControl>(view).First(item => item is ListView or DataGrid);
                    var labels = Descendants<TextBlock>(table).Select(label => label.Text).ToArray();
                    NUnitAssert.That(labels, Does.Contain("bip_spine_1"), view.GetType().Name);
                    NUnitAssert.That(labels, Does.Contain("17"), view.GetType().Name);
                    NUnitAssert.That(labels, Has.None.Contains("AnimatedBone"), view.GetType().Name);
                });
            }
        });
    }

    [TestCase(ThemeType.VSCodeDark)]
    [TestCase(ThemeType.VSCodeLight)]
    public void AttachmentCombo_FilterSelectAndUndoPreserveModelAndVisibleSelection(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var hub = Mock.Of<IEventHub>();
            var executor = new CommandExecutor(hub);
            var editor = new Editors.KitbasherEditor.ViewModels.SceneNodeEditor.SceneNodePropertyEditor(executor);
            var root = new KitbasherRootScene(new GameWorld.Core.Components.AnimationsContainerComponent(),
                Mock.Of<IPackFileService>(), hub);
            var geometry = new GameWorld.Core.Rendering.Geometry.MeshObject(
                Mock.Of<GameWorld.Core.Rendering.Geometry.IGraphicsCardGeometry>(), "missing_skeleton")
            {
                VertexArray = [],
                IndexArray = [],
            };
            geometry.ChangeVertexType(Shared.GameFormats.RigidModel.UiVertexFormat.Weighted);
            var mesh = new GameWorld.Core.SceneNodes.Rmv2MeshNode(geometry,
                new Shared.GameFormats.RigidModel.MaterialHeaders.WeightedMaterial { ModelName = "Mesh" }, null!, null!);
            var model = new GameWorld.Core.SceneNodes.MainEditableNode("Model",
                new GameWorld.Core.SceneNodes.SkeletonNode(null), Mock.Of<IPackFileService>());
            model.AddObject(new GameWorld.Core.SceneNodes.Rmv2LodNode("Lod 0", 0)).AddObject(mesh);
            using var animation = new Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2.AnimationViewModel(
                root, Mock.Of<GameWorld.Core.Services.ISkeletonAnimationLookUpHelper>(), editor);
            animation.Initialize(mesh);
            var none = animation.AttachableBones.SelectedItem!;
            var bone = new Shared.Ui.Editors.BoneMapping.AnimatedBone(7, "bip_spine_1");
            animation.AttachableBones.PossibleValues.Add(bone);
            animation.AttachableBones.Values.Add(bone);
            var view = new KitbasherEditor.Views.EditorViews.Rmv2.AnimationView { DataContext = new { Animation = animation } };
            view.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{typeof(KitbasherView).Assembly.GetName().Name};component/KitbashUiStyles.xaml"),
            });
            WithWindow(view, 400, window =>
            {
                Descendants<Expander>(view).First().IsExpanded = true;
                window.UpdateLayout();
                var combo = Descendants<ComboBox>(view).Single();
                NUnitAssert.That(combo.Text, Is.EqualTo("无"));
                NUnitAssert.That(combo.Items.Count, Is.EqualTo(2));
                combo.IsDropDownOpen = true;
                combo.SetCurrentValue(ComboBox.TextProperty, "spine");
                window.UpdateLayout();
                NUnitAssert.That(combo.Items.Cast<object>(), Is.EqualTo(new[] { bone }));
                NUnitAssert.That(executor.CurrentDocumentStateId, Is.Zero);
                combo.SetCurrentValue(Selector.SelectedItemProperty, bone);
                combo.IsDropDownOpen = false;
                window.UpdateLayout();
                NUnitAssert.That(mesh.AttachmentPointName, Is.EqualTo(bone.Name.Value));
                NUnitAssert.That(combo.Text, Is.EqualTo(bone.Name.Value));
                NUnitAssert.That(combo.Items.Count, Is.EqualTo(2));

                executor.Undo();
                window.UpdateLayout();
                NUnitAssert.That(mesh.AttachmentPointName, Is.Null.Or.Empty);
                NUnitAssert.That(combo.SelectedItem, Is.SameAs(none));
                NUnitAssert.That(combo.Text, Is.EqualTo("无"));
                NUnitAssert.That(combo.Items.Count, Is.EqualTo(2));
                NUnitAssert.That(executor.CurrentDocumentStateId, Is.Zero);
            });
        });
    }

    private static Mock<IEventHub> CreateFeedbackHub()
    {
        var hub = new Mock<IEventHub>();
        Action<CommandStackChangedEvent>? changed = null;
        Action<CommandStackUndoEvent>? undone = null;
        hub.Setup(value => value.Register(It.IsAny<object>(), It.IsAny<Action<CommandStackChangedEvent>>()))
            .Callback<object, Action<CommandStackChangedEvent>>((_, action) => changed = action);
        hub.Setup(value => value.Register(It.IsAny<object>(), It.IsAny<Action<CommandStackUndoEvent>>()))
            .Callback<object, Action<CommandStackUndoEvent>>((_, action) => undone = action);
        hub.Setup(value => value.Publish(It.IsAny<CommandStackChangedEvent>()))
            .Callback<CommandStackChangedEvent>(notification => changed?.Invoke(notification));
        hub.Setup(value => value.Publish(It.IsAny<CommandStackUndoEvent>()))
            .Callback<CommandStackUndoEvent>(notification => undone?.Invoke(notification));
        return hub;
    }

    private static GameWorld.Core.Commands.ICommand CreateFeedbackCommand()
    {
        var command = new Mock<GameWorld.Core.Commands.ICommand>();
        command.SetupGet(value => value.IsMutation).Returns(true);
        command.SetupGet(value => value.HintText).Returns("Internal English command");
        return command.Object;
    }

    private static void WithTheme(ThemeType theme, Action action)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            if (LocalizationManager.Instance == null)
            {
                var localization = new LocalizationManager();
                localization.LoadLanguage();
            }
            try
            {
                ThemesController.SetTheme(theme);
                if (IconLibrary.SaveFileIcon == null)
                    IconLibrary.Load();
                Application.Current.Resources["BoolToCollapsedConverter"] = new BoolToVisibilityConverter
                {
                    TrueValue = Visibility.Visible,
                    FalseValue = Visibility.Collapsed,
                };
                Application.Current.Resources["ViewTemplateDataSelector"] = new ViewTemplateDataSelector();
                action();
            }
            finally
            {
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    private static void WithWindow(UIElement content, double width, Action<Window> action)
    {
        var window = new Window
        {
            Style = new Style(typeof(Window)),
            Content = content,
            Width = width,
            Height = 300,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -10000,
            Top = -10000,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            action(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static IEnumerable<ThemeType> Themes() => Enum.GetValues<ThemeType>();

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private sealed class SaveCheckBoxRow
    {
        public int LodIndex => 0;
        public bool OptimizeLod_Alpha { get; set; }
        public bool OptimizeLod_Vertex { get; set; }
    }

    private sealed class ToolbarSettings
    {
        public NotifyAttr<bool> IsVisible { get; } = new(true);
        public bool IsEnabled { get; set; }
        public bool IsWireframe { get; set; }
        public bool IsSolid { get; set; }
        public bool IsMaterialPreview { get; set; } = true;
        public System.Windows.Media.Imaging.BitmapImage CurrentIcon => IconLibrary.ProportionalOffIcon;
    }

    private sealed class WorkspaceModel
    {
        public GridLength LeftColumnWidth { get; set; } = new(3, GridUnitType.Star);
        public GridLength RightColumnWidth { get; set; } = new(1, GridUnitType.Star);
        public object Scene { get; } = new Border();
        public object? MenuBar { get; set; }
        public object? SceneNodeEditor { get; set; }
        public object? OperationFeedback { get; set; }
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
}
