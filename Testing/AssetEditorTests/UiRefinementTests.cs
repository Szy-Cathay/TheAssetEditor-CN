using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiRefinementTests
{
    [TestCase(GameTypeEnum.Warhammer3)]
    [TestCase(GameTypeEnum.Warhammer2)]
    public void RegisteredShortcuts_UseExistingCommands_AndKeepGameAndDisabledRestrictions(GameTypeEnum game)
    {
        var settings = new ApplicationSettingsService(game) { AllowSettingsUpdate = false };
        var creator = new Mock<IEditorCreator>();
        var factory = new Mock<IUiCommandFactory>();
        factory.Setup(item => item.Create(It.IsAny<Action<OpenEditorCommand>?>()))
            .Returns(new OpenEditorCommand(creator.Object, Mock.Of<IPackFileService>()));
        var services = new DependencyInjectionConfig(loadResources: false).Build(false, registrations =>
        {
            registrations.AddSingleton(settings);
            registrations.AddSingleton(factory.Object);
        });
        try
        {
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                services.GetRequiredService<LocalizationManager>().LoadLanguage();
                var menu = services.GetRequiredService<MenuBarViewModel>();
                var registrations = services.GetRequiredService<IEditorDatabase>().GetEditorInfos()
                    .Where(item => item.AddToolbarButton).OrderBy(item => item.ToolbarName).ToArray();
                Assert.That(menu.Editors.Count, Is.EqualTo(registrations.Length));
                for (var index = 0; index < registrations.Length; index++)
                {
                    var registration = registrations[index];
                    var shortcut = menu.Editors[index];
                    var enabled = registration.IsToolbarButtonEnabled &&
                        (registration.SupportedGames.Count == 0 || registration.SupportedGames.Contains(game));
                    Assert.That(shortcut.IsEnabled, Is.EqualTo(enabled), registration.ToolbarName);
                    Assert.That(shortcut.OpenEditorCommand.CanExecute(null), Is.EqualTo(enabled), registration.ToolbarName);
                    shortcut.OpenEditorCommand.Execute(null);
                    creator.Verify(item => item.Create(registration.EditorEnum, null), enabled ? Times.Once() : Times.Never());
                    Assert.That(shortcut.DisplayName, Does.Not.StartWith("DisplayName."));
                    NUnit.Framework.TestContext.WriteLine($"{game}: {shortcut.DisplayName}; enabled={enabled}; icon={shortcut.IconKind}");
                }
            });
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void EmptyWorkspace_ReflectsRecentProjects_AndInvokesTheirOpenCommand(ThemeType theme)
    {
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        var open = new Mock<IFolderProjectOpenService>();
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
                try
                {
                    ThemesController.SetTheme(theme);
                    var menu = services.GetRequiredService<MenuBarViewModel>();
                    var tabs = new CachedTabControl
                    {
                        Width = 640,
                        Height = 540,
                        Style = (Style)Application.Current.FindResource("AeShell.EditorTabs"),
                        DataContext = new { MenuBar = menu },
                    };
                    Arrange(tabs);
                    var empty = (TextBlock)tabs.Template.FindName("RecentProjectsEmpty", tabs);
                    var recent = (ListBox)tabs.Template.FindName("RecentProjectsList", tabs);
                    Assert.That(empty.Visibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(recent.Items, Is.Empty);
                    const string first = @"D:\测试工程\帝国士兵";
                    const string newest = @"D:\测试工程\这是一个很长的工程名称用于检查完整路径能够通过提示查看并且不会撑开面板";
                    settings.CurrentSettings.RecentFolderProjectPaths.Add(first);
                    settings.CurrentSettings.RecentFolderProjectPaths.Add(newest);
                    Arrange(tabs);
                    Assert.That(empty.Visibility, Is.EqualTo(Visibility.Collapsed));
                    Assert.That(recent.Items.Cast<RecentPackFileItem>().Select(item => item.Path), Is.EqualTo(new[] { newest, first }));
                    var row = (ListBoxItem)recent.ItemContainerGenerator.ContainerFromIndex(0);
                    var button = Descendants<Button>(row).Single();
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                    button.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    open.Verify(service => service.Open(newest), Times.Once);
                    Assert.That(button.ToolTip, Is.EqualTo(newest));
                    Assert.That(recent.ActualWidth, Is.LessThanOrEqualTo(tabs.ActualWidth));
                    var state = (FrameworkElement)tabs.Template.FindName("EmptyEditorState", tabs);
                    Assert.That(state.TranslatePoint(new Point(0, state.ActualHeight), tabs).Y, Is.LessThanOrEqualTo(tabs.ActualHeight));
                    foreach (var dpi in new[] { 96, 120, 144 })
                        SaveSnapshot(tabs, $"recent-projects-{theme}-{dpi}.png", dpi);
                    settings.CurrentSettings.RecentFolderProjectPaths.Clear();
                    Arrange(tabs);
                    Assert.That(empty.Visibility, Is.EqualTo(Visibility.Visible));
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
            });
        }
        finally
        {
            (services as IDisposable)?.Dispose();
        }
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
                yield return typed;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    internal static void SaveSnapshot(FrameworkElement root, string fileName, int dpi = 144)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi / 96),
            (int)Math.Ceiling(root.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        var surface = new DrawingVisual();
        using (var drawing = surface.RenderOpen())
        {
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            drawing.DrawRectangle((Brush)root.FindResource("AeBrush.Canvas"), null, bounds);
            drawing.DrawRectangle(new VisualBrush(root), null, bounds);
        }
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, fileName));
        encoder.Save(stream);
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
    }
}
