using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs;
using NUnitAssert = NUnit.Framework.Assert;
using ShapePath = System.Windows.Shapes.Path;

namespace AssetEditorTests;

[NonParallelizable]
public class UiCommonControlResourceTests
{
    private static readonly IReadOnlyDictionary<string, Type> ExpectedStyles =
        new Dictionary<string, Type>
        {
            ["AeButton.Primary"] = typeof(Button),
            ["AeButton.Secondary"] = typeof(Button),
            ["AeButton.Quiet"] = typeof(Button),
            ["AeButton.Danger"] = typeof(Button),
            ["AeButton.Icon"] = typeof(Button),
            ["AeButton.DropdownArrow"] = typeof(ToggleButton),
            ["AeButton.VisibilityToggle"] = typeof(ToggleButton),
            ["AeInput.TextBox"] = typeof(TextBox),
            ["AeInput.ComboBox"] = typeof(ComboBox),
            ["AeInput.CheckBox"] = typeof(CheckBox),
            ["AeInput.RadioButton"] = typeof(RadioButton),
            ["AeInput.Switch"] = typeof(ToggleButton),
            ["AeInput.ExpandableField"] = typeof(Expander),
            ["AeForm.Label"] = typeof(Label),
            ["AeForm.TextLabel"] = typeof(TextBlock),
            ["AeValidation.Message"] = typeof(TextBlock),
            ["AeTab.Item"] = typeof(TabItem),
            ["AeTag.Container"] = typeof(Border),
            ["AeTag.Text"] = typeof(TextBlock),
            ["AeTree.View"] = typeof(TreeView),
            ["AeTree.Item"] = typeof(TreeViewItem),
            ["AeList.View"] = typeof(ListBox),
            ["AeList.Item"] = typeof(ListBoxItem),
            ["AeTable.Grid"] = typeof(DataGrid),
            ["AeTable.Header"] = typeof(DataGridColumnHeader),
            ["AeTable.Row"] = typeof(DataGridRow),
            ["AeTable.Cell"] = typeof(DataGridCell),
            ["AeMenu.Bar"] = typeof(Menu),
            ["AeMenu.Item"] = typeof(MenuItem),
            ["AeMenu.Context"] = typeof(ContextMenu),
            ["AeToolTip"] = typeof(ToolTip),
            ["AeFeedback.Notice"] = typeof(Border),
            ["AeFeedback.Icon"] = typeof(Border),
            ["AeFeedback.SuccessIcon"] = typeof(Border),
            ["AeFeedback.WarningIcon"] = typeof(Border),
            ["AeFeedback.DangerIcon"] = typeof(Border),
            ["AeEmptyState.Panel"] = typeof(Border),
            ["AeEmptyState.Title"] = typeof(TextBlock),
            ["AeEmptyState.Description"] = typeof(TextBlock),
            ["AeProgress.Bar"] = typeof(ProgressBar),
            ["AeScrollBar.Compact"] = typeof(ScrollBar),
        };

    [Test]
    public void CommonControlDictionaries_ExposeApprovedKeyedStylesOnly()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var dictionaries = LoadDictionaries();

