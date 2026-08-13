using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Views.ExternalPack;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests
{
    [NonParallelizable]
    public class ExternalPackOpenChoiceWindowTests
    {
        [Test]
        public void Window_RendersChineseChoicesWithDesignSystemStyles()
        {
            using var services = new ServiceCollection()
                .AddSingleton(CreateLocalization())
                .BuildServiceProvider();
            WpfTestApplicationHost.InvokeWithThemeResources(
                services,
                () => RenderAndAssert());
        }

        private static void RenderAndAssert()
        {
            var previousTheme = ThemesController.CurrentTheme;
            ExternalPackOpenChoiceWindow? window = null;
            try
            {
                ThemesController.SetTheme(ThemeType.DarkTheme);
                window = new ExternalPackOpenChoiceWindow(
                    @"D:\mods\example.pack")
                {
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                window.Show();
                window.UpdateLayout();

                var buttonTexts = FindDescendants<Button>(window)
                    .Select(button => button.Content?.ToString())
                    .ToList();
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
                    NUnitAssert.That(
                        buttonTexts,
                        Does.Contain("作为参考打开"));
                    NUnitAssert.That(
                        buttonTexts,
                        Does.Contain("导入为工程"));
                    NUnitAssert.That(buttonTexts, Does.Contain("取消"));
                    NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
                    NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
                });

                SaveForVisualReview(bitmap);
            }
            finally
            {
                window?.Close();
                ThemesController.SetTheme(previousTheme);
            }
        }

        private static void SaveForVisualReview(RenderTargetBitmap bitmap)
        {
            var outputDirectory = Environment.GetEnvironmentVariable(
                "AE_UI_QA_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return;

            Directory.CreateDirectory(outputDirectory);
            using var stream = File.Create(Path.Combine(
                outputDirectory,
                "external-pack-open-choice.png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        private static IEnumerable<T> FindDescendants<T>(
            DependencyObject root)
            where T : DependencyObject
        {
            for (var index = 0;
                 index < VisualTreeHelper.GetChildrenCount(root);
                 index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                    yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        private static LocalizationManager CreateLocalization()
        {
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            return localization;
        }
    }
}
