using System.Diagnostics;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Views.Startup;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class StartupArtworkLayoutTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void Startup_UsesTransparentIconAndNativeFeedback_OnLightAndDarkBackgrounds(ThemeType theme)
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        using var services = new ServiceCollection().AddSingleton(localization).BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var previous = ThemesController.CurrentTheme;
            Application.Current.MainWindow = null;
            ThemesController.SetTheme(theme);
            var startup = new StartupPackLoadingWindow((_, token) =>
            {
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                token.ThrowIfCancellationRequested();
                return null;
            }, _ => { }, () => { }, (_, _) => Task.CompletedTask)
            {
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            var run = startup.RunAsync();
            try
            {
                PumpUntil(() => startup.Opacity == 1);
                startup.StartupOperationProgress.Report(new OperationProgressUpdate("正在读取资源", "test.pack", 1, 4));
                startup.UpdateLayout();
                Assert.That(startup.AllowsTransparency, Is.True);
                Assert.That(((SolidColorBrush)startup.Background).Color.A, Is.Zero);
                Assert.That(startup.StartupImage.Source.Width / startup.StartupImage.Source.Height, Is.EqualTo(1));
                Assert.That(startup.StartupImage.ActualWidth, Is.EqualTo(startup.StartupImage.ActualHeight));
                Assert.That(new TextRange(startup.StartupIdentity.ContentStart, startup.StartupIdentity.ContentEnd).Text,
                    Is.EqualTo("AssetEditorCN"));
                Assert.That(startup.StartupOperationProgress.StatusText, Is.EqualTo("正在读取资源"));
                Assert.That(startup.StartupOperationProgress.CurrentDetailText, Is.EqualTo("test.pack"));
                Assert.That(startup.StartupOperationProgress.ProgressValue, Is.EqualTo(1));
                Assert.That(startup.StartupOperationProgress.ProgressMaximum, Is.EqualTo(4));
                Assert.That(startup.StartupIdentity.ActualWidth, Is.LessThan(startup.StartupLayout.ActualWidth));

                foreach (var dpi in new[] { 96, 120, 144 })
                {
                    var bitmap = Render(startup.StartupLayout, dpi);
                    Save(bitmap, $"startup-transparent-{theme}-{dpi}.png");
                    Save(OnBackground(bitmap, Brushes.White), $"startup-white-{theme}-{dpi}.png");
                    Save(OnBackground(bitmap, Brushes.Black), $"startup-black-{theme}-{dpi}.png");
                    Assert.That(Alpha(bitmap, 0.001, 0.001), Is.Zero);
                    Assert.That(Alpha(bitmap, 0.999, 0.001), Is.Zero);
                    Assert.That(Alpha(bitmap, 0.001, 0.999), Is.Zero);
                    Assert.That(Alpha(bitmap, 0.999, 0.999), Is.Zero);
                    // The approved PNG intentionally contains near-opaque interior pixels.
                    Assert.That(Alpha(bitmap, 0.5, 0.35), Is.GreaterThan(240));
                }

                Assert.That(startup.StartupOperationProgress.DetailsExpander.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(startup.ExitButton.Visibility, Is.EqualTo(Visibility.Collapsed));
                startup.FailurePanel.Visibility = Visibility.Visible;
                startup.MaxHeight = 420;
                startup.UpdateLayout();
                startup.FailureCloseHint.BringIntoView();
                startup.UpdateLayout();
                var viewport = (FrameworkElement)startup.Content;
                var hint = startup.FailureCloseHint.TranslatePoint(new Point(0, startup.FailureCloseHint.ActualHeight), viewport);
                Assert.That(hint.Y, Is.LessThanOrEqualTo(viewport.ActualHeight + 1), "The close hint must remain reachable in a small work area.");
                Assert.That(startup.RetryButton.IsVisible, Is.True);
                Assert.That(startup.CheckGamePathButton.IsVisible, Is.True);
                Save(Render(startup.StartupLayout, 144), $"startup-failure-{theme}.png");
            }
            finally
            {
                startup.Close();
                PumpUntil(() => run.IsCompleted);
                ThemesController.SetTheme(previous);
            }
        });
    }

    [Test]
    public void ApplicationIcon_ContainsAllWindowsSizes_WithTransparentCorners()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/AssetEditor.CN;component/AssetEditor.CN.ico")).Stream;
            var icon = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.That(icon.Frames.Select(frame => frame.PixelWidth), Is.EquivalentTo(new[] { 16, 24, 32, 48, 64, 128, 256 }));
            foreach (var frame in icon.Frames)
            {
                var pixels = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                Assert.That(frame.PixelWidth, Is.EqualTo(frame.PixelHeight));
                Assert.That(Alpha(pixels, 0, 0), Is.Zero);
                Assert.That(Alpha(pixels, 0.999, 0.999), Is.Zero);
                Assert.That(Alpha(pixels, 0.5, 0.5), Is.GreaterThan(240));
            }
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [Category("DesktopPreview")]
    public void StartupText_RendersOnDesktopAtWindowDpi(ThemeType theme)
    {
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1")
            Assert.Ignore("Requires a reserved desktop.");
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        using var services = new ServiceCollection().AddSingleton(localization).BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var previous = ThemesController.CurrentTheme;
            Application.Current.MainWindow = null;
            ThemesController.SetTheme(theme);
            var plate = new Window
            {
                Left = 20, Top = 20, Width = 900, Height = 640,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                Background = (Brush)Application.Current.FindResource("AeBrush.Surface3"), Content = new Border(),
            };
            var startup = new StartupPackLoadingWindow((_, token) =>
            {
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                token.ThrowIfCancellationRequested();
                return null;
            }, _ => { }, () => { }, (_, _) => Task.CompletedTask)
            {
                Left = 40, Top = 40, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            Task<bool>? run = null;
            try
            {
                plate.Show();
                startup.Owner = plate;
                run = startup.RunAsync();
                PumpUntil(() => startup.Opacity == 1);
                startup.StartupOperationProgress.Report(new OperationProgressUpdate("正在读取游戏资源", "terrain2.pack", 135, 271));
                var handle = new WindowInteropHelper(startup).Handle;
                var nativeDpi = GetDpiForWindow(handle);
                foreach (var dpi in new[] { 96, 120, 144 })
                {
                    var bounds = new NativeRectangle
                    {
                        Left = 90, Top = 90,
                        Right = 90 + (int)Math.Ceiling(startup.Width * dpi / 96),
                        Bottom = 90 + (int)Math.Ceiling(startup.ActualHeight * dpi / 96),
                    };
                    // Exercise WPF window DPI without changing the user's display settings.
                    SendDpiMessage(handle, 0x02E0, new nint(dpi | (dpi << 16)), ref bounds);
                    startup.UpdateLayout();
                    startup.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var settled = Stopwatch.StartNew();
                    PumpUntil(() => settled.ElapsedMilliseconds >= 180);
                    Assert.That(VisualTreeHelper.GetDpi(startup).PixelsPerInchX, Is.EqualTo(dpi));
                    Assert.That(startup.StartupIdentity.Effect, Is.Null);
                    Assert.That(startup.StartupOperationProgress.Effect, Is.Null);
                    var text = ((SolidColorBrush)startup.StartupIdentity.Foreground).Color;
                    var background = ((SolidColorBrush)plate.Background).Color;
                    Assert.That(Math.Abs(text.R - background.R), Is.GreaterThan(140));
                    Save(OperationProgressWindowGallery.Capture(startup), $"startup-text-desktop-{theme}-{dpi}.png");
                    Save(Render(startup.StartupIdentity, dpi), $"startup-title-alpha-{theme}-{dpi}.png");
                    NUnit.Framework.TestContext.WriteLine($"Window DPI {dpi}; monitor DPI {nativeDpi}; theme {theme}; foreground {text}; background {background}.");
                }
            }
            finally
            {
                startup.Close();
                if (run != null)
                    PumpUntil(() => run.IsCompleted);
                plate.Close();
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendDpiMessage(nint hwnd, int message, nint wParam, ref NativeRectangle bounds);

    private static RenderTargetBitmap OnBackground(BitmapSource bitmap, Brush background)
    {
        var result = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, bitmap.Width, bitmap.Height);
            context.DrawRectangle(background, null, bounds);
            context.DrawImage(bitmap, bounds);
        }
        result.Render(drawing);
        return result;
    }

    private static RenderTargetBitmap Render(FrameworkElement view, int dpi)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi / 96),
            (int)Math.Ceiling(view.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(drawing);
        return bitmap;
    }

    private static byte Alpha(BitmapSource bitmap, double x, double y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(bitmap.PixelWidth * x), (int)(bitmap.PixelHeight * y), 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private static void Save(BitmapSource bitmap, string name)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, name));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var clock = Stopwatch.StartNew();
        while (!complete() && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
        Assert.That(complete(), Is.True, "The startup window did not reach the expected state.");
    }
}
