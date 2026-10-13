using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests
{

    [NonParallelizable]
    public class SharedGridSplitterStyleTests
    {
        [TestCase("AeVerticalGridSplitterStyle", Orientation.Vertical)]
        [TestCase("AeHorizontalGridSplitterStyle", Orientation.Horizontal)]
        public void DividerStyle_LoadsThinLineWithWideHitTarget(
            string resourceKey,
            Orientation orientation)
        {
            WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
            {
                ResourceDictionary? resources = null;
                Exception? loadError = null;
                try
                {
                    resources = new ResourceDictionary
                    {
                        Source = new Uri(
                            "pack://application:,,,/Shared.Ui;component/Common/Styles/GridSplitterStyles.xaml"),
                    };
                }
                catch (Exception exception)
                {
                    loadError = exception;
                }

                NUnitAssert.That(loadError, Is.Null, loadError?.ToString());
                NUnitAssert.That(resources, Is.Not.Null);
                var style = resources![resourceKey] as Style;
                NUnitAssert.That(style, Is.Not.Null);
                NUnitAssert.That(style!.TargetType, Is.EqualTo(typeof(GridSplitter)));

                var setters = style.Setters
                    .OfType<Setter>()
                    .ToDictionary(setter => setter.Property);

                var template = setters[Control.TemplateProperty]
                    .Value as ControlTemplate;
                NUnitAssert.That(template, Is.Not.Null);
                var root = template!.LoadContent() as Border;
                NUnitAssert.That(root, Is.Not.Null);
                var divider = root!.Child as Border;

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(divider, Is.Not.Null);
                    NUnitAssert.That(orientation == Orientation.Vertical ? divider!.Width : divider!.Height, Is.EqualTo(1));
                    NUnitAssert.That(root.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Stretch));
                    NUnitAssert.That(root.VerticalAlignment, Is.EqualTo(VerticalAlignment.Stretch));
                    NUnitAssert.That(
                        setters[GridSplitter.ResizeBehaviorProperty].Value,
                        Is.EqualTo(GridResizeBehavior.PreviousAndNext));
                    NUnitAssert.That(
                        setters[GridSplitter.ResizeDirectionProperty].Value,
                        Is.EqualTo(
                            orientation == Orientation.Vertical
                                ? GridResizeDirection.Columns
                                : GridResizeDirection.Rows));
                    NUnitAssert.That(
                        setters[
                            orientation == Orientation.Vertical
                                ? FrameworkElement.WidthProperty
                                : FrameworkElement.HeightProperty].Value,
                        Is.EqualTo(8d));
                });
            });
        }

        [TestCase(ThemeType.DarkTheme)]
        [TestCase(ThemeType.LightTheme)]
        [TestCase(ThemeType.HighContrastDark)]
        [TestCase(ThemeType.HighContrastLight)]
        public void Divider_DragsWithinPanelMinimums_AndRendersAtSupportedScales(ThemeType theme)
        {
            WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
            {
                var previous = ThemesController.CurrentTheme;
                Window? host = null;
                try
                {
                    ThemesController.SetTheme(theme);
                    host = new Window
                    {
                        Width = 600, Height = 600, Left = -10000, Top = -10000,
                        WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
                    };
                    var resources = new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Shared.Ui;component/Common/Styles/GridSplitterStyles.xaml"),
                    };
                    foreach (var vertical in new[] { true, false })
                    {
                        var grid = new Grid { Width = 600, Height = 600 };
                        grid.SetResourceReference(Panel.BackgroundProperty, "AeBrush.Canvas");
                        if (vertical)
                        {
                            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260, GridUnitType.Star), MinWidth = 180 });
                            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(332, GridUnitType.Star), MinWidth = 220 });
                        }
                        else
                        {
                            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260, GridUnitType.Star), MinHeight = 180 });
                            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(332, GridUnitType.Star), MinHeight = 220 });
                        }
                        var splitter = new GridSplitter
                        {
                            Style = (Style)resources[vertical ? "AeVerticalGridSplitterStyle" : "AeHorizontalGridSplitterStyle"],
                        };
                        if (vertical)
                            Grid.SetColumn(splitter, 1);
                        else
                            Grid.SetRow(splitter, 1);
                        grid.Children.Add(splitter);
                        // Add opaque siblings after the splitter to exercise overlapping hit testing.
                        var firstPanel = new Border();
                        var lastPanel = new Border();
                        firstPanel.SetResourceReference(Border.BackgroundProperty, "AeBrush.Surface1");
                        lastPanel.SetResourceReference(Border.BackgroundProperty, "AeBrush.Surface2");
                        if (vertical)
                            Grid.SetColumn(lastPanel, 2);
                        else
                            Grid.SetRow(lastPanel, 2);
                        grid.Children.Add(firstPanel);
                        grid.Children.Add(lastPanel);
                        host.Content = grid;
                        host.Show();
                        host.UpdateLayout();
                        NUnitAssert.That(vertical ? grid.ColumnDefinitions[1].ActualWidth : grid.RowDefinitions[1].ActualHeight,
                            Is.EqualTo(1).Within(0.01), "The transparent hit target must not create an empty layout gutter.");
                        NUnitAssert.That(vertical ? splitter.ActualWidth : splitter.ActualHeight, Is.EqualTo(8));
                        foreach (var edge in new[] { 0.5, 7.5 })
                        {
                            var point = splitter.TranslatePoint(vertical ? new Point(edge, 100) : new Point(100, edge), grid);
                            var hit = (DependencyObject)grid.InputHitTest(point);
                            NUnitAssert.That(hit == splitter || splitter.IsAncestorOf(hit), Is.True,
                                "Both sides of the overlapping target must remain draggable above adjacent panels.");
                        }
                        var initial = vertical ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight;
                        splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter),
                            Environment.TickCount, vertical ? Key.Right : Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });
                        grid.UpdateLayout();
                        NUnitAssert.That(vertical ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight,
                            Is.GreaterThan(initial));
                        Drag(splitter, vertical ? 500 : 0, vertical ? 0 : 500);
                        grid.UpdateLayout();
                        NUnitAssert.That(vertical ? grid.ColumnDefinitions[2].ActualWidth : grid.RowDefinitions[2].ActualHeight, Is.EqualTo(220).Within(0.1));
                        Drag(splitter, vertical ? -500 : 0, vertical ? 0 : -500);
                        grid.UpdateLayout();
                        NUnitAssert.That(vertical ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight, Is.EqualTo(180).Within(0.1));
                        foreach (var dpi in new[] { 96, 120, 144 })
                        {
                            var bitmap = new RenderTargetBitmap(600 * dpi / 96, 600 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
                            bitmap.Render(grid);
                            var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
                            if (string.IsNullOrEmpty(output))
                                continue;
                            Directory.CreateDirectory(output);
                            using var stream = File.Create(Path.Combine(output, $"divider-{theme}-{(vertical ? "vertical" : "horizontal")}-{dpi}.png"));
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            encoder.Save(stream);
                        }
                    }
                }
                finally
                {
                    host?.Close();
                    ThemesController.SetTheme(previous);
                }
            });
        }

        private static void Drag(GridSplitter splitter, double horizontal, double vertical)
        {
            splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new DragDeltaEventArgs(horizontal, vertical) { RoutedEvent = Thumb.DragDeltaEvent });
            splitter.UpdateLayout();
            splitter.RaiseEvent(new DragCompletedEventArgs(horizontal, vertical, false) { RoutedEvent = Thumb.DragCompletedEvent });
        }
    }
}
