using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class MaximizedMainWindowTests
{
    [TestCase(ThemeType.DarkTheme, true)]
    [TestCase(ThemeType.LightTheme, false)]
    [TestCase(ThemeType.HighContrastDark, false)]
    [TestCase(ThemeType.HighContrastLight, true)]
    public void MaximizedWindow_KeepsStatusBarInsideItsMonitorWorkArea(ThemeType theme, bool startMaximized)
    {
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        settings.CurrentSettings.StartMaximised = startMaximized;
        var services = new DependencyInjectionConfig(loadResources: false).Build(false,
            registrations => registrations.AddSingleton(settings));
        try
        {
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                services.GetRequiredService<LocalizationManager>().LoadLanguage();
                var previousTheme = ThemesController.CurrentTheme;
                ThemesController.SetTheme(theme);
                var model = services.GetRequiredService<MainViewModel>();
                var window = services.GetRequiredService<MainWindow>();
                window.DataContext = model;
                window.ShowInTaskbar = false;
                try
                {
                    window.Show();
                    Flush(window);
                    var handle = new WindowInteropHelper(window).Handle;
                    var nativeDpi = (int)GetDpiForWindow(handle);
                    NUnit.Framework.TestContext.WriteLine($"Desktop monitors: {GetSystemMetrics(80)}; native window DPI: {nativeDpi}.");
                    Assert.That(window.WindowState, Is.EqualTo(startMaximized ? WindowState.Maximized : WindowState.Normal));
                    if (startMaximized)
                        AssertInsideWorkArea(window, "initial maximization");

                    foreach (var dpi in new[] { 96, 120, 144 })
                    {
                        window.WindowState = WindowState.Normal;
                        Flush(window);
                        Assert.That(GetWindowRect(handle, out var bounds), Is.True);
                        SendDpiMessage(handle, 0x02E0, new nint(dpi | (dpi << 16)), ref bounds);
                        Flush(window);
                        Assert.That(VisualTreeHelper.GetDpi(window).PixelsPerInchX, Is.EqualTo(dpi));
                        var content = (FrameworkElement)window.Content;
                        var status = window.ApplicationStatusBar;
                        var statusBottom = status.TranslatePoint(new Point(0, status.ActualHeight), content).Y;
                        Assert.That(statusBottom, Is.LessThanOrEqualTo(content.ActualHeight));
                        Assert.That(status.ActualHeight, Is.GreaterThanOrEqualTo(24));
                        UiRefinementTests.SaveSnapshot(content, $"restored-layout-{theme}-{dpi}.png", dpi);

                        // WM_DPICHANGED alone does not change the monitor's native DPI.
                        // Restore the real DPI before comparing WPF points with Win32 work-area bounds.
                        Assert.That(GetWindowRect(handle, out bounds), Is.True);
                        SendDpiMessage(handle, 0x02E0, new nint(nativeDpi | (nativeDpi << 16)), ref bounds);
                        Flush(window);
                        Assert.That(VisualTreeHelper.GetDpi(window).PixelsPerInchX, Is.EqualTo(GetDpiForWindow(handle)));
                        var restoredBounds = window.RestoreBounds;

                        var button = new ButtonAutomationPeer(window.MainMenuBar.MaximizeRestoreButton);
                        ((IInvokeProvider)button.GetPattern(PatternInterface.Invoke)).Invoke();
                        Flush(window);
                        AssertInsideWorkArea(window, $"maximize button after {dpi} DPI transition (native {nativeDpi})");

                        SendMessage(handle, 0x00A3, 2, CaptionPosition(window));
                        Flush(window);
                        Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));
                        Assert.That(window.RestoreBounds.Width, Is.EqualTo(restoredBounds.Width).Within(1));
                        Assert.That(window.RestoreBounds.Height, Is.EqualTo(restoredBounds.Height).Within(1));
                        SendMessage(handle, 0x00A3, 2, CaptionPosition(window));
                        Flush(window);
                        AssertInsideWorkArea(window, $"caption double-click after {dpi} DPI transition (native {nativeDpi})");
                    }

                    var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        var frame = new DispatcherFrame();
                        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
                            (_, _) => frame.Continue = false, window.Dispatcher);
                        Dispatcher.PushFrame(frame);
                        timer.Stop();
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(OperationProgressWindowGallery.Capture(window)));
                        using var stream = File.Create(Path.Combine(output, $"maximized-native-{theme}.png"));
                        encoder.Save(stream);
                    }
                }
                finally
                {
                    window.DataContext = null;
                    window.Close();
                    model.FileTree.Dispose();
                    ThemesController.SetTheme(previousTheme);
                }
            });
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }

    private static void AssertInsideWorkArea(MainWindow window, string action)
    {
        Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized), action);
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
        Assert.That(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor), Is.True);
        Assert.That(GetWindowRect(handle, out var bounds), Is.True);
        var status = window.ApplicationStatusBar;
        var bottom = status.PointToScreen(new Point(status.ActualWidth, status.ActualHeight));
        var menuTop = window.MainMenuBar.PointToScreen(new Point());
        NUnit.Framework.TestContext.WriteLine($"{action}: window={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; monitor={monitor.Monitor.Left},{monitor.Monitor.Top},{monitor.Monitor.Right},{monitor.Monitor.Bottom}; work={monitor.Work.Left},{monitor.Work.Top},{monitor.Work.Right},{monitor.Work.Bottom}; menu={menuTop}; status={bottom}.");
        Assert.Multiple(() =>
        {
            Assert.That(bottom.Y, Is.LessThanOrEqualTo(monitor.Work.Bottom + 1), "The status bar must stay above the taskbar.");
            Assert.That(bottom.X, Is.LessThanOrEqualTo(monitor.Work.Right + 1), "A taskbar may also be on the right.");
            Assert.That(menuTop.X, Is.GreaterThanOrEqualTo(monitor.Work.Left - 1));
            Assert.That(menuTop.Y, Is.GreaterThanOrEqualTo(monitor.Work.Top - 1));
            Assert.That(status.ActualHeight, Is.GreaterThanOrEqualTo(24));
        });
    }

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static nint CaptionPosition(MainWindow window)
    {
        var area = window.MainMenuBar.CaptionDragArea;
        var point = area.PointToScreen(new Point(area.ActualWidth / 2, area.ActualHeight / 2));
        return new nint(((int)point.Y << 16) | ((int)point.X & 0xffff));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation information);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRectangle bounds);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendDpiMessage(nint hwnd, int message, nint wParam, ref NativeRectangle bounds);
}
