using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using Editors.KitbasherEditor.Components;
using KitbasherEditor.Views;
using NUnit.Framework;
using Shared.Core.Misc;
using Shared.Core.Settings;
using Shared.EmbeddedResources;
using Shared.Ui.Common.Behaviors;
using Shared.Ui.Common.MenuSystem;
using Assert = NUnit.Framework.Assert;
using Path = System.Windows.Shapes.Path;

namespace AssetEditorTests;

[NonParallelizable]
public class PlaybackAndViewportRefinementTests
{
    public static IEnumerable<TestCaseData> ThemeScales =>
        from theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight }
        from scale in new[] { 1d, 1.25d, 1.5d }
        select new TestCaseData(theme, scale);

    [TestCaseSource(nameof(ThemeScales))]
    public void SceneToolbarAndPlayers_RenderWithoutWindowsOrInput(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var initialWindows = Application.Current.Windows.Count;
            if (IconLibrary.Gizmo_MoveIcon == null)
                IconLibrary.Load();
            foreach (var viewportBackground in new[] { BackgroundColour.DarkGrey, BackgroundColour.LegacyBlue, BackgroundColour.Green })
            {
                var backgroundColour = ApplicationSettingsHelper.GetEnumAsColour(viewportBackground);
                var colour = Color.FromRgb(backgroundColour.R, backgroundColour.G, backgroundColour.B);
                var buttons = Enumerable.Range(0, 3).Select(index =>
                    new MenuBarGroupButton(new MenuAction { ToolTip = new[] { "常态", "悬停", "选中" }[index] }, "probe", index == 2)
                    { Image = IconLibrary.Gizmo_MoveIcon! }).ToArray();
                var view = new KitbasherView
                {
                    DataContext = new WorkspaceProbe
                    {
                        LeftColumnWidth = new GridLength(1, GridUnitType.Star),
                        RightColumnWidth = new GridLength(400),
                        Scene = new Border { Background = new SolidColorBrush(colour) },
                        Animation = new PlayerProbe(),
                        MenuBar = new
                        {
                            MenuItems = Array.Empty<object>(), CustomButtons = Array.Empty<object>(), SidebarButtons = buttons,
                            TransformTool = new { IsVisible = false }, CanUseMeshSelectionTools = new NotifyAttr<bool>(false),
                            SelectionSettings = new KitbashSelectionSettings()
                        }
                    }
                };
                VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
                Layout(view, 1060, 470);
                var sidebar = (StackPanel)view.FindName("SidebarOverlay");
                var radio = Descendants<RadioButton>((ItemsControl)view.FindName("SidebarTools")).ToArray();
                Assert.That(radio.Length, Is.EqualTo(3));
                SetReadOnly(radio[1], typeof(UIElement), "IsMouseOverPropertyKey", true);
                Layout(view, 1060, 470);
                foreach (var button in radio)
                {
                    var glyph = Descendants<System.Windows.Shapes.Rectangle>(button).Single();
                    Assert.That(((ImageBrush)glyph.OpacityMask).ImageSource, Is.Not.Null);
                    var chrome = (Border)button.Template.FindName("Chrome", button);
                    var background = ((SolidColorBrush)chrome.Background).Color;
                    if (background.A == 0)
                        background = ((SolidColorBrush)sidebar.Background).Color;
                    Assert.That(chrome.BorderThickness, Is.EqualTo(new Thickness(0)));
                    Assert.That(Contrast(((SolidColorBrush)button.Foreground).Color, background), Is.GreaterThanOrEqualTo(3));
                }
                var hovered = (Border)radio[1].Template.FindName("Chrome", radio[1]);
                var selected = (Border)radio[2].Template.FindName("Chrome", radio[2]);
                Assert.That(((SolidColorBrush)hovered.Background).Color, Is.Not.EqualTo(((SolidColorBrush)selected.Background).Color));
                AssertSidebarCorners(view, sidebar, colour, scale);
                Render(view, $"workspace-{theme}-{viewportBackground}-{scale:0.00}", scale);

                var toggle = (ToggleButton)view.FindName("SidebarToggle");
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                Layout(view, 1060, 470);
                AssertSidebarCorners(view, sidebar, colour, scale);
                Render(view, $"workspace-{theme}-{viewportBackground}-{scale:0.00}-collapsed", scale);
            }
            Assert.That(Application.Current.Windows.Count, Is.EqualTo(initialWindows));
        });
    }

    [TestCaseSource(nameof(ThemeScales))]
    public void PlaybackStatesAndTrack_UseNativeGeometryWithoutScale(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Shared.Ui;component/Common/Styles/EditorWorkspaceStyles.xaml") };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var gallery = new StackPanel { Background = Brush("AeBrush.Surface1") };
            VisualTreeHelper.SetRootDpi(gallery, new DpiScale(scale, scale));
            gallery.Children.Add(row);
            foreach (var state in new[] { "常态", "悬停", "按下", "播放中", "播放中悬停", "禁用" })
            {
                var column = new StackPanel { Width = 100 };
                column.Children.Add(new TextBlock { Text = state, Foreground = Brush("AeBrush.TextPrimary"), HorizontalAlignment = HorizontalAlignment.Center });
                var glyph = state.Contains("播放")
                    ? "M 4,2 H7 V14 H4 Z M 10,2 H13 V14 H10 Z"
                    : "M 4,2 L 14,8 L 4,14 Z";
                var button = new ToggleButton
                {
                    Style = (Style)styles["AeEditor.PlaybackToggle"],
                    Content = new Path { Data = Geometry.Parse(glyph), Width = 16, Height = 16 }
                };
                ((Path)button.Content).SetBinding(Path.FillProperty, new Binding(nameof(Control.Foreground)) { Source = button });
                if (state.Contains("播放")) button.IsChecked = true;
                if (state.Contains("悬停")) SetReadOnly(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
                if (state == "按下") SetReadOnly(button, typeof(ButtonBase), "IsPressedPropertyKey", true);
                if (state == "禁用") button.IsEnabled = false;
                column.Children.Add(button);
                row.Children.Add(column);
                Layout(button, 32, 30);
                var chrome = (Border)button.Template.FindName("Chrome", button);
                Assert.That(chrome.RenderTransform.Value.IsIdentity, Is.True);
                Assert.That(chrome.BorderThickness, Is.EqualTo(new Thickness(0)));
                if (state.Contains("播放"))
                    Assert.That(((SolidColorBrush)chrome.Background).Color, Is.EqualTo(((SolidColorBrush)Brush("AeBrush.AccentSoft")).Color));
            }

            var position = new NotifyAttr<double>(0.75);
            var slider = new Slider { Style = (Style)styles["AeEditor.PlaybackSlider"], Minimum = 0, Maximum = 2 };
            slider.SetBinding(Slider.ValueProperty, new Binding(nameof(position.Value))
            {
                Source = position, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            gallery.Children.Add(slider);
            Layout(gallery, 600);
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            Assert.That(track.Thumb.ActualHeight, Is.GreaterThanOrEqualTo(24));
            Assert.That(((Border)slider.Template.FindName("Rail", slider)).ActualHeight, Is.EqualTo(3));
            Assert.That(SliderScrubbing.GetIsEnabled(slider), Is.True);

            var seek = typeof(SliderScrubbing).GetMethod("Seek", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var fraction in new[] { 0d, 0.25, 0.5, 0.75, 1d })
            {
                var x = track.Thumb.ActualWidth / 2 + fraction * (track.ActualWidth - track.Thumb.ActualWidth);
                seek.Invoke(null, new object[] { slider, new Point(x, track.ActualHeight / 2) });
                Assert.That(slider.Value, Is.EqualTo(2 * fraction).Within(0.00001));
                Assert.That(position.Value, Is.EqualTo(slider.Value));
                Assert.That(BindingOperations.IsDataBound(slider, Slider.ValueProperty), Is.True);
                Layout(gallery, 600);
            }
            position.Value = 0.75;
            SliderScrubbing.SetIsScrubbing(slider, true);
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.That(SliderScrubbing.GetIsScrubbing(slider), Is.False);
            Layout(gallery, 600);
            Render(gallery, $"playback-states-{theme}-{scale:0.00}", scale);
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    public void MenuSeparators_AreCompactAndItemsKeepClickHeight(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var panel = new StackPanel { Background = Brush("AeBrush.Surface2") };
            VisualTreeHelper.SetRootDpi(panel, new DpiScale(1.5, 1.5));
            var separator = new Separator { Style = (Style)Application.Current.FindResource(MenuItem.SeparatorStyleKey) };
            var before = new MenuItem { Header = "另存为" };
            var after = new MenuItem { Header = "导入参考模型" };
            panel.Children.Add(before);
            panel.Children.Add(separator);
            panel.Children.Add(after);
            Layout(panel, 240);
            Assert.That(separator.ActualHeight + separator.Margin.Top + separator.Margin.Bottom, Is.LessThanOrEqualTo(5));
            Assert.That(before.ActualHeight, Is.GreaterThanOrEqualTo(24));
            Assert.That(after.ActualHeight, Is.GreaterThanOrEqualTo(24));
            Render(panel, $"menu-spacing-{theme}", 1.5);
        });
    }

    internal static void WithTheme(ThemeType theme, Action action) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try { ThemesController.SetTheme(theme); action(); }
            finally { ThemesController.SetTheme(previous); }
        });

    internal static void Layout(FrameworkElement element, double width, double height = double.PositiveInfinity)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, double.IsPositiveInfinity(height) ? element.DesiredSize.Height : height));
        element.UpdateLayout();
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    private static void AssertSidebarCorners(FrameworkElement view, FrameworkElement sidebar, Color sceneColour, double scale)
    {
        var clip = sidebar.Clip as RectangleGeometry;
        var radius = (CornerRadius)sidebar.FindResource("AeRadius.Control");
        Assert.That(clip, Is.Not.Null);
        Assert.That(clip!.Rect.Size, Is.EqualTo(sidebar.RenderSize));
        Assert.That(clip.RadiusX, Is.EqualTo(radius.TopLeft));
        Assert.That(clip.RadiusY, Is.EqualTo(radius.TopLeft));

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale),
            (int)Math.Ceiling(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var origin = sidebar.TranslatePoint(new Point(), view);
        var pixel = new byte[4];
        foreach (var x in new[] { 0.5, sidebar.ActualWidth - 0.5 })
        foreach (var y in new[] { 0.5, sidebar.ActualHeight - 0.5 })
        {
            bitmap.CopyPixels(new Int32Rect((int)Math.Floor((origin.X + x) * scale),
                (int)Math.Floor((origin.Y + y) * scale), 1, 1), pixel, 4, 0);
            Assert.That(Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]), Is.EqualTo(sceneColour),
                "Neither the backplate nor its children may cover the rounded-off corners.");
        }
    }

    private static void SetReadOnly(DependencyObject target, Type owner, string field, bool value) =>
        target.SetValue((DependencyPropertyKey)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, value);

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color colour)
        {
            static double Linear(byte channel) => channel / 255d <= 0.04045 ? channel / 3294.6 : Math.Pow((channel / 255d + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    internal static void Render(FrameworkElement element, string name, double scale)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale),
            (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        using var stream = File.Create(System.IO.Path.Combine(output, name + ".png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    public sealed class WorkspaceProbe
    {
        public GridLength LeftColumnWidth { get; set; }
        public GridLength RightColumnWidth { get; set; }
        public object Scene { get; set; } = null!;
        public PlayerProbe Animation { get; set; } = null!;
        public object MenuBar { get; set; } = null!;
    }

    public sealed class PlayerProbe
    {
        public bool IsEnabled { get; set; } = true;
        public bool IsPlaying { get; set; } = true;
        public bool IsScrubbing { get; set; }
        public NotifyAttr<Visibility> AnimationControllerVisability { get; } = new(Visibility.Visible);
        public int CurrentFrame { get; set; } = 19;
        public int MaxFrames { get; set; } = 47;
        public float PlaybackPositionSeconds { get; set; } = 0.89f;
        public float MaxTimeSeconds { get; set; } = 2.3f;
        public string HeaderText { get; set; } = "测试骨骼 / 动画";
        public object[] SkeletonList { get; set; } = [];
        public object[] AnimationsForCurrentSkeleton { get; set; } = [];
    }
}
