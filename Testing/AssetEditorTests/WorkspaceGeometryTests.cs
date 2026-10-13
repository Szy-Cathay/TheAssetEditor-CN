using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
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
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class WorkspaceGeometryTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void WorkspaceBorders_StayAlignedThroughDocumentAndSidebarChanges(ThemeType theme) => Verify(theme, false);

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [Category("DesktopPreview")]
    public void InstalledResourceTree_UsesNarrowScrollSlotsAndAlignedWorkspace(ThemeType theme)
    {
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1"
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AE_STARTUP_GAME_DATA")))
            Assert.Ignore("Requires a reserved desktop and AE_STARTUP_GAME_DATA.");
        Verify(theme, true);
    }

    private static void Verify(ThemeType theme, bool live)
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
                var previous = ThemesController.CurrentTheme;
                ThemesController.SetTheme(theme);
                var container = live
                    ? services.GetRequiredService<IPackFileContainerLoader>().Load(Path.Combine(
                        Environment.GetEnvironmentVariable("AE_STARTUP_GAME_DATA")!, "data.pack"))!
                    : CreateContainer();
                Assert.That(container, Is.Not.Null);
                container.IsCaPackFile = true;
                services.GetRequiredService<IPackFileService>().AddContainer(container);
                var model = services.GetRequiredService<MainViewModel>();
                var prepare = model.FileTree.InitializeCaFilesAsync();
                PumpUntil(() => prepare.IsCompleted);
                prepare.GetAwaiter().GetResult();
                var branch = model.FileTree.Files.Single();
                for (var depth = 0; depth < 5; depth++)
                {
                    branch.IsNodeExpanded = true;
                    if (branch.Children.Count > 60)
                        break;
                    var next = branch.Children.Where(child => child.Children.Count > 0).MaxBy(child => child.Children.Count);
                    if (next == null)
                        break;
                    branch = next;
                }
                var files = container.FileList.Values.Where(file => file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.Name.Length).Take(6).ToArray();
                Assert.That(files, Has.Length.EqualTo(6));
                var window = services.GetRequiredService<MainWindow>();
                window.DataContext = model;
                window.Title = "Asset Editor 国区版 · 布局验证";
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = live ? 50 : -10000;
                window.Top = live ? 50 : -10000;
                window.Width = 1200;
                window.Height = 740;
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Topmost = live;
                var failures = new List<string>();
                try
                {
                    window.Show();
                    Flush(window);
                    Assert.That(window.WorkspaceSidebar.Items, Has.Count.EqualTo(2));
                    foreach (var item in window.ActivityBar.Items.Cast<ListBoxItem>())
                    {
                        Assert.That(item.ToolTip, Is.Not.Null.And.Not.Empty);
                        Assert.That(AutomationProperties.GetName(item), Is.EqualTo(item.ToolTip));
                        Assert.That(ToolTipService.GetShowOnDisabled(item), Is.True);
                    }
                    foreach (var dpi in new[] { 96, 120, 144 })
                    {
                        var origin = live ? 60 : -10000;
                        var bounds = new NativeRectangle { Left = origin, Top = origin, Right = origin + 1200 * dpi / 96, Bottom = origin + 740 * dpi / 96 };
                        SendDpiMessage(new WindowInteropHelper(window).Handle, 0x02E0, new nint(dpi | (dpi << 16)), ref bounds);
                        Flush(window);
                        Assert.That(VisualTreeHelper.GetDpi(window).PixelsPerInchX, Is.EqualTo(dpi));
                        CheckAndCapture("empty");
                        var first = model.EditorManager.CreateFromFile(files[0], EditorEnums.XML_Editor);
                        Assert.That(first, Is.Not.Null, "The selected resource must open in the actual text editor.");
                        Flush(window);
                        Assert.That(window.EditorsTabControl.Items, Has.Count.EqualTo(1));
                        Assert.That(UiRefinementTests.Descendants<ICSharpCode.AvalonEdit.TextEditor>(window.EditorsTabControl)
                            .Any(editor => editor.IsVisible && editor.Document.TextLength > 0), Is.True);
                        CheckAndCapture("one");
                        foreach (var file in files.Skip(1))
                            Assert.That(model.EditorManager.CreateFromFile(file, EditorEnums.XML_Editor), Is.Not.Null);
                        Flush(window);
                        Assert.That(window.EditorsTabControl.Items, Has.Count.EqualTo(6));
                        CheckAndCapture("many");
                        model.IsPackFileExplorerVisible = false;
                        Flush(window);
                        CheckAndCapture("collapsed");
                        model.IsPackFileExplorerVisible = true;
                        Flush(window);
                        CheckAndCapture("expanded");
                        model.EditorManager.CloseAllTools(first);
                        Flush(window);
                        Assert.That(model.EditorManager.GetAllEditors(), Is.Empty);
                        Assert.That(((Panel)window.EditorsTabControl.Template.FindName("PART_ItemsHolder", window.EditorsTabControl)).Children, Is.Empty);
                        CheckAndCapture("last-closed");

                        void CheckAndCapture(string state)
                        {
                            var editors = window.EditorsTabControl;
                            var strip = (FrameworkElement)editors.Template.FindName("EditorTabStrip", editors);
                            var body = (FrameworkElement)editors.Template.FindName("PART_ItemsHolder", editors);
                            var workspace = (Grid)VisualTreeHelper.GetParent(editors);
                            var splitter = workspace.Children.OfType<GridSplitter>().Single();
                            var divider = (FrameworkElement)splitter.Template.FindName("Divider", splitter);
                            var rects = new Dictionary<string, Rect>
                            {
                                ["activity"] = Bounds(window.ActivityRail, window),
                                ["left"] = Bounds(window.WorkspaceShell, window), ["right"] = Bounds(editors, window),
                                ["rightHeader"] = Bounds(strip, window),
                                ["leftContent"] = Bounds(window.ResourceTreeView, window), ["rightContent"] = Bounds(body, window),
                                ["divider"] = Bounds(divider, window), ["status"] = Bounds(window.ApplicationStatusBar, window),
                            };
                            var treeBar = UiRefinementTests.Descendants<ScrollBar>(window.ResourceTreeView)
                                .FirstOrDefault(bar => bar.IsVisible && bar.Orientation == Orientation.Vertical);
                            if (treeBar != null)
                                rects["treeScrollSlot"] = Bounds(treeBar, window);
                            var horizontalBar = UiRefinementTests.Descendants<ScrollBar>(editors)
                                .FirstOrDefault(bar => bar.IsVisible && bar.Orientation == Orientation.Horizontal);
                            if (horizontalBar != null)
                                rects["editorHorizontalSlot"] = Bounds(horizontalBar, window);
                            Equal("activity top", rects["activity"].Top, rects["right"].Top);
                            Equal("activity bottom", rects["activity"].Bottom, rects["right"].Bottom);
                            Equal("activity height", rects["activity"].Height, rects["right"].Height);
                            Equal("editor content top", rects["rightContent"].Top, rects["right"].Top + rects["rightHeader"].Height);
                            if (editors.Items.Count == 0)
                            {
                                Assert.That(strip.Visibility, Is.EqualTo(Visibility.Collapsed));
                                Equal("empty editor has no title gap", rects["rightContent"].Top, rects["right"].Top);
                            }
                            else
                            {
                                Equal("tab top", rects["rightHeader"].Top, rects["right"].Top);
                                var tabs = Enumerable.Range(0, editors.Items.Count)
                                    .Select(index => (TabItem)editors.ItemContainerGenerator.ContainerFromIndex(index)).ToArray();
                                var rows = tabs.Select(tab => tab.TranslatePoint(new Point(), strip).Y).Distinct().Count();
                                Equal("tab band height", rects["rightHeader"].Height, rows * 28);
                            }
                            if (model.IsPackFileExplorerVisible)
                            {
                                Equal("panel top", rects["left"].Top, rects["right"].Top);
                                Equal("tree has no title gap", rects["leftContent"].Top, rects["left"].Top);
                                Equal("tree uses full height", rects["leftContent"].Height, rects["left"].Height);
                                Equal("panel bottom", rects["left"].Bottom, rects["right"].Bottom);
                                Equal("panel height", rects["left"].Height, rects["right"].Height);
                                Equal("divider top", rects["divider"].Top, rects["right"].Top);
                                Equal("divider bottom", rects["divider"].Bottom, rects["status"].Top);
                            }
                            else
                            {
                                Assert.That(splitter.IsVisible, Is.False);
                                Equal("collapsed left", rects["right"].Left, Bounds(window.ActivityRail, window).Right);
                            }
                            foreach (var bar in UiRefinementTests.Descendants<ScrollBar>(window).Where(bar => bar.IsVisible))
                            {
                                Equal("scroll slot", bar.Orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight, 6);
                                var slot = LayoutInformation.GetLayoutSlot(bar);
                                Equal("reserved scroll slot", bar.Orientation == Orientation.Vertical ? slot.Width : slot.Height, 6);
                            }
                            if (live)
                            {
                                var settled = Stopwatch.StartNew();
                                PumpUntil(() => settled.ElapsedMilliseconds >= 120);
                                Save(window, theme, dpi, state, rects, container);
                            }
                            NUnit.Framework.TestContext.WriteLine($"{theme} {dpi} {state}: activity={rects["activity"]}; left={rects["left"]}; right={rects["right"]}; tab band={rects["rightHeader"]}; left content={rects["leftContent"]}; right content={rects["rightContent"]}; divider={rects["divider"]}.");
                            void Equal(string edge, double actual, double expected)
                            {
                                if (Math.Abs(actual - expected) > 0.01)
                                    failures.Add($"{theme} {dpi} {state} {edge}: {actual:F3} != {expected:F3}");
                            }
                        }
                    }
                    var tree = UiRefinementTests.Descendants<TreeView>(window.ResourceTreeView).Single();
                    var scroll = UiRefinementTests.Descendants<ScrollViewer>(tree).First();
                    Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0));
                    scroll.ScrollToEnd();
                    Flush(window);
                    Assert.That(scroll.VerticalOffset, Is.GreaterThan(0));
                    scroll.ScrollToTop();
                    Flush(window);
                    scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                    Flush(window);
                    Assert.That(scroll.VerticalOffset, Is.GreaterThan(0));
                    var presenter = UiRefinementTests.Descendants<ScrollContentPresenter>(scroll).First();
                    var hit = presenter.InputHitTest(new Point(presenter.ActualWidth - 1, presenter.ActualHeight / 2)) as DependencyObject;
                    while (hit != null)
                    {
                        Assert.That(hit, Is.Not.InstanceOf<ScrollBar>(), "The bar must not intercept the content's edge.");
                        hit = VisualTreeHelper.GetParent(hit);
                    }
                    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
                }
                finally
                {
                    foreach (var editor in model.EditorManager.GetAllEditors().ToArray())
                        model.EditorManager.CloseTool(editor);
                    window.DataContext = null;
                    window.Close();
                    model.FileTree.Dispose();
                    ThemesController.SetTheme(previous);
                }
            });
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    private static PackFileContainer CreateContainer()
    {
        var result = new PackFileContainer("layout.pack");
        for (var index = 0; index < 200; index++)
        {
            var name = $"resource_geometry_alignment_document_{index:000}.xml";
            result.FileList[$"resources\\{name}"] = PackFile.CreateFromBytes(name,
                Encoding.UTF8.GetBytes("<resources>\n" + string.Join("\n", Enumerable.Range(1, 100).Select(row => $"  <file index=\"{row}\">{new string('x', 180)}</file>")) + "\n</resources>"));
        }
        return result;
    }

    private static Rect Bounds(FrameworkElement element, Window window)
        => new(element.TranslatePoint(new Point(), window), new Size(element.ActualWidth, element.ActualHeight));

    private static void Save(Window window, ThemeType theme, int dpi, string state, Dictionary<string, Rect> rects, PackFileContainer container)
    {
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        var name = $"workspace-{theme}-{dpi}-{state}";
        using (var file = File.Create(Path.Combine(output, name + ".png")))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(OperationProgressWindowGallery.Capture(window)));
            encoder.Save(file);
        }
        var geometry = rects.ToDictionary(pair => pair.Key,
            pair => new[] { pair.Value.X, pair.Value.Y, pair.Value.Width, pair.Value.Height }.Select(value => value * dpi / 96).ToArray());
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
        {
            Theme = theme.ToString(), Dpi = dpi, State = state, Unit = "physical pixels", Rectangles = geometry,
            OpenDocuments = ((MainWindow)window).EditorsTabControl.Items.Count,
            ResourcePack = container.SystemFilePath, ResourceCount = container.FileList.Count,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Flush(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var clock = Stopwatch.StartNew();
        while (!complete() && clock.Elapsed < TimeSpan.FromSeconds(30))
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.That(complete(), Is.True, "The window did not settle.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendDpiMessage(nint hwnd, int message, nint wParam, ref NativeRectangle bounds);
}
