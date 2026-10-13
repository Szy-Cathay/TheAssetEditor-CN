using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class RecentProjectInteractionTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void RecentProject_PaintsOnlyInsideTheAnimatedButton_AndPreservesSelection(ThemeType theme)
    {
        WithWorkspace(theme, false, (window, tabs, row, button, invoked) =>
        {
            Assert.That(row.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("AeFocus.Keyboard")));
            Assert.That(button.FocusVisualStyle, Is.SameAs(row.FocusVisualStyle));
            Assert.That(row.Focusable && button.Focusable, Is.True);
            SetState(row, typeof(UIElement), "IsMouseOverPropertyKey", true);
            SetState(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
            Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
            UiRefinementTests.SaveSnapshot(tabs, $"recent-{theme}-hover.png");
            foreach (var selected in new[] { false, true })
            {
                row.IsSelected = selected;
                SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", true);
                Pump(180);
                Assert.That(Scale(button).ScaleX, Is.EqualTo(0.985).Within(0.001));
                Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.Surface2")));
                UiRefinementTests.SaveSnapshot(tabs, $"recent-{theme}-pressed-{selected}.png");
                AssertNoOuterSurface(tabs, row);
                Assert.That(invoked, Is.Empty);
                SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", false);
                Pump(180);
                Assert.That(Scale(button).ScaleX, Is.EqualTo(1).Within(0.001));
                Assert.That(row.IsSelected, Is.EqualTo(selected));
                Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor(selected ? "AeBrush.AccentSoft" : "AeBrush.SurfaceHover")));
            }
            SetState(row, typeof(UIElement), "IsMouseOverPropertyKey", false);
            SetState(button, typeof(UIElement), "IsMouseOverPropertyKey", false);
            Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
            row.IsSelected = false;
            Pump(30);
            Assert.That(ColorOf(button.Background).A, Is.Zero);
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [Category("DesktopPreview")]
    public void RecentProject_RealMouseAndKeyboardKeepOneAnimatedSurface(ThemeType theme)
    {
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1")
            Assert.Ignore("Requires a reserved desktop.");
        WithWorkspace(theme, true, (window, tabs, row, button, invoked) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            window.Activate();
            SetForegroundWindow(hwnd);
            Pump(200);
            Assert.That(GetForegroundWindow(), Is.EqualTo(hwnd));
            var clock = Stopwatch.StartNew();
            var frames = new List<(long Time, BitmapSource Image)>();
            MoveTo(button, new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            Record(180);
            Save("hover");
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Record(180);
            Assert.That(button.IsPressed && button.IsMouseCaptured, Is.True);
            AssertNoOuterSurface(tabs, row);
            Save("pressed");
            Pump(2000);
            Record(16);
            Assert.That(button.IsPressed, Is.True);
            Assert.That(Scale(button).ScaleX, Is.EqualTo(0.985).Within(0.001));
            Assert.That(invoked, Is.Empty);
            MoveTo(button, new Point(-30, -30));
            Record(180);
            Assert.That(button.IsPressed, Is.False);
            Assert.That(Scale(button).ScaleX, Is.EqualTo(1).Within(0.001));
            MoveTo(button, new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            Record(180);
            Assert.That(button.IsPressed, Is.True);
            AssertNoOuterSurface(tabs, row);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Record(180);
            Assert.That(button.IsPressed || button.IsMouseCaptured, Is.False);
            Assert.That(invoked, Has.Count.EqualTo(1));
            Save("released");
            MoveTo(button, new Point(-30, -30));
            Pump(150);
            Assert.That(button.IsMouseOver, Is.False);
            if (row.IsSelected)
                Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
            else
                Assert.That(ColorOf(button.Background).A, Is.Zero);

            UiRefinementTests.Descendants<Button>(tabs).First().Focus();
            for (var index = 0; index < 8 && !button.IsKeyboardFocused; index++)
            {
                keybd_event(0x09, 0, 0, UIntPtr.Zero);
                keybd_event(0x09, 0, 2, UIntPtr.Zero);
                Pump(80);
            }
            Assert.That(button.IsKeyboardFocused, Is.True, "Tab navigation must still reach the project command.");
            Assert.That(button.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("AeFocus.Keyboard")));
            var count = invoked.Count;
            keybd_event(0x20, 0, 0, UIntPtr.Zero);
            Pump(2000);
            Assert.That(button.IsPressed, Is.True);
            Assert.That(invoked, Has.Count.EqualTo(count));
            AssertNoOuterSurface(tabs, row);
            Save("keyboard-pressed");
            keybd_event(0x20, 0, 2, UIntPtr.Zero);
            Pump(180);
            Assert.That(invoked, Has.Count.EqualTo(count + 1));
            Assert.That(button.IsPressed, Is.False);
            keybd_event(0x0D, 0, 0, UIntPtr.Zero);
            keybd_event(0x0D, 0, 2, UIntPtr.Zero);
            Pump(180);
            Assert.That(invoked, Has.Count.EqualTo(count + 2));
            Assert.That(invoked.All(path => path.EndsWith("yinyin_animation_test", StringComparison.Ordinal)), Is.True);

            var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                var encoder = new GifBitmapEncoder();
                for (var index = 0; index < frames.Count; index++)
                {
                    var metadata = new BitmapMetadata("gif");
                    var delay = index + 1 < frames.Count ? frames[index + 1].Time - frames[index].Time : 300;
                    metadata.SetQuery("/grctlext/Delay", (ushort)Math.Max(1, delay / 10));
                    encoder.Frames.Add(BitmapFrame.Create(frames[index].Image, null, metadata, null));
                }
                using var stream = File.Create(Path.Combine(output, $"recent-{theme}-mouse.gif"));
                encoder.Save(stream);
            }
            NUnit.Framework.TestContext.WriteLine($"{theme}: held mouse and Space for 2 s; drag out/back; one command per release/Enter; native DPI {VisualTreeHelper.GetDpi(window).PixelsPerInchX}; {frames.Count} frames.");

            void Record(int milliseconds)
            {
                var start = clock.ElapsedMilliseconds;
                do
                {
                    Pump(16);
                    frames.Add((clock.ElapsedMilliseconds, OperationProgressWindowGallery.Capture(window)));
                } while (clock.ElapsedMilliseconds - start < milliseconds);
            }
            void Save(string state)
            {
                UiRefinementTests.SaveSnapshot(tabs, $"recent-{theme}-{state}.png");
            }
        });
    }

    private static void WithWorkspace(ThemeType theme, bool desktop, Action<Window, CachedTabControl, ListBoxItem, Button, List<string>> verify)
    {
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        settings.CurrentSettings.RecentFolderProjectPaths.Add(@"D:\测试工程\参考工程");
        settings.CurrentSettings.RecentFolderProjectPaths.Add(@"D:\测试工程\yinyin_animation_test");
        var invoked = new List<string>();
        var open = new Mock<IFolderProjectOpenService>();
        open.Setup(service => service.Open(It.IsAny<string>())).Callback<string>(invoked.Add);
        var services = new DependencyInjectionConfig(loadResources: false).Build(false, registrations =>
        {
            registrations.AddSingleton(settings);
            registrations.AddSingleton(open.Object);
        });
        try
        {
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                services.GetRequiredService<LocalizationManager>().LoadLanguage();
                var previousTheme = ThemesController.CurrentTheme;
                var previousWindow = desktop ? GetForegroundWindow() : IntPtr.Zero;
                GetCursorPos(out var previousPointer);
                ThemesController.SetTheme(theme);
                var tabs = new CachedTabControl
                {
                    Style = (Style)Application.Current.FindResource("AeShell.EditorTabs"),
                    DataContext = new { MenuBar = services.GetRequiredService<MenuBarViewModel>() },
                };
                var window = new Window
                {
                    Title = "最近工程按压验证", Content = tabs, Width = 760, Height = 560,
                    Left = desktop ? 100 : -10000, Top = desktop ? 100 : -10000,
                    ShowActivated = desktop, ShowInTaskbar = false, Topmost = desktop,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Pump(120);
                    var list = (ListBox)tabs.Template.FindName("RecentProjectsList", tabs);
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var button = UiRefinementTests.Descendants<Button>(row).Single();
                    verify(window, tabs, row, button, invoked);
                }
                finally
                {
                    if (desktop)
                    {
                        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                        keybd_event(0x20, 0, 2, UIntPtr.Zero);
                        window.Close();
                        SetCursorPos(previousPointer.X, previousPointer.Y);
                        SetForegroundWindow(previousWindow);
                    }
                    else
                        window.Close();
                    ThemesController.SetTheme(previousTheme);
                }
            });
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    private static void AssertNoOuterSurface(CachedTabControl tabs, ListBoxItem row)
    {
        tabs.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)tabs.ActualWidth, (int)tabs.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(tabs);
        var point = row.TranslatePoint(new Point(1, row.ActualHeight / 2), tabs);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
        Assert.That(Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]), Is.EqualTo(ResourceColor("AeBrush.Canvas")),
            "The row outside the shrinking button must reveal the canvas, not a stationary hover/selected ring.");
    }

    private static ScaleTransform Scale(Button button) => (ScaleTransform)button.Template.FindName("InteractionScale", button);
    private static Color ResourceColor(string key) => ColorOf((Brush)Application.Current.FindResource(key));
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static void SetState(DependencyObject control, Type owner, string field, bool value)
        => control.SetValue((DependencyPropertyKey)owner.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static void MoveTo(Visual element, Point point)
    {
        var screen = element.PointToScreen(point);
        SetCursorPos((int)screen.X, (int)screen.Y);
        Pump(80);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
