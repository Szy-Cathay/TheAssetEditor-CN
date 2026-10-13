using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AssetEditor.Views.Startup;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class StartupScreenLifecycleTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void StartupFailure_PreservesPathRepair_AndClosesWithoutAnExitButton(bool escape)
    {
        WithResources(() =>
        {
            StartupPackLoadingWindow? startup = null;
            var repaired = false;
            var explained = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (startup!.FailurePanel.IsVisible)
                {
                    timer.Stop();
                    explained = startup.FailurePanel.IsVisible && startup.FailureCloseHint.Text.Contains("Esc") &&
                        startup.ExitButton.Visibility == Visibility.Collapsed && startup.RetryButton.IsEnabled;
                    startup.CheckGamePathButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (escape)
                        startup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(startup),
                            Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    else
                        SystemCommands.CloseWindow(startup);
                }
            };
            startup = new StartupPackLoadingWindow((_, _) => throw new InvalidOperationException("Invalid game path."),
                _ => { }, () => repaired = true, (_, _) => Task.CompletedTask)
            {
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            startup.Resources["AeMotion.WindowExit"] = new Duration(TimeSpan.Zero);
            timer.Start();
            try { Assert.That(RunForTest(startup), Is.False); }
            finally { timer.Stop(); }
            Assert.That(repaired, Is.True);
            Assert.That(explained, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelDuringRead_WaitsForWorkerAndNeverRegistersPartialData(bool failureDuringCancellation)
    {
        WithResources(() =>
        {
            StartupPackLoadingWindow? startup = null;
            var registered = false;
            var workerStopped = false;
            startup = new StartupPackLoadingWindow((_, token) =>
            {
                startup!.Dispatcher.BeginInvoke(() => startup.ExitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.That(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)), Is.True);
                workerStopped = true;
                if (failureDuringCancellation)
                    throw new InvalidOperationException("Read failed after cancellation.");
                token.ThrowIfCancellationRequested();
                return null;
            }, _ => registered = true, () => { });
            Assert.That(RunForTest(startup), Is.False);
            Assert.That(workerStopped, Is.True);
            Assert.That(registered, Is.False);
        });
    }

    [Test]
    public void RetryWorkspaceFailure_DoesNotReadOrRegisterCaPacksAgain()
    {
        WithResources(() =>
        {
            var reads = 0;
            var registrations = 0;
            var preparations = 0;
            var workspaceFailureExplained = false;
            StartupPackLoadingWindow? startup = null;
            startup = new StartupPackLoadingWindow((_, _) =>
            {
                reads++;
                return new PackFileContainer("CA") { IsCaPackFile = true };
            }, _ => registrations++, () => { }, (_, _) =>
            {
                if (++preparations == 1)
                {
                    _ = startup!.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                        () =>
                        {
                            workspaceFailureExplained = startup.StartupOperationProgress.StatusText == "工作区打开失败" &&
                                startup.CheckGamePathButton.Visibility == Visibility.Collapsed;
                            startup.RetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        });
                    throw new InvalidOperationException("Workspace preparation failed.");
                }
                return Task.CompletedTask;
            });
            Assert.That(RunForTest(startup), Is.True);
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(registrations, Is.EqualTo(1));
            Assert.That(preparations, Is.EqualTo(2));
            Assert.That(workspaceFailureExplained, Is.True);
        });
    }

    [TestCase(WindowState.Normal)]
    [TestCase(WindowState.Maximized)]
    public void HandOff_RendersMainWindowBeforeClosingStartup_AndKeepsInputBlocked(WindowState state)
    {
        WithResources(() =>
        {
            var main = new Window { Width = 300, Height = 200, Left = -10000, Top = -10000, WindowState = state, ShowInTaskbar = false, Content = new Border() };
            var rendered = false;
            var firstRun = false;
            var readAfterRender = false;
            var visibleBeforePreparation = false;
            var renderedAtHandOff = false;
            var enabledAtHandOff = true;
            var startupVisibleAtHandOff = false;
            Exception? handoffFailure = null;
            main.ContentRendered += (_, _) => rendered = true;
            StartupPackLoadingWindow? startup = null;
            startup = new StartupPackLoadingWindow((_, _) =>
            {
                readAfterRender = firstRun;
                return new PackFileContainer("CA") { IsCaPackFile = true };
            }, _ => { }, () => { }, async (report, token) =>
            {
                visibleBeforePreparation = main.IsVisible;
                try
                {
                    await startup!.HandOffToAsync(main, token);
                }
                catch (Exception exception)
                {
                    handoffFailure = exception;
                    _ = startup!.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                        () => startup.ExitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    throw;
                }
                renderedAtHandOff = rendered;
                enabledAtHandOff = main.IsEnabled;
                startupVisibleAtHandOff = startup.IsVisible;
            }, () => firstRun = true);
            try
            {
                Assert.That(RunForTest(startup), Is.True, handoffFailure?.ToString());
                Assert.That(readAfterRender, Is.True);
                Assert.That(main.IsVisible, Is.True);
                Assert.That(main.WindowState, Is.EqualTo(state));
                Assert.That(startup.IsVisible, Is.False);
                Assert.That(visibleBeforePreparation, Is.False, "The main window appeared before preparation.");
                Assert.That(renderedAtHandOff, Is.True, "The main window had no rendered frame.");
                Assert.That(enabledAtHandOff, Is.False, "The main window accepted input during handoff.");
                Assert.That(startupVisibleAtHandOff, Is.True, "The startup window closed before handoff.");
            }
            finally
            {
                main.Close();
            }
        });
    }

    private static bool RunForTest(StartupPackLoadingWindow startup)
    {
        var timedOut = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            timedOut = true;
            startup.ExitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try
        {
            var operation = startup.RunAsync();
            var frame = new DispatcherFrame();
            operation.ContinueWith(_ => startup.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            var succeeded = operation.GetAwaiter().GetResult();
            Assert.That(timedOut, Is.False, startup.StartupOperationProgress.StatusText);
            return succeeded;
        }
        finally
        {
            timer.Stop();
        }
    }

    private static void WithResources(Action test)
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        using var services = new ServiceCollection().AddSingleton(localization).BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            Application.Current.MainWindow = null;
            test();
        });
    }
}
