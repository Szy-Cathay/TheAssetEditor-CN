using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using Shared.Core.Settings;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class ScrollBarAppearanceTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void SharedScrollBars_UseNarrowSlotsAroundRoundedThumb(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                ThemesController.SetTheme(theme);
                foreach (var styleKey in new object[] { typeof(ScrollBar), "CompactScrollBarStyle", "AeScrollBar.Compact" })
                foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
                {
                    var bar = new ScrollBar
                    {
                        Style = (Style)Application.Current.FindResource(styleKey), Orientation = orientation,
                        Minimum = 0, Maximum = 1000, ViewportSize = 200, Value = 300,
                    };
                    Layout(bar, orientation == Orientation.Vertical ? new Size(17, 260) : new Size(260, 17));
                    var track = (Track)bar.Template.FindName("PART_Track", bar);
                    var thumb = track.Thumb;
                    var surface = (Border?)thumb.Template.FindName("ThumbSurface", thumb);
                    Assert.That(surface, Is.Not.Null, $"{styleKey} {orientation} still uses the square legacy thumb.");
                    Assert.That(track.IsEnabled, Is.True, "Touch and automation must not require mouse hover.");
                    Assert.That(Find<RepeatButton>(bar).Count(), Is.EqualTo(2), "Only page areas remain; arrows take no space.");
                    Assert.That(orientation == Orientation.Vertical ? thumb.ActualWidth : thumb.ActualHeight,
                        Is.EqualTo(6).Within(0.01));
                    Assert.That(orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight,
                        Is.EqualTo(6).Within(0.01), "The layout slot must shrink along with the visible thumb.");
                    Assert.That(orientation == Orientation.Vertical ? surface!.ActualWidth : surface!.ActualHeight,
                        Is.EqualTo(4).Within(0.01));
                    Assert.That(surface.CornerRadius.TopLeft, Is.GreaterThanOrEqualTo(3));
                    Assert.That(VisualTreeHelper.HitTest(thumb, new Point(1, 1)), Is.Not.Null,
                        "The transparent edge must remain part of the thumb surface.");
                    Assert.That(orientation == Orientation.Vertical ? track.ActualHeight : track.ActualWidth,
                        Is.EqualTo(orientation == Orientation.Vertical ? bar.ActualHeight : bar.ActualWidth));

                    SetState(bar, typeof(UIElement), "IsMouseOverPropertyKey", true);
                    bar.UpdateLayout();
                    Assert.That(orientation == Orientation.Vertical ? surface.ActualWidth : surface.ActualHeight,
                        Is.EqualTo(6).Within(0.01));
                    SetState(thumb, typeof(Thumb), "IsDraggingPropertyKey", true);
                    SetState(bar, typeof(UIElement), "IsMouseOverPropertyKey", false);
                    bar.UpdateLayout();
                    Assert.That(orientation == Orientation.Vertical ? surface.ActualWidth : surface.ActualHeight,
                        Is.EqualTo(6).Within(0.01), "Dragging stays visible after leaving the bar.");
                    Assert.That(((SolidColorBrush)surface.Background).Color,
                        Is.EqualTo(((SolidColorBrush)Application.Current.FindResource("AeBrush.TextPrimary")).Color));
                    SetState(thumb, typeof(Thumb), "IsDraggingPropertyKey", false);
                    bar.IsEnabled = false;
                    Assert.That(thumb.Opacity, Is.EqualTo(Application.Current.FindResource("AeInteraction.DisabledOpacity")));
                }
            }
            finally { ThemesController.SetTheme(previous); }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ScrollViewer_PreservesWheelKeyboardDragAndAutomation(bool compact)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var viewer = CreateViewer(compact);
            var window = new Window
            {
                Content = viewer, Width = 420, Height = 300, Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                Flush(viewer);
                var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
                var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
                var presenter = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
                Assert.That(viewer.ScrollableHeight, Is.GreaterThan(1000));
                Assert.That(viewer.ScrollableWidth, Is.GreaterThan(500));
                Assert.That(presenter.ActualWidth + vertical.ActualWidth, Is.EqualTo(viewer.ActualWidth).Within(1));
                Assert.That(presenter.ActualHeight + horizontal.ActualHeight, Is.EqualTo(viewer.ActualHeight).Within(1));
                Assert.That(LayoutInformation.GetLayoutSlot(vertical).Width, Is.EqualTo(6).Within(0.01));
                Assert.That(LayoutInformation.GetLayoutSlot(horizontal).Height, Is.EqualTo(6).Within(0.01));

                viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    { RoutedEvent = Mouse.MouseWheelEvent });
                Flush(viewer);
                Assert.That(viewer.VerticalOffset, Is.GreaterThan(0));
                var before = viewer.VerticalOffset;
                viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.PageDown)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                Flush(viewer);
                Assert.That(viewer.VerticalOffset, Is.GreaterThan(before));

                foreach (var bar in new[] { vertical, horizontal })
                {
                    var track = (Track)bar.Template.FindName("PART_Track", bar);
                    Assert.That(track.Thumb.InputHitTest(new Point(1, 1)), Is.Not.Null,
                        "The connected window must accept dragging from the transparent edge.");
                    before = bar.Value;
                    track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
                    track.Thumb.RaiseEvent(new DragDeltaEventArgs(bar.Orientation == Orientation.Horizontal ? 24 : 0,
                        bar.Orientation == Orientation.Vertical ? 24 : 0));
                    track.Thumb.RaiseEvent(new DragCompletedEventArgs(24, 24, false));
                    Flush(viewer);
                    Assert.That(bar.Value, Is.GreaterThan(before));
                    Assert.That(bar.Value, Is.EqualTo(bar.Orientation == Orientation.Vertical
                        ? viewer.VerticalOffset : viewer.HorizontalOffset).Within(0.01));
                    Assert.That(AutomationPeerFor(bar).GetPattern(PatternInterface.RangeValue), Is.Not.Null);
                    before = bar.Value;
                    ((RoutedCommand)track.IncreaseRepeatButton.Command).Execute(null, track.IncreaseRepeatButton);
                    Flush(viewer);
                    Assert.That(bar.Value, Is.GreaterThan(before));
                }

                var provider = (IScrollProvider)AutomationPeerFor(viewer).GetPattern(PatternInterface.Scroll)!;
                provider.SetScrollPercent(100, 100);
                Flush(viewer);
                Assert.That(viewer.HorizontalOffset, Is.EqualTo(viewer.ScrollableWidth));
                Assert.That(viewer.VerticalOffset, Is.EqualTo(viewer.ScrollableHeight));
                Assert.That(viewer.PanningMode, Is.EqualTo(PanningMode.Both), "Template must preserve the caller's touch mode.");
            }
            finally { window.Close(); }
        });
    }

    [TestCase("/Shared.Ui;component/Common/Styles/EditorWorkspaceStyles.xaml")]
    [TestCase("/Editors.KitbasherEditor;component/KitbashUiStyles.xaml")]
    public void EditorOverrides_ReachListsTreesTablesAndAvalonEdit(string dictionary)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var rows = Enumerable.Range(1, 5000).Select(index => $"资源 {index:0000}").ToArray();
            var list = new ListBox { ItemsSource = rows };
            VirtualizingPanel.SetIsVirtualizing(list, true);
            ScrollViewer.SetCanContentScroll(list, true);
            var controls = new Control[]
            {
                list,
                new TreeView { ItemsSource = rows },
                new DataGrid { ItemsSource = rows.Select(text => new { Name = text }), AutoGenerateColumns = true },
                new ICSharpCode.AvalonEdit.TextEditor { Text = string.Join("\n", rows), WordWrap = false },
            };
            var root = new Grid();
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(dictionary, UriKind.Relative) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            for (var index = 0; index < controls.Length; index++)
            {
                Grid.SetColumn(controls[index], index % 2);
                Grid.SetRow(controls[index], index / 2);
                root.Children.Add(controls[index]);
            }
            Layout(root, new Size(800, 500));
            foreach (var control in controls)
            {
                var bars = Find<ScrollBar>(control).Where(bar => bar.Visibility == Visibility.Visible).ToArray();
                Assert.That(bars, Is.Not.Empty, control.GetType().Name);
                foreach (var bar in bars)
                {
                    var track = (Track)bar.Template.FindName("PART_Track", bar);
                    Assert.That(track.Thumb.Template.FindName("ThumbSurface", track.Thumb), Is.Not.Null,
                        $"{dictionary}: {control.GetType().Name} overrides the public scrollbar.");
                }
            }
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(4999), Is.Null, "Long lists remain virtualized.");
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    [Category("DesktopPreview")]
    public void ScrollBars_RenderInNativeWindowAtWindowDpi(ThemeType theme)
    {
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1")
            Assert.Ignore("Requires a reserved desktop.");
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            ThemesController.SetTheme(theme);
            var viewer = CreateViewer(false);
            var root = new Border { Padding = new Thickness(16), Child = viewer };
            root.SetResourceReference(Border.BackgroundProperty, "AeBrush.Surface1");
            var window = new Window
            {
                Title = "滚动条预览", Content = root, Width = 700, Height = 400, Left = 40, Top = 40,
                ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            };
            try
            {
                window.Show();
                Flush(root);
                var handle = new WindowInteropHelper(window).Handle;
                foreach (var dpi in new[] { 96, 120, 144 })
                {
                    var bounds = new NativeRectangle { Left = 60, Top = 60, Right = 60 + 700 * dpi / 96, Bottom = 60 + 400 * dpi / 96 };
                    SendDpiMessage(handle, 0x02E0, new nint(dpi | (dpi << 16)), ref bounds);
                    viewer.ScrollToVerticalOffset(350);
                    viewer.ScrollToHorizontalOffset(180);
                    Flush(root);
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 150)
                        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.That(VisualTreeHelper.GetDpi(window).PixelsPerInchX, Is.EqualTo(dpi));
                    Save(OperationProgressWindowGallery.Capture(window), $"scrollbars-desktop-{theme}-{dpi}.png");
                    var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
                    var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
                    var horizontalThumb = ((Track)horizontal.Template.FindName("PART_Track", horizontal)).Thumb;
                    SetState(vertical, typeof(UIElement), "IsMouseOverPropertyKey", true);
                    SetState(horizontalThumb, typeof(Thumb), "IsDraggingPropertyKey", true);
                    Flush(root);
                    clock.Restart();
                    while (clock.ElapsedMilliseconds < 150)
                        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Save(OperationProgressWindowGallery.Capture(window), $"scrollbars-desktop-active-{theme}-{dpi}.png");
                    SetState(vertical, typeof(UIElement), "IsMouseOverPropertyKey", false);
                    SetState(horizontalThumb, typeof(Thumb), "IsDraggingPropertyKey", false);
                }
            }
            finally
            {
                window.Close();
                ThemesController.SetTheme(previous);
            }
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendDpiMessage(nint hwnd, int message, nint wParam, ref NativeRectangle bounds);

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void LongContent_RendersBothAxesAtSupportedScales(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                ThemesController.SetTheme(theme);
                var viewer = CreateViewer(false);
                var root = new Border { Padding = new Thickness(16), Child = viewer };
                root.SetResourceReference(Border.BackgroundProperty, "AeBrush.Surface1");
                Layout(root, new Size(640, 380));
                viewer.ScrollToVerticalOffset(550);
                viewer.ScrollToHorizontalOffset(220);
                Flush(root);
                foreach (var dpi in new[] { 96, 120, 144 })
                {
                    var image = new RenderTargetBitmap(640 * dpi / 96, 380 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
                    image.Render(root);
                    Save(image, $"scrollbars-{theme}-{dpi}.png");
                }
            }
            finally { ThemesController.SetTheme(previous); }
        });
    }

    private static ScrollViewer CreateViewer(bool compact)
    {
        var rows = new StackPanel { Width = 1200 };
        for (var index = 1; index <= 160; index++)
        {
            var text = new TextBlock { Text = $"{index:000}　资源文件　variantmeshes / units / empire / empire_general / model.rigid_model_v2", Height = 24 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "AeBrush.TextPrimary");
            rows.Children.Add(text);
        }
        var viewer = new ScrollViewer
        {
            Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both,
        };
        if (compact)
            viewer.Style = (Style)Application.Current.FindResource("CompactBarsScrollViewerStyle");
        return viewer;
    }

    private static AutomationPeer AutomationPeerFor(UIElement control) => UIElementAutomationPeer.CreatePeerForElement(control)!;

    private static void Layout(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        Flush(element);
    }

    private static void Flush(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var nested in Find<T>(child))
                yield return nested;
        }
    }

    private static void SetState(DependencyObject target, Type owner, string name, bool value)
        => target.SetValue((DependencyPropertyKey)owner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);

    private static void Save(BitmapSource image, string name)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, name));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }
}
