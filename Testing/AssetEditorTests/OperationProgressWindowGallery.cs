using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Views.FolderProject;
using AssetEditor.Views.Startup;
using NUnit.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
[Category("DesktopPreview")]
public class OperationProgressWindowGallery
{
    [TestCase(ThemeType.DarkTheme, "startup")]
    [TestCase(ThemeType.LightTheme, "startup")]
    [TestCase(ThemeType.HighContrastDark, "startup")]
    [TestCase(ThemeType.HighContrastLight, "startup")]
    [TestCase(ThemeType.DarkTheme, "folder")]
    [TestCase(ThemeType.LightTheme, "folder")]
    [TestCase(ThemeType.DarkTheme, "host")]
    [TestCase(ThemeType.LightTheme, "host")]
    public void LoadingWindow_RecordsDesktopFrames(ThemeType theme, string workflow)
    {
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1")
            Assert.Ignore("Desktop recording requires an explicitly reserved GUI preview.");
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        Assert.That(output, Is.Not.Null.And.Not.Empty);
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            var previousMainWindow = Application.Current.MainWindow;
            ThemesController.SetTheme(theme);
            var host = new OperationProgressWindowHost
            {
                WindowTitle = "正在加载资源", StatusText = "正在读取测试资源",
                IsProgressIndeterminate = false, ProgressMaximum = 10,
            };
            var owner = new Window
            {
                Content = host, Width = 820, Height = 340, Left = 80, Top = 80,
                ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                WindowStyle = WindowStyle.None,
            };
            owner.SetResourceReference(Control.BackgroundProperty, "AeBrush.Surface3");
            var frames = new List<(long Time, BitmapSource Image)>();
            var stopwatch = new Stopwatch();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            timer.Tick += (_, _) => frames.Add((stopwatch.ElapsedMilliseconds, Capture(owner)));
            try
            {
                owner.Show();
                Application.Current.MainWindow = owner;
                Pump(TimeSpan.FromMilliseconds(120));
                stopwatch.Start();
                timer.Start();
                if (workflow == "startup")
                {
                    var window = new StartupPackLoadingWindow(report =>
                    {
                        for (var index = 1; index <= 10; index++)
                        {
                            report(new CaPackLoadProgress(CaPackLoadProgressStage.ReadingPacks,
                                $"test-resource-{index}.pack", index, 10));
                            Thread.Sleep(120);
                        }
                        return new PackFileContainer("All Game Packs") { IsCaPackFile = true };
                    }, _ => { }, () => { });
                    Assert.That(window.Run(), Is.True);
                }
                else if (workflow == "folder")
                {
                    var window = new FolderProjectProgressWindow("正在打开工程", "正在读取资源", report =>
                    {
                        for (var index = 1; index <= 10; index++)
                        {
                            report(new OperationProgressUpdate("正在读取测试资源", $"test-resource-{index}.bin", index, 10));
                            Thread.Sleep(120);
                        }
                        return null;
                    });
                    window.Run();
                }
                else
                {
                    host.IsOperationActive = true;
                    for (var index = 1; index <= 10; index++)
                    {
                        host.ProgressValue = index;
                        host.CurrentDetailText = $"test-resource-{index}.bin";
                        Pump(TimeSpan.FromMilliseconds(120));
                    }
                    var completed = host.CompleteAsync();
                    while (!completed.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(4))
                        Pump(TimeSpan.FromMilliseconds(16));
                    Assert.That(completed.IsCompletedSuccessfully, Is.True);
                }
                Pump(TimeSpan.FromMilliseconds(180));
                timer.Stop();
                Assert.That(frames.Count, Is.GreaterThan(15));
                Directory.CreateDirectory(output!);
                SaveFrames(frames, Path.Combine(output!, $"loading-{workflow}-{theme}"));
                NUnit.Framework.TestContext.WriteLine($"{workflow}/{theme}: {frames.Count} desktop frames, {stopwatch.ElapsedMilliseconds} ms; DPI {VisualTreeHelper.GetDpi(owner).PixelsPerInchX}.");
            }
            finally
            {
                timer.Stop();
                host.IsOperationActive = false;
                owner.Close();
                Application.Current.MainWindow = previousMainWindow;
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    internal static BitmapSource Capture(Window owner)
    {
        var origin = owner.PointToScreen(new Point());
        var dpi = VisualTreeHelper.GetDpi(owner);
        var width = (int)(owner.ActualWidth * dpi.DpiScaleX);
        var height = (int)(owner.ActualHeight * dpi.DpiScaleY);
        var desktop = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(desktop);
        var bitmap = CreateCompatibleBitmap(desktop, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            Assert.That(BitBlt(memory, 0, 0, width, height, desktop, (int)origin.X, (int)origin.Y, 0x40CC0020), Is.True);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, desktop);
        }
    }

    internal static void SaveFrames(List<(long Time, BitmapSource Image)> frames, string path)
    {
        var gif = new GifBitmapEncoder();
        for (var index = 0; index < frames.Count; index++)
        {
            var metadata = new BitmapMetadata("gif");
            var duration = index + 1 < frames.Count ? frames[index + 1].Time - frames[index].Time : 100;
            metadata.SetQuery("/grctlext/Delay", (ushort)Math.Max(1, duration / 10));
            gif.Frames.Add(BitmapFrame.Create(frames[index].Image, null, metadata, null));
        }
        using (var stream = File.Create(path + ".gif"))
            gif.Save(stream);

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 820, 590));
            var times = new long[] { 100, 510, 580, 750, 1230, frames[^1].Time };
            for (var index = 0; index < times.Length; index++)
            {
                var sample = frames.MinBy(frame => Math.Abs(frame.Time - times[index]));
                var x = index % 2 * 410;
                var y = index / 2 * 195;
                drawing.DrawText(new FormattedText($"{sample.Time} ms", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Black, 1), new Point(x + 8, y + 3));
                drawing.DrawImage(sample.Image, new Rect(x, y + 23, 410, 170));
            }
        }
        var sheet = new RenderTargetBitmap(820, 590, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using (var stream = File.Create(path + ".png"))
            encoder.Save(stream);
        File.WriteAllLines(path + "-frames.csv", new[] { "frame,milliseconds" }.Concat(frames.Select((frame, index) => $"{index},{frame.Time}")));
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
