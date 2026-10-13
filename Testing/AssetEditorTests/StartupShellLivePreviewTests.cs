using System.Diagnostics;
using System.Windows;
using CancelEventArgs = System.ComponentModel.CancelEventArgs;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
using AssetEditor.Views.Startup;
using Editors.Audio.AudioExplorer;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.Common.OperationProgress;
using Assert = NUnit.Framework.Assert;
using TestContext = NUnit.Framework.TestContext;

namespace AssetEditorTests;

[NonParallelizable]
[Category("DesktopPreview")]
public class StartupShellLivePreviewTests
{
    [TestCase(ThemeType.DarkTheme, true)]
    [TestCase(ThemeType.LightTheme, false)]
    public void InstalledGame_StartsWithRealResources_AndRendersMainWorkspace(ThemeType theme, bool startMaximized)
    {
        var data = Environment.GetEnvironmentVariable("AE_STARTUP_GAME_DATA");
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1" || string.IsNullOrEmpty(data))
            Assert.Ignore("Requires a reserved desktop and AE_STARTUP_GAME_DATA.");
        Assert.That(output, Is.Not.Null.And.Not.Empty);
        Directory.CreateDirectory(output!);
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        settings.CurrentSettings.StartMaximised = startMaximized;
        settings.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair { Game = GameTypeEnum.Warhammer3, Path = data! });
        var services = new DependencyInjectionConfig().Build(false,
            registrations => registrations.AddSingleton(settings));
        var localization = services.GetRequiredService<LocalizationManager>();
        localization.LoadLanguage();
        var packFiles = services.GetRequiredService<IPackFileService>();
        var loader = services.GetRequiredService<IPackFileContainerLoader>();

        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            Application.Current.MainWindow = null;
            ThemesController.SetTheme(theme);
            MainWindow? main = null;
            MainViewModel? model = null;
            StartupPackLoadingWindow? startup = null;
            var plate = new Window
            {
                Width = Math.Min(1000, SystemParameters.WorkArea.Width - 32), Height = Math.Min(620, SystemParameters.WorkArea.Height - 32), Left = 20, Top = 20,
                WindowStyle = WindowStyle.None, ShowActivated = false,
                ShowInTaskbar = false, Topmost = true, Content = new Border(),
            };
            plate.SetResourceReference(Control.BackgroundProperty, "AeBrush.Surface3");
            Exception? preparationFailure = null;
            var loads = 0;
            var registrations = 0;
            var realCountSeen = false;
            var clock = Stopwatch.StartNew();
            var lastTick = 0L;
            var longestGap = 0L;
            var lastFrame = -1000L;
            var frames = new List<(long Time, BitmapSource Image)>();
            var savedStartup = false;
            var trace = new List<string> { "milliseconds,opacity,completed,total,status" };
            startup = new StartupPackLoadingWindow((report, token) =>
            {
                loads++;
                return loader.LoadAllCaFiles(GameTypeEnum.Warhammer3, report, token);
            }, container =>
            {
                registrations++;
                packFiles.AddContainer(container);
            }, () => { }, async (report, token) =>
            {
                try
                {
                    model = services.GetRequiredService<MainViewModel>();
                    // The preview runs inside a test host with a different entry-assembly version.
                    var appVersion = FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location).ProductVersion!.Split('+', 2)[0];
                    model.ApplicationTitle = localization.GetFormat("Title.AppTitle", appVersion);
                    await model.FileTree.InitializeCaFilesAsync(token, (completed, total) => report(
                        new OperationProgressUpdate(localization.Get("StartupPackLoading.BuildingTree"), null, completed, total)));
                    main = services.GetRequiredService<MainWindow>();
                    main.DataContext = model;
                    main.Width = Math.Min(1280, SystemParameters.WorkArea.Width - 32);
                    main.Height = Math.Min(800, SystemParameters.WorkArea.Height - 32);
                    main.Topmost = true;
                    Application.Current.MainWindow = main;
                    await startup!.HandOffToAsync(main, token);
                }
                catch (Exception exception)
                {
                    preparationFailure = exception;
                    throw;
                }
            });
            plate.Show();
            startup.Owner = plate;
            startup.WindowStartupLocation = WindowStartupLocation.Manual;
            startup.Left = 40;
            startup.Top = 40;
            startup.MaxHeight = Math.Min(startup.MaxHeight, plate.Height - 40);
            startup.Topmost = true;
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                var now = clock.ElapsedMilliseconds;
                longestGap = Math.Max(longestGap, now - lastTick);
                lastTick = now;
                var progress = startup.StartupOperationProgress;
                realCountSeen |= progress.ProgressValue > 0 && progress.ProgressMaximum > 0;
                trace.Add(FormattableString.Invariant($"{now},{startup.Opacity:F3},{progress.ProgressValue},{progress.ProgressMaximum},{progress.StatusText}"));
                if (startup.FailurePanel.Visibility == Visibility.Visible || now > 180000)
                    startup.ExitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (startup.IsVisible && frames.Count < 140 && now - lastFrame > 150)
                {
                    frames.Add((now, OperationProgressWindowGallery.Capture(plate)));
                    lastFrame = now;
                    if (!savedStartup && startup.Opacity >= 0.99)
                    {
                        SavePng(OperationProgressWindowGallery.Capture(startup), Path.Combine(output!, $"startup-{theme}-window.png"));
                        savedStartup = true;
                    }
                }
            };
            try
            {
                timer.Start();
                var run = startup.RunAsync();
                PumpUntil(() => run.IsCompleted);
                timer.Stop();
                Assert.That(run.GetAwaiter().GetResult(), Is.True, preparationFailure?.ToString() ?? startup.StartupOperationProgress.StatusText);
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(registrations, Is.EqualTo(1));
                Assert.That(realCountSeen, Is.True);
                Assert.That(packFiles.GetAllPackfileContainers(), Has.Count.EqualTo(1));
                Assert.That(main!.IsVisible, Is.True);
                Assert.That(main.WindowState, Is.EqualTo(startMaximized ? WindowState.Maximized : WindowState.Normal));
                Assert.That(model!.FileTree.Files.Single().GetAllChildFileNodes(), Is.Not.Empty);
                main.IsEnabled = true;
                main.Activate();
                main.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(main);
                var content = (FrameworkElement)main.Content;
                var rendered = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX),
                    (int)(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var surface = new DrawingVisual();
                using (var drawing = surface.RenderOpen())
                {
                    var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                    drawing.DrawRectangle(main.Background, null, bounds);
                    drawing.DrawRectangle(new VisualBrush(content), null, bounds);
                }
                rendered.Render(surface);
                SavePng(rendered, Path.Combine(output!, $"main-{theme}.png"));
                var container = packFiles.GetAllPackfileContainers().Single();
                TestContext.WriteLine($"Sources: {container.SourcePackFilePaths.Count}; files: {container.FileList.Count}; elapsed: {clock.ElapsedMilliseconds} ms; heartbeat maximum: {longestGap} ms; DPI: {VisualTreeHelper.GetDpi(main).PixelsPerInchX}.");
                if (theme == ThemeType.DarkTheme && Environment.GetEnvironmentVariable("AE_UI_SHORTCUT_PREVIEW") == "1")
                    VerifyWorkspaceInteractions(main, model, services, settings, output!);
            }
            finally
            {
                timer.Stop();
                File.WriteAllLines(Path.Combine(output!, $"startup-{theme}-trace.csv"), trace);
                if (frames.Count > 0)
                {
                    OperationProgressWindowGallery.SaveFrames(frames, Path.Combine(output!, $"startup-{theme}"));
                    SavePng(frames[frames.Count / 2].Image, Path.Combine(output!, $"startup-{theme}-visible.png"));
                }
                if (main != null)
                {
                    main.DataContext = null;
                    main.Close();
                }
                model?.FileTree.Dispose();
                if (startup.IsVisible)
                    startup.Close();
                plate.Close();
                ThemesController.SetTheme(previousTheme);
                Application.Current.MainWindow = null;
                (services as IDisposable)?.Dispose();
            }
        });
    }

    private static void VerifyWorkspaceInteractions(MainWindow main, MainViewModel model,
        IServiceProvider services, ApplicationSettingsService settings, string output)
    {
        var packFiles = services.GetRequiredService<IPackFileService>();
        var projectPath = Path.Combine(output, $"界面验收工程-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectPath);
        using (var created = FolderProjectContainer.Create(projectPath, new FolderProjectSettings
        {
            Name = "界面验收工程", GameVersion = GameTypeEnum.Warhammer3,
        }))
        {
            services.GetRequiredService<IFolderProjectHistoryService>().Initialize(projectPath);
        }
        settings.CurrentSettings.RecentFolderProjectPaths.Add(projectPath);
        main.UpdateLayout();
        var tabs = (CachedTabControl)main.FindName("EditorsTabControl");
        var recent = (ListBox)tabs.Template.FindName("RecentProjectsList", tabs);
        var recentButton = UiRefinementTests.Descendants<Button>((DependencyObject)recent.ItemContainerGenerator.ContainerFromIndex(0)).Single();
        Invoke(recentButton);
        Invoke(recentButton);
        Assert.That(packFiles.GetAllPackfileContainers().OfType<FolderProjectContainer>().Count(), Is.EqualTo(1));
        Assert.That(packFiles.GetEditablePack(), Is.TypeOf<FolderProjectContainer>());
        TestContext.WriteLine("Recent project: two real button invocations opened one editable project.");

        main.WindowState = WindowState.Normal;
        main.Width = 1100;
        main.Height = 720;
        VerifyWindowChrome(main);
        foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
        {
            ThemesController.SetTheme(theme);
            main.UpdateLayout();
            foreach (var scale in new[] { 96, 120, 144 })
            {
                ApplyWindowDpi(main, scale);
                main.UpdateLayout();
                Assert.That(VisualTreeHelper.GetDpi(main).PixelsPerInchX, Is.EqualTo(scale));
                Assert.That(main.ApplicationStatusBar.ActualHeight, Is.InRange(24, 26));
                foreach (var text in UiRefinementTests.Descendants<TextBlock>(main.ApplicationStatusBar).Where(item => item.IsVisible))
                {
                    var top = text.TranslatePoint(new Point(), main.ApplicationStatusBar).Y;
                    var bottom = main.ApplicationStatusBar.ActualHeight - top - text.ActualHeight;
                    Assert.That(top, Is.GreaterThanOrEqualTo(3));
                    Assert.That(bottom, Is.GreaterThanOrEqualTo(3));
                }
                UiRefinementTests.SaveSnapshot((FrameworkElement)main.Content, $"workspace-{theme}-{scale}.png", scale);
            }
        }
        ThemesController.SetTheme(ThemeType.DarkTheme);
        var splitter = UiRefinementTests.Descendants<GridSplitter>(main).Single();
        var grid = (Grid)VisualTreeHelper.GetParent(splitter);
        Assert.That(grid.ColumnDefinitions[2].ActualWidth, Is.EqualTo(1).Within(0.01));
        var initialWidth = grid.ColumnDefinitions[1].ActualWidth;
        splitter.Focus();
        splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter), Environment.TickCount, Key.Right)
        { RoutedEvent = Keyboard.KeyDownEvent });
        main.UpdateLayout();
        Assert.That(grid.ColumnDefinitions[1].ActualWidth, Is.GreaterThan(initialWidth));
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(2000, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(2000, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        main.UpdateLayout();
        Assert.That(grid.ColumnDefinitions[3].ActualWidth, Is.GreaterThanOrEqualTo(360));
        model.FileTreeColumnWidth = new GridLength(initialWidth);
        main.UpdateLayout();

        for (var cycle = 0; cycle < 5; cycle++)
        {
            Invoke(main.MainMenuBar.SidebarToggle);
            main.UpdateLayout();
            Assert.That(main.WorkspaceShell.IsVisible, Is.False);
            Assert.That(main.ActivityRail.IsVisible, Is.True);
            Assert.That(grid.ColumnDefinitions[1].ActualWidth, Is.Zero);
            Invoke(main.MainMenuBar.SidebarToggle);
            main.UpdateLayout();
            Assert.That(main.WorkspaceShell.ActualWidth, Is.EqualTo(initialWidth).Within(1));
        }
        Invoke(main.MainMenuBar.SidebarToggle);
        main.UpdateLayout();
        UiRefinementTests.SaveSnapshot((FrameworkElement)main.Content, "workspace-sidebar-collapsed.png", 144);
        TestContext.WriteLine("Sidebar: five collapse/expand cycles preserved width; the editor rail remained visible.");

        var shortcuts = (ItemsControl)main.FindName("EditorShortcuts");
        var buttons = UiRefinementTests.Descendants<Button>(shortcuts).ToArray();
        Assert.That(buttons.Length, Is.EqualTo(model.MenuBar.Editors.Count));
        foreach (var button in buttons)
        {
            var shortcut = (EditorShortcutViewModel)button.DataContext;
            Assert.That(AutomationProperties.GetName(button), Is.EqualTo(shortcut.DisplayName));
            Assert.That(button.IsEnabled, Is.EqualTo(shortcut.IsEnabled));
            if (!shortcut.IsEnabled)
            {
                TestContext.WriteLine($"Disabled shortcut preserved: {shortcut.DisplayName}");
                continue;
            }
            button.BringIntoView();
            Invoke(button);
            main.UpdateLayout();
            var editor = model.EditorManager.GetAllEditors().Single();
            if (editor is AudioExplorerViewModel audio)
                PumpUntil(() => !audio.IsLoading && main.OwnedWindows.Count == 0);
            else
                main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(shortcut.IsActive, Is.True, shortcut.DisplayName);
            Assert.That(tabs.SelectedItem, Is.SameAs(editor));
            Assert.That(model.MenuBar.Editors.Count(item => item.IsActive), Is.EqualTo(1));
            main.UpdateLayout();
            VerifySplitterGaps(main);
            UiRefinementTests.SaveSnapshot((FrameworkElement)main.Content, $"editor-{shortcut.IconKind}.png");
            TestContext.WriteLine($"Opened from sidebar: {shortcut.DisplayName}; {editor.GetType().Name}.");
            model.EditorManager.CloseTool(editor);
            main.UpdateLayout();
            Assert.That(model.MenuBar.Editors.Any(item => item.IsActive), Is.False);
        }
        Invoke(main.MainMenuBar.SidebarToggle);
        main.UpdateLayout();
        var crow = packFiles.GetAllPackfileContainers().Where(item => item.IsCaPackFile)
            .SelectMany(item => item.FileList).Single(item => item.Key.EndsWith("emp_advisor_crow_01.rigid_model_v2", StringComparison.OrdinalIgnoreCase));
        var kitbash = model.EditorManager.CreateFromFile(crow.Value, EditorEnums.Kitbash_Editor);
        try
        {
            foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
            {
                ThemesController.SetTheme(theme);
                main.UpdateLayout();
                var frames = 0;
                void Rendered(object? sender, EventArgs args) => frames++;
                CompositionTarget.Rendering += Rendered;
                try { PumpUntil(() => frames >= 3); }
                finally { CompositionTarget.Rendering -= Rendered; }
                VerifySplitterGaps(main);
                SavePng(OperationProgressWindowGallery.Capture(main), Path.Combine(output, $"kitbash-native-{theme}.png"));
                foreach (var scale in new[] { 96, 120, 144 })
                    UiRefinementTests.SaveSnapshot((FrameworkElement)main.Content, $"kitbash-{theme}-{scale}.png", scale);
            }
            Assert.That(((ISaveableEditor)kitbash).HasUnsavedChanges, Is.False);
            TestContext.WriteLine($"Kitbash preview: {crow.Key}; all visible splitter layout tracks are 1 DIP.");
        }
        finally
        {
            model.EditorManager.CloseTool(kitbash);
            ThemesController.SetTheme(ThemeType.DarkTheme);
        }
        UiRefinementTests.SaveSnapshot((FrameworkElement)main.Content, "workspace-after-editor-cycle.png");
    }

    private static void VerifySplitterGaps(Window window)
    {
        foreach (var splitter in UiRefinementTests.Descendants<GridSplitter>(window).Where(item => item.IsVisible))
        {
            var grid = (Grid)VisualTreeHelper.GetParent(splitter);
            var gap = splitter.ResizeDirection == GridResizeDirection.Columns
                ? grid.ColumnDefinitions[Grid.GetColumn(splitter)].ActualWidth
                : grid.RowDefinitions[Grid.GetRow(splitter)].ActualHeight;
            Assert.That(gap, Is.EqualTo(1).Within(0.01), window.Title);
        }
    }

    private static void VerifyWindowChrome(MainWindow main)
    {
        main.UpdateLayout();
        var handle = new WindowInteropHelper(main).Handle;
        var menu = main.MainMenuBar;
        nint Position(FrameworkElement element)
        {
            var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            return new nint(((int)point.Y << 16) | ((int)point.X & 0xffff));
        }
        Assert.That(SendMessage(handle, 0x0084, 0, Position(menu.CaptionDragArea)), Is.EqualTo(new nint(2)),
            "The free menu area must be a native caption for dragging and the system menu.");
        foreach (var element in new FrameworkElement[] { menu.SidebarToggle, menu.ApplicationMenu, menu.MinimizeButton, menu.MaximizeRestoreButton, menu.CloseButton })
            Assert.That(SendMessage(handle, 0x0084, 0, Position(element)), Is.EqualTo(new nint(1)), element.Name);

        Invoke(menu.MaximizeRestoreButton);
        Assert.That(main.WindowState, Is.EqualTo(WindowState.Maximized));
        Invoke(menu.MaximizeRestoreButton);
        Assert.That(main.WindowState, Is.EqualTo(WindowState.Normal));
        Invoke(menu.MinimizeButton);
        Assert.That(main.WindowState, Is.EqualTo(WindowState.Minimized));
        SystemCommands.RestoreWindow(main);
        PumpUntil(() => main.WindowState == WindowState.Normal);
        main.Activate();
        main.UpdateLayout();
        SendMessage(handle, 0x00A3, 2, Position(menu.CaptionDragArea));
        PumpUntil(() => main.WindowState == WindowState.Maximized);
        SendMessage(handle, 0x00A3, 2, Position(menu.CaptionDragArea));
        PumpUntil(() => main.WindowState == WindowState.Normal);

        var requestedClose = false;
        void CancelClose(object? sender, CancelEventArgs e) { requestedClose = true; e.Cancel = true; }
        var model = main.DataContext;
        main.DataContext = null;
        main.Closing += CancelClose;
        try
        {
            Invoke(menu.CloseButton);
            Assert.That(requestedClose, Is.True);
            Assert.That(main.IsVisible, Is.True, "Closing handlers must be able to cancel a close request.");
        }
        finally
        {
            main.Closing -= CancelClose;
            main.DataContext = model;
        }
        main.UpdateLayout();
        TestContext.WriteLine("Window chrome: native caption/client hit tests, maximize/restore, minimize, double-click and cancellable close passed.");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    private static void ApplyWindowDpi(Window window, int dpi)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert.That(GetWindowRect(handle, out var bounds), Is.True);
        SendMessage(handle, 0x02E0, new nint(dpi | (dpi << 16)), ref bounds);
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRectangle bounds);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, ref NativeRectangle bounds);

    private static void Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        button.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var clock = Stopwatch.StartNew();
        while (!completed() && clock.Elapsed < TimeSpan.FromMinutes(4))
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
        Assert.That(completed(), Is.True, "Startup did not complete within four minutes.");
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
