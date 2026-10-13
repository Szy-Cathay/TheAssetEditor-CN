using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AssetEditor.Views.FolderProject;
using AssetEditor.Views.Startup;
using NUnit.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class OperationProgressWindowTransitionTests
{
    [TestCase(ThemeType.DarkTheme, true)]
    [TestCase(ThemeType.LightTheme, true)]
    [TestCase(ThemeType.HighContrastDark, true)]
    [TestCase(ThemeType.HighContrastLight, true)]
    [TestCase(ThemeType.DarkTheme, false)]
    [TestCase(ThemeType.LightTheme, false)]
    public void Transition_PresentsCompleteFramesAndAwaitsFadeOut(ThemeType theme, bool animate)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices, () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                ThemesController.SetTheme(theme);
                var window = new Window
                {
                    Width = 700, Height = 200, Left = -10000, Top = -10000,
                    ShowActivated = false, ShowInTaskbar = false,
                    Content = new Border(),
                };
                window.SetResourceReference(FrameworkElement.StyleProperty, "CustomWindowStyle");
                var transition = new OperationProgressWindowTransition(window, () => animate);
                try
                {
                    transition.Show();
                    window.Show();
                    Assert.That(ReadAlpha(window), Is.Zero, "Native visibility must wait for the first WPF frame.");
                    if (animate)
                        PumpUntil(() => ReadAlpha(window) is > 0 and < 255);
                    PumpUntil(() => ReadAlpha(window) == 255);
                    Assert.That(window.AllowsTransparency, Is.True);
                    Assert.That(window.IsHitTestVisible, Is.True);
                    var surface = (FrameworkElement)window.Template.FindName("WindowSurface", window);
                    var scale = (ScaleTransform)surface.RenderTransform;
                    Assert.That(scale.ScaleX, Is.EqualTo(1).Within(0.001));
                    Assert.That(surface.RenderTransformOrigin, Is.EqualTo(new Point(0.5, 0.5)));
                    var width = window.ActualWidth;
                    var height = window.ActualHeight;

                    var completed = transition.HideAsync();
                    Assert.That(completed.IsCompleted, Is.EqualTo(!animate));
                    PumpUntil(() => completed.IsCompleted);
                    Assert.That(ReadAlpha(window), Is.Zero);
                    Assert.That(window.IsHitTestVisible, Is.False);
                    Assert.That(window.Opacity, Is.Zero);
                    Assert.That(scale.ScaleX, Is.EqualTo(0.96));
                    Assert.That(scale.ScaleY, Is.EqualTo(scale.ScaleX));
                    Assert.That(window.ActualWidth, Is.EqualTo(width));
                    Assert.That(window.ActualHeight, Is.EqualTo(height));

                    transition.Show();
                    PumpUntil(() => ReadAlpha(window) == 255);
                    transition.HideImmediately();
                    Assert.That(ReadAlpha(window), Is.Zero);
                }
                finally
                {
                    window.Close();
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void Transition_RestartAndOwnerClose_StopPendingAnimations()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices, () =>
            {
                var owner = new Window { Left = -10000, Top = -10000, Width = 400, Height = 300, ShowActivated = false };
                var window = new Window { Content = new Border(), Left = -10000, Top = -10000, Width = 200, Height = 100, ShowActivated = false };
                var transition = new OperationProgressWindowTransition(window, () => true);
                try
                {
                    owner.Show();
                    window.Owner = owner;
                    window.Show();
                    transition.Show();
                    PumpUntil(() => ReadAlpha(window) == 255);
                    var interrupted = transition.HideAsync();
                    transition.Show();
                    PumpUntil(() => interrupted.IsCompleted && ReadAlpha(window) == 255);
                    var closing = transition.HideAsync();
                    owner.Close();
                    Assert.That(closing.IsCompleted, Is.True);
                    Assert.That(window.IsVisible, Is.False);
                    transition.Show();
                    Assert.That(transition.HideAsync().IsCompleted, Is.True);
                }
                finally
                {
                    owner.Close();
                }
            });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FolderOperation_FailureOrCancellation_ClosesBeforeReturning(bool cancel)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices, () =>
            {
                FolderProjectProgressWindow? window = null;
                window = new FolderProjectProgressWindow("测试", "读取资源", (report, token) =>
                {
                    window!.Dispatcher.Invoke(() => window.Close());
                    if (cancel)
                        token.ThrowIfCancellationRequested();
                    throw new InvalidDataException("Test failure");
                }, CancellationToken.None)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, ShowActivated = false,
                };
                if (cancel)
                    Assert.That(window.RunCancelable().Cancelled, Is.True);
                else
                    Assert.Throws<InvalidDataException>(() => window.RunCancelable());
                Assert.That(window.IsVisible, Is.False);
            });
    }

    [Test]
    public void ShortStartup_UsesPerPixelTransparencyToHideTheNativeWindow()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices, () =>
            {
                StartupPackLoadingWindow? window = null;
                var surfaceOpacity = 1d;
                var usesPerPixelOpacity = false;
                window = new StartupPackLoadingWindow(report =>
                {
                    window!.Dispatcher.Invoke(() =>
                    {
                        surfaceOpacity = window.Opacity;
                        usesPerPixelOpacity = HwndSource.FromHwnd(
                            new WindowInteropHelper(window).Handle).CompositionTarget.UsesPerPixelOpacity;
                    });
                    return new PackFileContainer("All Game Packs") { IsCaPackFile = true };
                }, _ => { }, () => { })
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                };

                Assert.That(window.Run(), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(surfaceOpacity, Is.Zero);
                    Assert.That(usesPerPixelOpacity, Is.True,
                        "Fading an opaque HWND exposes its unpainted background.");
                });
            });
    }

    private static byte ReadAlpha(Window window)
    {
        Assert.That(HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
            .CompositionTarget.UsesPerPixelOpacity, Is.True);
        return (byte)Math.Round(window.Opacity * 255);
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) =>
        {
            if (completed() || elapsed.Elapsed > TimeSpan.FromSeconds(3))
                frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.That(completed(), Is.True, "The transition did not settle.");
    }

}
