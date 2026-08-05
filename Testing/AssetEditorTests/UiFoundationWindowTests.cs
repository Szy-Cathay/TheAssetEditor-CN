using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CommonControls;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.StandardDialog;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiFoundationWindowTests
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void EnabledWindow_ReappliesTitleBarWhenThemeChanges()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                Window? window = null;

                try
                {
                    ThemesController.SetTheme(ThemeType.DarkTheme);
                    window = new Window
                    {
                        Width = 240,
                        Height = 120,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                    };
                    DarkTitleBarHelper.Enable(window);
                    window.Show();

                    NUnitAssert.That(ReadDarkTitleBar(window), Is.EqualTo(1));

                    ThemesController.SetTheme(ThemeType.LightTheme);

                    NUnitAssert.That(ReadDarkTitleBar(window), Is.EqualTo(0));
                }
                finally
                {
                    window?.Close();
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [TestCase(MessageDialogButtonSet.Ok, 1)]
    [TestCase(MessageDialogButtonSet.YesNo, 2)]
    public void MessageDialog_UsesThemeResourcesAndExpectedButtons(
        MessageDialogButtonSet buttonSet,
        int expectedButtonCount)
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var dialog = new MessageDialogWindow(
                    "测试标题",
                    "测试内容",
                    buttonSet);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(dialog.Title, Is.EqualTo("测试标题"));
                    NUnitAssert.That(dialog.Message, Is.EqualTo("测试内容"));
                    NUnitAssert.That(
                        dialog.ActionButtonCount,
                        Is.EqualTo(expectedButtonCount));
                    NUnitAssert.That(
                        dialog.Background,
                        Is.EqualTo(Application.Current.FindResource(
                            "AeBrush.Canvas")));
                    NUnitAssert.That(
                        dialog.Foreground,
                        Is.EqualTo(Application.Current.FindResource(
                            "AeBrush.TextPrimary")));
                });

                dialog.Close();
            });
    }

    [Test]
    public void StandardDialogs_DoNotFallBackToWindowsMessageBox()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Shared",
            "SharedUI",
            "BaseDialogs",
            "StandardDialog",
            "StandardDialogs.cs"));

        NUnitAssert.That(source, Does.Not.Contain("MessageBox.Show"));
    }

    private static int ReadDarkTitleBar(Window window)
    {
        var value = 0;
        var result = DwmGetWindowAttribute(
            new WindowInteropHelper(window).Handle,
            DwmwaUseImmersiveDarkMode,
            ref value,
            sizeof(int));
        NUnitAssert.That(result, Is.EqualTo(0));
        return value;
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate AssetEditor.CN.sln.");
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);
}
