using System.Reflection;
using System.Runtime.InteropServices;
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
using Material.Icons;
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
public class SidebarShortcutInteractionTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void Shortcuts_KeepRailHighlightWhilePressed_WithoutChangingSelection(ThemeType theme)
    {
        WithRail(theme, false, (window, rail, navigation, buttons, caption, creator) =>
        {
            var reference = (ListBoxItem)navigation.Items[1];
            SetState(reference, typeof(UIElement), "IsMouseOverPropertyKey", true);
            var hover = ColorOf(reference.Background);
            Assert.That(hover, Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
            reference.IsSelected = true;
            var selected = ColorOf(reference.Background);
            Assert.That(selected, Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
            foreach (var button in buttons.Where(item => item.IsEnabled))
            {
                var shortcut = (EditorShortcutViewModel)button.DataContext;
                Assert.That(button.Command, Is.SameAs(shortcut.OpenEditorCommand));
                Assert.That(button.FocusVisualStyle, Is.SameAs(reference.FocusVisualStyle));
                Assert.That(AutomationProperties.GetName(button), Is.EqualTo(shortcut.DisplayName));
                SetState(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
                Assert.That(ColorOf(button.Background), Is.EqualTo(hover));
                foreach (var active in new[] { false, true })
                {
                    shortcut.IsActive = active;
                    SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", true);
                    Pump(160);
                    UiRefinementTests.SaveSnapshot(rail, $"rail-{theme}-{shortcut.IconKind}-pressed-{active}.png");
                    Assert.That(ColorOf(button.Background), Is.EqualTo(active ? selected : hover), shortcut.DisplayName);
                    Assert.That(SurfacePixel(button), Is.EqualTo(active ? selected : hover), shortcut.DisplayName);
                    AssertNoMotion(button);
                    Assert.That(creator.Invocations, Is.Empty);
                    SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", false);
                    Pump(150);
                    Assert.That(ColorOf(button.Background), Is.EqualTo(active ? selected : hover));
                    AssertNoMotion(button);
                    Assert.That(shortcut.IsActive, Is.EqualTo(active));
                }
                SetState(button, typeof(UIElement), "IsMouseOverPropertyKey", false);
                Assert.That(ColorOf(button.Background), Is.EqualTo(selected));
                shortcut.IsActive = false;
                Assert.That(ColorOf(button.Background).A, Is.Zero);
                SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", true);
                Assert.That(ColorOf(button.Background), Is.EqualTo(hover), "Keyboard press must retain a visible surface.");
                SetState(button, typeof(ButtonBase), "IsPressedPropertyKey", false);
            }
            var disabled = buttons.Single(item => ((EditorShortcutViewModel)item.DataContext).IconKind == MaterialIconKind.Key);
            Assert.That(disabled.IsEnabled, Is.False);
            Assert.That(disabled.Command!.CanExecute(null), Is.False);
            Assert.That(((Border)disabled.Template.FindName("Chrome", disabled)).Opacity, Is.EqualTo(0.42));
            var settings = UiRefinementTests.Descendants<Button>(rail).Except(buttons).Single();
            SetState(settings, typeof(UIElement), "IsMouseOverPropertyKey", true);
            SetState(settings, typeof(ButtonBase), "IsPressedPropertyKey", true);
            Pump(160);
            Assert.That(ColorOf(settings.Background), Is.EqualTo(hover));
            AssertNoMotion(settings);
            SetState(settings, typeof(ButtonBase), "IsPressedPropertyKey", false);
            Pump(150);
            AssertNoMotion(settings);
            Assert.That(creator.Invocations, Is.Empty);
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [Category("DesktopPreview")]
    public void Rail_RealMouseAndKeyboardPreserveHighlightAndSingleCommands(ThemeType theme)
    {
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1")
            Assert.Ignore("Requires a reserved desktop.");
        WithRail(theme, true, (window, rail, navigation, buttons, caption, creator) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            window.Activate();
            SetForegroundWindow(hwnd);
            Pump(180);
            Assert.That(GetForegroundWindow(), Is.EqualTo(hwnd));
            var frameIndex = 0;
            foreach (ListBoxItem item in navigation.Items)
            {
                MoveTo(item);
                Save(AutomationProperties.GetName(item) + "：悬停");
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                foreach (var delay in new[] { 0, 30, 70, 120, 280 })
                {
                    if (delay > 0)
                        Pump(delay);
                    AssertNoMotion(item);
                }
                Assert.That(item.IsSelected, Is.True);
                Assert.That(ColorOf(item.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
                Save(AutomationProperties.GetName(item) + "：按住");
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                foreach (var delay in new[] { 0, 30, 70, 120 })
                {
                    if (delay > 0)
                        Pump(delay);
                    AssertNoMotion(item);
                }
                Assert.That(item.IsSelected, Is.True);
                Save(AutomationProperties.GetName(item) + "：松开");
            }
            Assert.That(creator.Invocations, Is.Empty);
            foreach (var button in buttons.Where(item => item.IsEnabled))
            {
                var shortcut = (EditorShortcutViewModel)button.DataContext;
                var count = creator.Invocations.Count;
                MoveTo(button);
                Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
                Save(shortcut.DisplayName + "：悬停");
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                foreach (var delay in new[] { 0, 30, 70, 120, 280 })
                {
                    if (delay > 0)
                        Pump(delay);
                    AssertNoMotion(button);
                }
                Assert.That(button.IsPressed && button.IsMouseCaptured, Is.True);
                Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
                Assert.That(SurfacePixel(button), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
                Assert.That(creator.Invocations, Has.Count.EqualTo(count));
                Save(shortcut.DisplayName + "：按住");
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                foreach (var delay in new[] { 0, 30, 70, 120 })
                {
                    if (delay > 0)
                        Pump(delay);
                    AssertNoMotion(button);
                }
                Assert.That(button.IsPressed || button.IsMouseCaptured, Is.False);
                Assert.That(creator.Invocations, Has.Count.EqualTo(count + 1));
                Assert.That(ColorOf(button.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
                Save(shortcut.DisplayName + "：松开");
                MoveAway();
                Assert.That(shortcut.IsActive, Is.False, "A command must not acquire a permanent selected state.");
                Assert.That(ColorOf(button.Background).A, Is.Zero);
            }
            var disabled = buttons.Single(item => !item.IsEnabled);
            var beforeDisabled = creator.Invocations.Count;
            MoveTo(disabled);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Pump(300);
            Assert.That(disabled.IsPressed, Is.False);
            Save("关键帧编辑器：禁用，按住无动作");
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Pump(160);
            Assert.That(creator.Invocations, Has.Count.EqualTo(beforeDisabled));
            Assert.That(disabled.IsKeyboardFocused, Is.False);

            var settings = UiRefinementTests.Descendants<Button>(rail).Except(buttons).Single();
            var settingsCommand = settings.Command;
            Assert.That(settingsCommand, Is.Not.Null);
            var settingsInvocations = 0;
            settings.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => settingsInvocations++);
            MoveTo(settings);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Pump(500);
            Assert.That(settings.IsPressed, Is.True);
            Assert.That(settingsInvocations, Is.Zero);
            Assert.That(ColorOf(settings.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
            AssertNoMotion(settings);
            Save("设置：按住，无缩放");
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Pump(180);
            AssertNoMotion(settings);
            Assert.That(settingsInvocations, Is.EqualTo(1));
            settings.Command = settingsCommand;

            var first = buttons.First(item => item.IsEnabled);
            var firstShortcut = (EditorShortcutViewModel)first.DataContext;
            firstShortcut.IsActive = true;
            MoveTo(first);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Pump(180);
            Assert.That(ColorOf(first.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
            Save(firstShortcut.DisplayName + "：活动编辑器按住");
            MoveAway();
            Assert.That(first.IsPressed, Is.False);
            MoveTo(first);
            Assert.That(first.IsPressed, Is.True);
            AssertNoMotion(first);
            MoveAway();
            var beforeCancel = creator.Invocations.Count;
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Pump(180);
            Assert.That(creator.Invocations, Has.Count.EqualTo(beforeCancel));
            Assert.That(firstShortcut.IsActive, Is.True);
            firstShortcut.IsActive = false;

            ((ListBoxItem)navigation.SelectedItem).Focus();
            for (var index = 0; index < 24 && !first.IsKeyboardFocused; index++)
            {
                Key(0x09);
                Assert.That(disabled.IsKeyboardFocused, Is.False);
            }
            Assert.That(first.IsKeyboardFocused, Is.True);
            Assert.That(first.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("AeFocus.Keyboard")));
            var beforeKeyboard = creator.Invocations.Count;
            keybd_event(0x20, 0, 0, UIntPtr.Zero);
            Pump(500);
            Assert.That(first.IsPressed, Is.True);
            Assert.That(ColorOf(first.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
            AssertNoMotion(first);
            Assert.That(creator.Invocations, Has.Count.EqualTo(beforeKeyboard));
            Save(firstShortcut.DisplayName + "：键盘空格按住");
            keybd_event(0x20, 0, 2, UIntPtr.Zero);
            Pump(180);
            Assert.That(creator.Invocations, Has.Count.EqualTo(beforeKeyboard + 1));
            Key(0x0D);
            Assert.That(creator.Invocations, Has.Count.EqualTo(beforeKeyboard + 2));
            Assert.That(first.IsPressed, Is.False);
            NUnit.Framework.TestContext.WriteLine($"{theme}: resource/history and all {buttons.Count(item => item.IsEnabled)} enabled shortcuts held/released; disabled keyframe; active state; drag cancellation; Tab/Space/Enter; once-only commands; native DPI {VisualTreeHelper.GetDpi(window).PixelsPerInchX}.");

            void MoveAway()
            {
                var point = window.PointToScreen(new Point(window.ActualWidth - 24, window.ActualHeight - 24));
                SetCursorPos((int)point.X, (int)point.Y);
                Pump(120);
            }
            void Save(string state)
            {
                caption.Text = state;
                Pump(30);
                var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
                if (string.IsNullOrEmpty(output))
                    return;
                Directory.CreateDirectory(output);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(OperationProgressWindowGallery.Capture(window)));
                using var stream = File.Create(Path.Combine(output, $"rail-{theme}-{frameIndex++:D3}.png"));
                encoder.Save(stream);
            }
        });
    }

    private static void WithRail(ThemeType theme, bool desktop,
        Action<Window, Grid, ListBox, Button[], TextBlock, Mock<IEditorCreator>> verify)
    {
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        settings.CurrentSettings.StartMaximised = false;
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
                var previousTheme = ThemesController.CurrentTheme;
                var previousWindow = desktop ? GetForegroundWindow() : IntPtr.Zero;
                GetCursorPos(out var previousPointer);
                ThemesController.SetTheme(theme);
                var source = new MainWindow(services);
                var rail = source.ActivityRail;
                ((Panel)VisualTreeHelper.GetParent(rail)).Children.Remove(rail);
                rail.Width = 44;
                rail.HorizontalAlignment = HorizontalAlignment.Left;
                var caption = new TextBlock { Margin = new Thickness(60, 20, 12, 0), TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top, Style = (Style)Application.Current.FindResource("AeText.Body") };
                var content = new Grid { Background = (Brush)Application.Current.FindResource("AeBrush.Surface2") };
                content.Children.Add(rail);
                content.Children.Add(caption);
                var window = new Window
                {
                    Title = "侧栏按压验证", Width = 400, Height = 660, Content = content,
                    DataContext = new { MenuBar = services.GetRequiredService<MenuBarViewModel>(), HistoryWorkspace = new NavigationState() },
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = desktop ? 120 : -10000, Top = desktop ? 90 : -10000,
                    ShowActivated = desktop, ShowInTaskbar = false, Topmost = desktop,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Pump(100);
                    var buttons = UiRefinementTests.Descendants<Button>(source.EditorShortcuts).ToArray();
                    Assert.That(buttons, Is.Not.Empty);
                    verify(window, rail, source.ActivityBar, buttons, caption, creator);
                }
                finally
                {
                    if (desktop)
                    {
                        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                        keybd_event(0x20, 0, 2, UIntPtr.Zero);
                    }
                    window.Close();
                    source.Close();
                    if (desktop)
                    {
                        SetCursorPos(previousPointer.X, previousPointer.Y);
                        SetForegroundWindow(previousWindow);
                    }
                    ThemesController.SetTheme(previousTheme);
                }
            });
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    private sealed class NavigationState
    {
        public int SelectedSidebarTabIndex { get; set; }
        public bool IsEnabled => true;
    }

    private static Color SurfacePixel(Button button)
    {
        var bitmap = new RenderTargetBitmap((int)button.ActualWidth, (int)button.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(button), null, new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        bitmap.Render(visual);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(4, (int)button.ActualHeight / 2, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static void AssertNoMotion(UIElement button)
    {
        foreach (var visual in UiRefinementTests.Descendants<UIElement>(button).Prepend(button))
        {
            Assert.That(visual.RenderTransform.Value.IsIdentity, Is.True, "Sidebar icons must not scale during press or release.");
            Assert.That(visual.RenderTransform.HasAnimatedProperties, Is.False, "Sidebar icons must not inherit a press/release animation.");
        }
    }
    private static Color ResourceColor(string key) => ColorOf((Brush)Application.Current.FindResource(key));
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static void SetState(DependencyObject control, Type owner, string field, bool value)
        => control.SetValue((DependencyPropertyKey)owner.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static void MoveTo(FrameworkElement element)
    {
        var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        SetCursorPos((int)point.X, (int)point.Y);
        Pump(100);
    }
    private static void Key(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
        Pump(120);
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