                NUnitAssert.Multiple(() =>
                {
                    foreach (var pair in ExpectedStyles)
                    {
                        var style = dictionaries
                            .Select(dictionary => dictionary[pair.Key])
                            .OfType<Style>()
                            .SingleOrDefault();
                        NUnitAssert.That(style, Is.Not.Null, pair.Key);
                        NUnitAssert.That(
                            style!.TargetType,
                            Is.EqualTo(pair.Value),
                            pair.Key);
                    }

                    foreach (var dictionary in dictionaries)
                    {
                        NUnitAssert.That(
                            dictionary.Keys.OfType<Type>(),
                            Is.Empty,
                            dictionary.Source?.OriginalString);
                    }
                });
            });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void CommonControlStyles_InstantiateInRequiredThemes(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                try
                {
                    ThemesController.SetTheme(theme);
                    var controls = ExpectedStyles.Select(pair =>
                    {
                        var control = (FrameworkElement)Activator.CreateInstance(
                            pair.Value)!;
                        control.Style = (Style)Application.Current.FindResource(
                            pair.Key);
                        return control;
                    }).ToArray();
                    var panel = new StackPanel();
                    foreach (var control in controls)
                    {
                        if (control is ContextMenu or ToolTip)
                        {
                            control.ApplyTemplate();
                            continue;
                        }

                        panel.Children.Add(control);
                    }

                    panel.Measure(new Size(800, double.PositiveInfinity));
                    panel.Arrange(new Rect(panel.DesiredSize));
                    panel.UpdateLayout();

                    NUnitAssert.That(
                        controls.Select(control => control.ActualHeight),
                        Has.All.GreaterThanOrEqualTo(0));
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void TreeChevron_UsesFixedCenteredVectorBox()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var item = new TreeViewItem
                {
                    Header = "帝国将军.pack",
                    Style = (Style)Application.Current.FindResource(
                        "AeTree.Item"),
                };
                var window = new Window
                {
                    Content = item,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    item.ApplyTemplate();
                    var host = (FrameworkElement)item.Template.FindName(
                        "PART_ChevronHost",
                        item);
                    var path = FindDescendant<ShapePath>(host);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(host.Width, Is.EqualTo(20));
                        NUnitAssert.That(
                            host.Height,
                            Is.EqualTo((double)Application.Current.FindResource(
                                "AeSize.CompactRowHeight")));
                        NUnitAssert.That(
                            host.VerticalAlignment,
                            Is.EqualTo(VerticalAlignment.Center));
                        NUnitAssert.That(path, Is.Not.Null);
                        NUnitAssert.That(
                            path!.VerticalAlignment,
                            Is.EqualTo(VerticalAlignment.Center));
                        NUnitAssert.That(
                            path.HorizontalAlignment,
                            Is.EqualTo(HorizontalAlignment.Center));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void FeedbackIcon_IsCenteredAgainstWholeNotice()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var icon = new Border
                {
                    Style = (Style)Application.Current.FindResource(
                        "AeFeedback.SuccessIcon"),
                };

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(icon.Width, Is.EqualTo(17));
                    NUnitAssert.That(icon.Height, Is.EqualTo(17));
                    NUnitAssert.That(
                        icon.VerticalAlignment,
                        Is.EqualTo(VerticalAlignment.Center));
                });
            });
    }

    [Test]
    public void ComboBox_DisplaysConfiguredMemberForSelectedObject()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var option = new NamedOption("master");
                var comboBox = new ComboBox
                {
                    Width = 240,
                    ItemsSource = new[] { option },
                    SelectedItem = option,
                    DisplayMemberPath = nameof(NamedOption.Name),
                    Style = (Style)Application.Current.FindResource(
                        "AeInput.ComboBox"),
                };
                var window = new Window
                {
                    Content = comboBox,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var visibleText = FindDescendants<TextBlock>(comboBox)
                        .Select(text => text.Text)
                        .ToArray();

                    NUnitAssert.That(visibleText, Does.Contain("master"));
                    NUnitAssert.That(
                        visibleText,
                        Has.None.Contains(nameof(NamedOption)));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void TextBox_PreservesEstablishedEditableTemplate()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var style = (Style)Application.Current.FindResource(
                    "AeInput.TextBox");
                var establishedStyle = (Style)Application.Current.FindResource(
                    typeof(TextBox));
                var textBox = new TextBox { Style = style };
                var peer = new TextBoxAutomationPeer(textBox);
                var valueProvider = (IValueProvider)peer.GetPattern(
                    PatternInterface.Value)!;
                valueProvider.SetValue("帝国将军");

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(style.BasedOn, Is.SameAs(establishedStyle));
                    NUnitAssert.That(textBox.Focusable, Is.True);
                    NUnitAssert.That(textBox.IsReadOnly, Is.False);
                    NUnitAssert.That(textBox.MinHeight, Is.EqualTo(26));
                    NUnitAssert.That(
                        textBox.VerticalContentAlignment,
                        Is.EqualTo(VerticalAlignment.Center));
                    NUnitAssert.That(textBox.Text, Is.EqualTo("帝国将军"));
                });
            });
    }

    [Test]
    public void ComboBox_UsesEntireSurfaceAsDropDownToggle()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var comboBox = new ComboBox
                {
                    Width = 240,
                    Style = (Style)Application.Current.FindResource(
                        "AeInput.ComboBox"),
                };
                comboBox.Items.Add("战锤 III");
                var window = new Window
                {
                    Width = 300,
                    Height = 100,
                    Content = comboBox,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var toggle = FindDescendants<ToggleButton>(comboBox)
                        .Single();
                    toggle.IsChecked = true;

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(comboBox.MinHeight, Is.EqualTo(26));
                        NUnitAssert.That(
                            toggle.ActualWidth,
                            Is.EqualTo(comboBox.ActualWidth).Within(1));
                        NUnitAssert.That(comboBox.IsDropDownOpen, Is.True);
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void ComboBox_UsesGlyphOnlyAnimatedDropDownArrow()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var comboBox = new ComboBox
                {
                    Width = 240,
                    Style = (Style)Application.Current.FindResource(
                        "AeInput.ComboBox"),
                };
                comboBox.Items.Add("战锤 III");
                var window = new Window
                {
                    Width = 300,
                    Height = 100,
                    Content = comboBox,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var toggle = FindDescendants<ToggleButton>(comboBox)
                        .Single();
                    var arrow = FindDescendants<ShapePath>(toggle).Single();
                    var arrowHost = (Border)VisualTreeHelper.GetParent(arrow);
                    var hoverTrigger = arrow.Style.Triggers
                        .OfType<Trigger>()
                        .Single(trigger =>
                            trigger.Property == UIElement.IsMouseOverProperty);
                    var animations = hoverTrigger.EnterActions
                        .OfType<BeginStoryboard>()
                        .SelectMany(action => action.Storyboard.Children)
                        .OfType<DoubleAnimation>()
                        .ToArray();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            FindDescendants<System.Windows.Shapes.Ellipse>(
                                toggle),
                            Is.Empty);
                        NUnitAssert.That(arrowHost.Width, Is.EqualTo(20));
                        NUnitAssert.That(
                            arrowHost.Background,
                            Is.EqualTo(Brushes.Transparent));
                        NUnitAssert.That(animations, Is.Not.Empty);
                        NUnitAssert.That(
                            animations.Select(animation =>
                                animation.Duration.TimeSpan),
                            Does.Contain(TimeSpan.FromMilliseconds(120)));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void TextOnlyMenu_DoesNotReserveAnIconRail()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var itemStyle = (Style)Application.Current.FindResource(
                    "AeMenu.Item");
                var rootItem = new MenuItem
                {
                    Header = "文件",
                    Style = itemStyle,
                };
                rootItem.Items.Add(new MenuItem
                {
                    Header = "新建 Pack",
                    Style = itemStyle,
                });
                var menu = new Menu
                {
                    Style = (Style)Application.Current.FindResource(
                        "AeMenu.Bar"),
                };
                menu.Items.Add(rootItem);
                var window = new Window
                {
                    Content = menu,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var templateRoot = (FrameworkElement)rootItem.Template
                        .LoadContent();
                    var reservedRails = FindDescendants<FrameworkElement>(
                            templateRoot)
                        .Where(element =>
                            element is Canvas or Border &&
                            element.Width is >= 22 and <= 23)
                        .ToArray();
                    var contextMenu = new ContextMenu
                    {
                        Style = (Style)Application.Current.FindResource(
                            "AeMenu.Context"),
                    };
                    rootItem.IsSubmenuOpen = true;
                    var popup = (Popup)rootItem.Template.FindName(
                        "PART_Popup",
                        rootItem);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(reservedRails, Is.Empty);
                        NUnitAssert.That(rootItem.MinHeight, Is.EqualTo(24));
                        NUnitAssert.That(
                            contextMenu.Padding,
                            Is.EqualTo(new Thickness(2)));
                        NUnitAssert.That(popup.IsOpen, Is.True);
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void SecondaryButton_PressUsesScaleWithoutAccentOutline()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var style = (Style)Application.Current.FindResource(
                    "AeButton.Secondary");
                var template = (ControlTemplate)FindSetter(
                    style,
                    Control.TemplateProperty)!.Value;
                var pressedVisual = style.Triggers
                    .OfType<Trigger>()
                    .Single(item =>
                        item.Property == ButtonBase.IsPressedProperty);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        FindSetter(
                            style,
                            FrameworkElement.FocusVisualStyleProperty)
                            ?.Value,
                        Is.Null);
                    NUnitAssert.That(
                        pressedVisual.Setters
                            .OfType<Setter>()
                            .Any(item =>
                                item.Property ==
                                Control.BorderBrushProperty),
                        Is.False);
                });
                AssertPressReleaseMotion(template);
            });
    }

    [Test]
    public void SecondaryButton_ShrinksWhilePressedAndReboundsOnRelease()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var button = new PressStateTestButton
                {
                    Content = "Test",
                    Style = (Style)Application.Current.FindResource(
                        "AeButton.Secondary"),
                };
                var window = CreateOffscreenWindow(button);
                try
                {
                    window.Show();
                    button.ApplyTemplate();
                    window.UpdateLayout();
                    var scale = GetInteractionScale(button);

                    button.SetPressed(true);
                    PumpDispatcher(TimeSpan.FromMilliseconds(90));
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            scale.ScaleX,
                            Is.EqualTo(0.985).Within(0.001));
                        NUnitAssert.That(
                            scale.ScaleY,
                            Is.EqualTo(0.985).Within(0.001));
                    });

                    PumpDispatcher(TimeSpan.FromMilliseconds(150));
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            scale.ScaleX,
                            Is.EqualTo(0.985).Within(0.001));
                        NUnitAssert.That(
                            scale.ScaleY,
                            Is.EqualTo(0.985).Within(0.001));
                    });

                    button.SetPressed(false);
                    PumpDispatcher(TimeSpan.FromMilliseconds(150));
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            scale.ScaleX,
                            Is.EqualTo(1).Within(0.001));
                        NUnitAssert.That(
                            scale.ScaleY,
                            Is.EqualTo(1).Within(0.001));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void PublicButtons_UsePressAndReleaseMotion()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                static Style Style(object key) =>
                    (Style)Application.Current.FindResource(key);
                var editorStyles = Load(
                    "Shared.Ui",
                    "Common/Styles/EditorWorkspaceStyles.xaml");
                var optionalRadioStyles = Load(
                    "Shared.Ui",
                    "BaseDialogs/OptionalRadioButtonStyle.xaml");
                var gitStyles = Load(
                    "Views/FolderProjectVersionControl/FolderProjectGitStyles.xaml");

                var controls = new (string Name, ButtonBase Control)[]
                {
                    ("design button", new Button
                    {
                        Content = "Test",
                        Style = Style("AeButton.Secondary"),
                    }),
                    ("legacy button", new Button
                    {
                        Content = "Test",
                        Style = Style(typeof(Button)),
                    }),
                    ("legacy toggle", new ToggleButton
                    {
                        Content = "Test",
                        Style = Style(typeof(ToggleButton)),
                    }),
                    ("legacy radio", new RadioButton
                    {
                        Content = "Test",
                        Style = Style(typeof(RadioButton)),
                    }),
                    ("dropdown arrow", new ToggleButton
                    {
                        Style = Style("AeButton.DropdownArrow"),
                    }),
                    ("switch", new ToggleButton
                    {
                        Style = Style("AeInput.Switch"),
                    }),
                    ("radio input", new RadioButton
                    {
                        Content = "Test",
                        Style = Style("AeInput.RadioButton"),
                    }),
                    ("title bar", new Button
                    {
                        Content = "Test",
                        Style = Style("TitleBarButtonStyle"),
                    }),
                    ("disabled-background button", new Button
                    {
                        Content = "Test",
                        Style = Style("NoBackgroundOnDisabledButton"),
                    }),
                    ("toolbar button", new Button
                    {
                        Content = "Test",
                        Style = Style("ToolBarButtonBaseStyle"),
                    }),
                    ("toolbar overflow", new ToggleButton
                    {
                        Style = Style("ToolBarHorizontalOverflowButtonStyle"),
                    }),
                    ("editor toggle", new ToggleButton
                    {
                        Content = "Test",
                        Style = (Style)editorStyles["AeEditor.ToggleIcon"],
                    }),
                    ("editor playback", new ToggleButton
                    {
                        Content = "Test",
                        Style = (Style)editorStyles["AeEditor.PlaybackToggle"],
                    }),
                    ("optional radio", new OptionalRadioButton
                    {
                        Content = "Test",
                        Style = (Style)optionalRadioStyles[
                            "OptionalRadioButtonStyle"],
                    }),
                    ("git split button", new Button
                    {
                        Content = "Test",
                        Style = (Style)gitStyles[
                            "GitCommitSplitButtonPartStyle"],
                    }),
                };

                foreach (var (name, control) in controls)
                {
                    var template = (ControlTemplate)FindSetter(
                        control.Style,
                        Control.TemplateProperty)!.Value;
                    AssertPressReleaseMotion(template, name);
                }
            });
    }

    [Test]
    public void PublicButtonTemplates_UsePressAndReleaseMotionAcrossFamilies()
    {
        var root = FindSolutionRoot();
        var paths = new[]
        {
            "AssetEditor/Themes/Controls.xaml",
            "AssetEditor/Themes/DesignSystem/Controls/Buttons.xaml",
            "AssetEditor/Themes/DesignSystem/Controls/Inputs.xaml",
            "Shared/SharedUI/Common/Styles/EditorWorkspaceStyles.xaml",
            "Shared/SharedUI/BaseDialogs/OptionalRadioButtonStyle.xaml",
            "AssetEditor/Views/FolderProjectVersionControl/FolderProjectGitStyles.xaml",
            "Editors/Kitbashing/KitbasherEditor/KitbashUiStyles.xaml",
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(
                root,
                path.Replace('/', Path.DirectorySeparatorChar)));
            var scaleCount = source.Split(
                "x:Name=\"InteractionScale\"",
                StringSplitOptions.None).Length - 1;
            var pressCount = source.Split(
                "AeMotion.ButtonPressStoryboard",
                StringSplitOptions.None).Length - 1;
            var releaseCount = source.Split(
                "AeMotion.ButtonReleaseStoryboard",
                StringSplitOptions.None).Length - 1;

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(scaleCount, Is.GreaterThan(0), path);
                NUnitAssert.That(pressCount, Is.EqualTo(scaleCount), path);
                NUnitAssert.That(releaseCount, Is.EqualTo(scaleCount), path);
                NUnitAssert.That(
                    source,
                    Does.Not.Contain("ButtonBase.Click"),
                    path);
                NUnitAssert.That(
                    source,
                    Does.Not.Contain("To=\"0.94\""),
                    path);
            });
        }
    }

    [Test]
    public void Buttons_HighlightOnHoverAndAnimatePressWithoutFocusRings()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var style = (Style)Application.Current.FindResource(
                    "AeButton.Secondary");
                var template = FindSetter(style, Control.TemplateProperty)
                    ?.Value as ControlTemplate;
                var root = (FrameworkElement)template!.LoadContent();
                var group = VisualStateManager.GetVisualStateGroups(root)
                    .OfType<VisualStateGroup>()
                    .Single(item => item.Name == "CommonStates");
                var pressed = group.States
                    .Cast<VisualState>()
                    .Single(item => item.Name == "Pressed");
                var hoverTrigger = style.Triggers
                    .OfType<Trigger>()
                    .Single(item =>
                        item.Property == UIElement.IsMouseOverProperty);
                var focusVisual = FindSetter(
                    style,
                    FrameworkElement.FocusVisualStyleProperty)?.Value as Style;
                var persistentFocusRings = FindDescendants<Border>(root)
                    .Where(item => item.Name == "FocusRing")
                    .ToArray();
                var translucentStateOverlays = FindDescendants<Border>(root)
                    .Where(item => item.Name == "StateOverlay")
                    .ToArray();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(pressed.Storyboard, Is.Null);
                    NUnitAssert.That(
                        hoverTrigger.Setters
                            .OfType<Setter>()
                            .Any(item =>
                                item.Property ==
                                Control.BackgroundProperty),
                        Is.True);
                    NUnitAssert.That(focusVisual, Is.Null);
                    NUnitAssert.That(persistentFocusRings, Is.Empty);
                    NUnitAssert.That(translucentStateOverlays, Is.Empty);
                });
                AssertPressReleaseMotion(template);
            });
    }

    [Test]
    public void LegacyButtonBases_UsePressReleaseMotionWithoutFocusRings()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var styles = new (string Name, Style Style)[]
                {
                    ("button", (Style)Application.Current.FindResource(
                        typeof(Button))),
                    ("toggle", (Style)Application.Current.FindResource(
                        typeof(ToggleButton))),
                    ("radio", (Style)Application.Current.FindResource(
                        typeof(RadioButton))),
                    ("disabled-background", (Style)Application.Current
                        .FindResource("NoBackgroundOnDisabledButton")),
                    ("toolbar", (Style)Application.Current.FindResource(
                        "ToolBarButtonBaseStyle")),
                };

                foreach (var (name, style) in styles)
                {
                    var focusVisualSetter = FindSetter(
                        style,
                        FrameworkElement.FocusVisualStyleProperty);
                    var template = FindSetter(
                            style,
                            Control.TemplateProperty)
                        ?.Value as ControlTemplate;
                    var root = (FrameworkElement)template!.LoadContent();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            FindDescendants<Border>(root)
                                .Where(item => item.Name == "StateOverlay"),
                            Is.Empty);
                        NUnitAssert.That(focusVisualSetter, Is.Not.Null);
                        NUnitAssert.That(focusVisualSetter!.Value, Is.Null);
                        NUnitAssert.That(
                            template.Triggers
                                .OfType<Trigger>()
                                .Where(item =>
                                    item.Property ==
                                    ButtonBase.IsPressedProperty)
                                .SelectMany(item => item.Setters
                                    .OfType<Setter>())
                                .Any(item =>
                                    item.Property ==
                                    Control.BorderBrushProperty),
                            Is.False,
                            name);
                    });
                    AssertPressReleaseMotion(template, name);
                }
            });
    }

    [Test]
    public void EditorWorkspace_UsesTheGlobalGlyphOnlyExpanderTemplate()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var dictionary = Load(
                    "Shared.Ui",
                    "Common/Styles/EditorWorkspaceStyles.xaml");
                var style = (Style)dictionary[typeof(Expander)];
                var template = FindSetter(
                        style,
                        Control.TemplateProperty)
                    ?.Value as ControlTemplate;

                NUnitAssert.That(template, Is.Not.Null);
                var expander = new Expander
                {
                    Style = style,
                    Header = "Header",
                    Content = new TextBlock { Text = "Content" },
                    IsExpanded = true,
                };
                expander.Measure(new Size(320, 200));
                expander.Arrange(new Rect(0, 0, 320, 200));
                expander.ApplyTemplate();
                expander.UpdateLayout();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(style.BasedOn, Is.Not.Null);
                    NUnitAssert.That(
                        FindDescendants<ShapePath>(expander),
                        Is.Not.Empty);
                    NUnitAssert.That(
                        FindDescendants<System.Windows.Shapes.Ellipse>(
                            expander),
                        Is.Empty);
                });
            });
    }

    [Test]
    public void EditorToggleButtons_UsePressReleaseMotionWithoutFocusFrames()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var kitbash = Load(
                    "Editors.KitbasherEditor",
                    "KitbashUiStyles.xaml");
                var controls = new (string Name, ButtonBase Control)[]
                {
                    ("kitbash tool", new RadioButton
                    {
                        Content = "Test",
                        Style = (Style)kitbash["Kitbash.ToolRadioButton"],
                    }),
                    ("switch", new ToggleButton
                    {
                        Style = (Style)Application.Current.FindResource(
                            "AeInput.Switch"),
                    }),
                };

                foreach (var (name, control) in controls)
                {
                    var style = control.Style;
                    var template = (ControlTemplate)FindSetter(
                        style,
                        Control.TemplateProperty)!.Value;
                    var root = (FrameworkElement)template.LoadContent();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            FindSetter(
                                style,
                                FrameworkElement.FocusVisualStyleProperty)
                                ?.Value,
                            Is.Null);
                        NUnitAssert.That(
                            FindDescendants<Border>(root)
                                .Where(item => item.Name == "FocusRing"),
                            Is.Empty);
                    });
                    AssertPressReleaseMotion(template, name);
                }
            });
    }

    [Test]
    public void DropDownArrows_AreGlyphOnlyAcrossSharedTemplates()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Themes",
            "Controls.xaml"));
        var toolbarStart = source.IndexOf(
            "x:Key=\"ToolBarVerticalOverflowButtonStyle\"",
            StringComparison.Ordinal);
        var toolbarEnd = source.IndexOf(
            "x:Key=\"ToolBarThumbStyle\"",
            StringComparison.Ordinal);
        var toolbarArrows = source[toolbarStart..toolbarEnd];
        var watermarkStart = source.IndexOf(
            "x:Key=\"WatermarkComboBoxTemplate\"",
            StringComparison.Ordinal);
        var watermarkEnd = source.IndexOf(
            "x:Key=\"WatermarkComboBox\"",
            watermarkStart,
            StringComparison.Ordinal);
        var watermarkComboBox = source[watermarkStart..watermarkEnd];

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(toolbarStart, Is.GreaterThanOrEqualTo(0));
            NUnitAssert.That(toolbarEnd, Is.GreaterThan(toolbarStart));
            NUnitAssert.That(toolbarArrows, Does.Contain("x:Name=\"Chevron\""));
            NUnitAssert.That(toolbarArrows, Does.Contain("AeBrush.AccentHover"));
            NUnitAssert.That(toolbarArrows, Does.Not.Contain("ToolBarButtonHover"));
            NUnitAssert.That(toolbarArrows, Does.Not.Contain("CornerRadius"));
            NUnitAssert.That(watermarkStart, Is.GreaterThanOrEqualTo(0));
            NUnitAssert.That(watermarkEnd, Is.GreaterThan(watermarkStart));
            NUnitAssert.That(
                watermarkComboBox,
                Does.Not.Contain(
                    "Property=\"Background\" TargetName=\"splitBorder\""));
            NUnitAssert.That(
                watermarkComboBox,
                Does.Not.Contain(
                    "Property=\"BorderBrush\" TargetName=\"splitBorder\""));
            NUnitAssert.That(watermarkComboBox, Does.Not.Contain("#FF606060"));
        });
    }

    [Test]
    public void TreeRows_HoverOnlyTheDirectRowRatherThanAncestorItems()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Controls",
            "Collections.xaml"));
        var start = source.IndexOf(
            "x:Key=\"AeTree.Item\"",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "x:Key=\"AeTree.View\"",
            StringComparison.Ordinal);
        var treeItemStyle = source[start..end];

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(start, Is.GreaterThanOrEqualTo(0));
            NUnitAssert.That(end, Is.GreaterThan(start));
            NUnitAssert.That(
                treeItemStyle,
                Does.Contain(
                    "Binding=\"{Binding IsMouseOver, ElementName=Row}\""));
            NUnitAssert.That(
                treeItemStyle,
                Does.Not.Contain(
                    "<Trigger Property=\"IsMouseOver\" Value=\"True\">"));
        });
    }

    [Test]
    public void GlobalExpanderArrows_AreGlyphOnlyAndUseWeakHoverMotion()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Themes",
            "Controls.xaml"));
        var start = source.IndexOf(
            "x:Key=\"ExpanderArrowGlyphStyle\"",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "x:Key=\"ExpanderWithBorderBackground\"",
            StringComparison.Ordinal);
        var expanderHeaders = source[start..end];

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(start, Is.GreaterThanOrEqualTo(0));
            NUnitAssert.That(end, Is.GreaterThan(start));
            NUnitAssert.That(expanderHeaders, Does.Not.Contain("<Ellipse"));
            NUnitAssert.That(
                expanderHeaders,
                Does.Contain("AeBrush.AccentHover"));
            NUnitAssert.That(
                expanderHeaders,
                Does.Contain("AeMotion.Hover"));
            NUnitAssert.That(expanderHeaders, Does.Contain("DoubleAnimation"));
        });
    }

    [Test]
    public void MenusCollectionsAndSettingsNavigation_UseWeakInteractionMotion()
    {
        var root = FindSolutionRoot();
        var paths = new[]
        {
            "AssetEditor/Themes/DesignSystem/Controls/MenusAndFeedback.xaml",
            "AssetEditor/Themes/DesignSystem/Controls/Collections.xaml",
            "AssetEditor/Themes/DesignSystem/Workflows.xaml",
        };

        foreach (var path in paths)
        {
            var source = File.ReadAllText(Path.Combine(
                root,
                path.Replace('/', Path.DirectorySeparatorChar)));
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(source, Does.Contain("AeMotion.Hover"));
                NUnitAssert.That(source, Does.Contain("DoubleAnimation"));
            });
        }
    }

    private static ResourceDictionary[] LoadDictionaries() =>
    [
        Load("Themes/DesignSystem/Controls/Buttons.xaml"),
        Load("Themes/DesignSystem/Controls/Inputs.xaml"),
        Load("Themes/DesignSystem/Controls/Collections.xaml"),
        Load("Themes/DesignSystem/Controls/MenusAndFeedback.xaml"),
    ];

    private static void AssertPressReleaseMotion(
        ControlTemplate template,
        string? name = null)
    {
        var pressTrigger = template.Triggers
            .OfType<Trigger>()
            .Single(item =>
                item.Property == ButtonBase.IsPressedProperty &&
                Equals(item.Value, true) &&
                item.EnterActions.Count > 0 &&
                item.ExitActions.Count > 0);
        var pressStoryboard = pressTrigger.EnterActions
            .OfType<BeginStoryboard>()
            .Single()
            .Storyboard;
        var releaseStoryboard = pressTrigger.ExitActions
            .OfType<BeginStoryboard>()
            .Single()
            .Storyboard;

        AssertScaleStoryboard(
            pressStoryboard,
            0.985,
            TimeSpan.FromMilliseconds(70),
            name);
        AssertScaleStoryboard(
            releaseStoryboard,
            1,
            TimeSpan.FromMilliseconds(120),
            name);
    }

    private static void AssertScaleStoryboard(
        Storyboard storyboard,
        double target,
        TimeSpan duration,
        string? name)
    {
        var tracks = storyboard.Children
            .OfType<DoubleAnimation>()
            .ToArray();

        NUnitAssert.That(tracks, Has.Length.EqualTo(2), name);
        foreach (var track in tracks)
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    Storyboard.GetTargetName(track),
                    Is.EqualTo("InteractionScale"));
                NUnitAssert.That(
                    Storyboard.GetTargetProperty(track).Path,
                    Is.EqualTo("ScaleX").Or.EqualTo("ScaleY"));
                NUnitAssert.That(track.To, Is.EqualTo(target), name);
                NUnitAssert.That(
                    track.Duration.TimeSpan,
                    Is.EqualTo(duration),
                    name);
                NUnitAssert.That(
                    track.EasingFunction,
                    Is.TypeOf<CubicEase>(),
                    name);
            });
        }
    }

    private static Window CreateOffscreenWindow(UIElement content) => new()
    {
        Width = 160,
        Height = 80,
        Left = -10000,
        Top = -10000,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        Content = content,
    };

    private static ScaleTransform GetInteractionScale(ButtonBase button)
    {
        var templateRoot = (FrameworkElement)VisualTreeHelper.GetChild(
            button,
            0);
        return (ScaleTransform)templateRoot.RenderTransform;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher.CurrentDispatcher)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class PressStateTestButton : Button
    {
        public void SetPressed(bool value) => IsPressed = value;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;
            var nested = FindDescendant<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private static Setter? FindSetter(Style? style, DependencyProperty property)
    {
        while (style != null)
        {
            var setter = style.Setters
                .OfType<Setter>()
                .FirstOrDefault(item => item.Property == property);
            if (setter != null)
                return setter;
            style = style.BasedOn;
        }

        return null;
    }

    private static ResourceDictionary Load(string path) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AssetEditor.CN;component/{path}"),
    };

    private static ResourceDictionary Load(
        string assemblyName,
        string path) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/{assemblyName};component/{path}"),
    };

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }

    private sealed record NamedOption(string Name);
}
