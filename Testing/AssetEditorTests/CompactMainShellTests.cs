using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
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
public class CompactMainShellTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void MainShell_KeepsRailAndResizedSidebar_AndCentersCompactStatus(ThemeType theme)
    {
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        settings.CurrentSettings.StartMaximised = false;
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
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -10000;
                window.Top = -10000;
                window.Width = 1100;
                window.Height = 720;
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var menu = window.MainMenuBar;
                    Assert.That(window.Template.FindName("titlebar", window), Is.Null);
                    Assert.That(window.WindowStyle, Is.EqualTo(WindowStyle.SingleBorderWindow));
                    Assert.That(menu.ActualHeight, Is.EqualTo(32));
                    Assert.That(WindowChrome.GetWindowChrome(window).CaptionHeight, Is.EqualTo(32));
                    var root = (FrameworkElement)window.Content;
                    var railTop = window.ActivityRail.TranslatePoint(new Point(), root).Y;
                    var railBottom = window.ActivityRail.TranslatePoint(new Point(0, window.ActivityRail.ActualHeight), root).Y;
                    var menuBottom = menu.TranslatePoint(new Point(0, menu.ActualHeight), root).Y;
                    var statusTop = window.ApplicationStatusBar.TranslatePoint(new Point(), root).Y;
                    var editorRight = window.EditorsTabControl.TranslatePoint(new Point(window.EditorsTabControl.ActualWidth, 0), root).X;
                    Assert.That(railTop, Is.EqualTo(menuBottom).Within(0.01));
                    Assert.That(railBottom, Is.EqualTo(statusTop).Within(0.01));
                    Assert.That(editorRight, Is.EqualTo(root.ActualWidth).Within(0.01));
                    Assert.That(menu.ApplicationMenu.Items.Cast<MenuItem>().Select(item => item.Header),
                        Does.Not.Contain("视图"));

                    var splitter = UiRefinementTests.Descendants<GridSplitter>(window).Single();
                    var grid = (Grid)VisualTreeHelper.GetParent(splitter);
                    var initialWidth = window.WorkspaceShell.ActualWidth;
                    splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter),
                        Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
                    window.UpdateLayout();
                    var resizedWidth = window.WorkspaceShell.ActualWidth;
                    Assert.That(resizedWidth, Is.GreaterThan(initialWidth));
                    var railWidth = window.ActivityRail.ActualWidth;
                    for (var cycle = 0; cycle < 4; cycle++)
                    {
                        Invoke(menu.SidebarToggle);
                        window.UpdateLayout();
                        Assert.That(model.IsPackFileExplorerVisible, Is.False);
                        Assert.That(window.WorkspaceShell.IsVisible, Is.False);
                        Assert.That(grid.ColumnDefinitions[1].ActualWidth, Is.Zero);
                        Assert.That(window.ActivityRail.IsVisible, Is.True);
                        Assert.That(window.ActivityRail.ActualWidth, Is.EqualTo(railWidth));
                        Assert.That(UiRefinementTests.Descendants<Button>(window.EditorShortcuts), Is.Not.Empty);
                        Invoke(menu.SidebarToggle);
                        window.UpdateLayout();
                        Assert.That(model.IsPackFileExplorerVisible, Is.True);
                        Assert.That(window.WorkspaceShell.ActualWidth, Is.EqualTo(resizedWidth).Within(1));
                        Assert.That(grid.ColumnDefinitions[2].ActualWidth, Is.EqualTo(1).Within(0.01));
                    }

                    var status = window.ApplicationStatusBar;
                    Assert.That(status.ActualHeight, Is.InRange(24, 26));
                    foreach (var text in UiRefinementTests.Descendants<TextBlock>(status).Where(item => item.IsVisible))
                    {
                        var top = text.TranslatePoint(new Point(), status).Y;
                        var bottom = status.ActualHeight - top - text.ActualHeight;
                        Assert.That(top, Is.GreaterThanOrEqualTo(3));
                        Assert.That(bottom, Is.GreaterThanOrEqualTo(3));
                        Assert.That(Math.Abs(top - bottom), Is.LessThanOrEqualTo(1.1));
                    }
                    foreach (var dpi in new[] { 96, 120, 144 })
                        UiRefinementTests.SaveSnapshot((FrameworkElement)window.Content, $"compact-shell-{theme}-{dpi}.png", dpi);

                    model.HasUpdateCheckFailure = true;
                    model.UpdateCheckStatus = "更新检查失败，请稍后重试。";
                    window.UpdateLayout();
                    Assert.That(window.UpdateCheckStatusText.IsVisible, Is.True);
                    Assert.That(status.ActualHeight, Is.GreaterThan(26));
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

    private static void Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        button.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
