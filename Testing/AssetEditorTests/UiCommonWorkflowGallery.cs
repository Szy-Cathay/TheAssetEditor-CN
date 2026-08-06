using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using AssetEditor.ViewModels;
using AssetEditor.Views.Settings;
using AssetEditor.Views.Startup;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.DependencyInjection;
using Shared.Core.ErrorHandling.Exceptions;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.StandardDialog;
using Shared.Ui.Common.Exceptions;
using Shared.Ui.Common.OperationProgress;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiCommonWorkflowGallery
{
    private static readonly string[] Variants =
    [
        "settings-general",
        "settings-theme",
        "settings-rendering",
        "settings-audio",
        "settings-save",
        "confirm",
        "error",
        "loading",
        "failure",
    ];

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCaseSource(nameof(Cases))]
    public void CommonWorkflow_RendersRequiredThemeAndState(
        ThemeType theme,
        string variant)
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () => Render(theme, variant));
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var theme in new[]
                 {
                     ThemeType.DarkTheme,
                     ThemeType.LightTheme,
                     ThemeType.HighContrastDark,
                     ThemeType.HighContrastLight,
                 })
        {
            foreach (var variant in Variants)
                yield return new TestCaseData(theme, variant);
        }
    }

    private static void Render(ThemeType theme, string variant)
    {
        var previousTheme = ThemesController.CurrentTheme;
        try
        {
            ThemesController.SetTheme(theme);
            if (variant.StartsWith("settings-", StringComparison.Ordinal))
                RenderSettings(theme, variant);
            else if (variant == "confirm")
                RenderConfirm(theme, variant);
            else if (variant == "error")
                RenderError(theme, variant);
            else
                RenderStartup(theme, variant);
        }
        finally
        {
            ThemesController.SetTheme(previousTheme);
        }
    }

    private static void RenderSettings(ThemeType theme, string variant)
    {
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settings.CurrentSettings.GameDirectories.Add(
            new ApplicationSettings.GamePathPair
            {
                Game = GameTypeEnum.Warhammer3,
                Path = @"D:\Games\Total War WARHAMMER III",
            });
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        var viewModel = new SettingsViewModel(
            settings,
            new ApplicationSettingsApplier(
                settings,
                autoSave,
                new TestEventHub()),
            Mock.Of<IStandardDialogs>());
        var category = Array.IndexOf(Variants, variant);
        var window = new SettingsWindow
        {
            Width = 860,
            Height = 700,
            DataContext = viewModel,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            var categories = FindLogicalDescendant<TabControl>(window);
            NUnitAssert.That(categories, Is.Not.Null);
            categories!.SelectedIndex = category;
            window.UpdateLayout();
            Capture(window, theme, variant);
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(categories.SelectedIndex, Is.EqualTo(category));
                NUnitAssert.That(categories.Items.Count, Is.EqualTo(5));
                NUnitAssert.That(window.ActualWidth, Is.GreaterThanOrEqualTo(760));
                NUnitAssert.That(window.ActualHeight, Is.GreaterThanOrEqualTo(600));
            });
        }
        finally
        {
            window.Close();
            autoSave.Stop();
        }
    }

    private static void RenderConfirm(ThemeType theme, string variant)
    {
        using var window = new MessageDialogWindow(
            "放弃未保存的修改？",
            "帝国将军模型中的修改尚未保存。放弃后无法恢复。",
            MessageDialogButtonSet.YesNo)
        {
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        window.UpdateLayout();
        Capture(window, theme, variant);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(window.ActionButtonCount, Is.EqualTo(2));
            NUnitAssert.That(window.Message, Does.Contain("尚未保存"));
        });
        window.Close();
    }

    private static void RenderError(ThemeType theme, string variant)
    {
        var information = new ExceptionInformation
        {
            AssetEditorVersion = "2.1.1",
            CurrentGame = GameTypeEnum.Warhammer3,
            CurrentEditorName = "帝国将军模型",
            UserMessage = "读取 variantmeshes 文件时发生错误。",
            NumberOfOpenEditors = 3,
            NumberOfOpenedEditors = 8,
            RunTimeInSeconds = 126,
            ExceptionInfo =
            [
                new ExceptionInstance(
                    "无法解析资源路径。",
                    ["VariantMeshParser.Read", "EditorManager.Open"]),
            ],
            ActivePackFiles =
            [
                new ExceptionPackFileContainerInfo(
                    true,
                    false,
                    "帝国将军.pack",
                    @"D:\Mods\帝国将军.pack"),
            ],
        };
        var window = new CustomExceptionWindow(
            information,
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IEventHub>(),
            new ScopeToken(),
            Mock.Of<IScopeRepository>())
        {
            Width = 860,
            Height = 560,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Capture(window, theme, variant);
            var errorTexts = FindLogicalDescendants<TextBox>(window)
                .Select(textBox => textBox.Text)
                .ToArray();
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(window.Title, Does.Contain("错误"));
                NUnitAssert.That(
                    errorTexts.Any(text => text.Contains(
                        "无法解析资源路径",
                        StringComparison.Ordinal)),
                    Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static void RenderStartup(ThemeType theme, string variant)
    {
        StartupPackLoadingWindow? window = null;
        var captured = false;
        if (variant == "loading")
        {
            window = new StartupPackLoadingWindow(
                report =>
                {
                    report(new CaPackLoadProgress(
                        CaPackLoadProgressStage.ReadingPacks,
                        "data.pack",
                        2,
                        3));
                    Thread.Sleep(
                        OperationProgressVisibilityController
                            .ShowDelay +
                        TimeSpan.FromMilliseconds(100));
                    window!.Dispatcher.Invoke(() =>
                    {
                        window.UpdateLayout();
                        Capture(window, theme, variant);
                        captured = true;
                    });
                    return new PackFileContainer("All Game Packs")
                    {
                        IsCaPackFile = true,
                    };
                },
                _ => { },
                () => { });
        }
        else
        {
            window = new StartupPackLoadingWindow(
                _ =>
                {
                    window!.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority
                            .ApplicationIdle,
                        () =>
                        {
                            window.UpdateLayout();
                            Capture(window, theme, variant);
                            captured = true;
                            window.ExitButton.RaiseEvent(
                                new RoutedEventArgs(Button.ClickEvent));
                        });
                    return null;
                },
                _ => { },
                () => { });
        }

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        var result = window.Run();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(captured, Is.True);
            NUnitAssert.That(result, Is.EqualTo(variant == "loading"));
            NUnitAssert.That(
                window.StartupOperationProgress.DetailHistory,
                Is.Not.Empty);
        });
    }

    private static void Capture(
        Window window,
        ThemeType theme,
        string variant)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(
                window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(
                window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
            NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
        });

        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            $"common-workflow-{variant}-{theme}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static T? FindLogicalDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper
                     .GetChildren(parent)
                     .OfType<DependencyObject>())
        {
            if (child is T match)
                return match;

            var descendant = FindLogicalDescendant<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper
                     .GetChildren(parent)
                     .OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }
}
