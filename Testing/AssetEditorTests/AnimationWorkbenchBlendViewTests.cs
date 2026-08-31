using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class AnimationWorkbenchBlendViewTests
{
    [Test]
    public void BlendUiAndDiagnosticsHaveChineseLocalizationEntries()
    {
        var languagePath = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json");
        using var json = JsonDocument.Parse(File.ReadAllText(languagePath));
        var requiredKeys = new[]
        {
            "AnimationWorkbench.Blend.Title",
            "AnimationWorkbench.Blend.Subtitle",
            "AnimationWorkbench.Blend.AnimationAOutFrame",
            "AnimationWorkbench.Blend.AnimationBInFrame",
            "AnimationWorkbench.Blend.OverlapDuration",
            "AnimationWorkbench.Blend.Curve.Smooth",
            "AnimationWorkbench.Blend.Curve.Linear",
            "AnimationWorkbench.Blend.Curve.EaseInOut",
            "AnimationWorkbench.Blend.OutputFps",
            "AnimationWorkbench.Blend.AlignHorizontal",
            "AnimationWorkbench.Blend.AlignYaw",
            "AnimationWorkbench.Blend.PreserveHeight",
            "AnimationWorkbench.Blend.OutputImpact",
            "AnimationWorkbench.Blend.SecondsFormat",
            "AnimationWorkbench.Blend.RateFormat",
            "AnimationWorkbench.Blend.SourceRatesFormat",
            "AnimationWorkbench.Blend.OutputFramesFormat",
            "AnimationWorkbench.Blend.ResamplingAppliedBoth",
            "AnimationWorkbench.Blend.ResamplingAppliedA",
            "AnimationWorkbench.Blend.ResamplingAppliedB",
            "AnimationWorkbench.Blend.OverlapQuantizationFormat",
            "AnimationWorkbench.Blend.LoopSeam",
            "AnimationWorkbench.Blend.Apply",
            "AnimationWorkbench.Blend.Cancel",
        }.Concat(Enum.GetNames<AnimationWorkbenchDiagnosticCode>()
            .Where(name => name.StartsWith("Blend", StringComparison.Ordinal))
            .Select(name => $"AnimationWorkbench.Diagnostic.{name}"));

        foreach (var key in requiredKeys)
        {
            NUnitAssert.That(
                json.RootElement.TryGetProperty(key, out var value),
                Is.True,
                key);
            NUnitAssert.That(value.GetString(), Is.Not.Empty, key);
        }
    }

    [Test]
    public void Xaml_UsesSemanticDesignSystemAndLocalizedAutomationNames()
    {
        var root = FindSolutionRoot();
        var xamlPath = Path.Combine(
            root,
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchBlendView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeSurface.Panel"));
            NUnitAssert.That(source, Does.Contain("AeSurface.Control"));
            NUnitAssert.That(source, Does.Contain("AeEditor.PlaybackSlider"));
            NUnitAssert.That(source, Does.Contain("AeInput.ComboBox"));
            NUnitAssert.That(source, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(source, Does.Contain("AeFocus.Keyboard"));
            NUnitAssert.That(source, Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(
                Regex.IsMatch(source, "#[0-9a-fA-F]{3,8}"),
                Is.False);
            NUnitAssert.That(
                document.Descendants().Count(element =>
                    element.Name.LocalName == nameof(Slider)),
                Is.EqualTo(3));
            NUnitAssert.That(
                document.Descendants().Count(element =>
                    element.Name.LocalName == nameof(CheckBox)),
                Is.EqualTo(3));
        });
    }

    [Test]
    public void BlendView_RendersLiveImpactAcrossRequiredThemes()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                try
                {
                    foreach (var theme in new[]
                             {
                                 ThemeType.DarkTheme,
                                 ThemeType.LightTheme,
                                 ThemeType.HighContrastDark,
                                 ThemeType.HighContrastLight,
                             })
                    {
                        ThemesController.SetTheme(theme);
                        using var document = CreateLoadedDocument();
                        var controller =
                            new AnimationWorkbenchBlendController(document);
                        var view = new AnimationWorkbenchBlendView
                        {
                            Controller = controller,
                        };
                        var window = Host(view);
                        try
                        {
                            window.Show();
                            window.UpdateLayout();
                            window.Dispatcher.Invoke(
                                () => { },
                                DispatcherPriority.ApplicationIdle);
                            window.UpdateLayout();

                            var applyButton = FindDescendants<Button>(view)
                                .Single(button => button.Name == "ApplyButton");
                            var outputFrames = FindDescendants<TextBlock>(view)
                                .Single(text => text.Name == "OutputFramesText");
                            var resampling = FindDescendants<TextBlock>(view)
                                .Single(text => text.Name == "ResampleText");
                            var bitmap = Render(window);
                            NUnitAssert.Multiple(() =>
                            {
                                NUnitAssert.That(applyButton.IsEnabled, Is.True);
                                NUnitAssert.That(outputFrames.Text, Is.Not.Empty);
                                NUnitAssert.That(
                                    resampling.Text,
                                    Does.Contain("动画 B"));
                                NUnitAssert.That(
                                    resampling.Text,
                                    Does.Contain("请求重叠"));
                                NUnitAssert.That(
                                    resampling.Text,
                                    Does.Contain("实际重叠"));
                                NUnitAssert.That(
                                    bitmap.PixelWidth,
                                    Is.GreaterThan(0),
                                    theme.ToString());
                                NUnitAssert.That(
                                    bitmap.PixelHeight,
                                    Is.GreaterThan(0),
                                    theme.ToString());
                                NUnitAssert.That(
                                    view.ActualWidth,
                                    Is.LessThanOrEqualTo(window.ActualWidth));
                            });
                            SaveForVisualReview(bitmap, theme);
                        }
                        finally
                        {
                            window.Close();
                        }
                    }
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void BlendView_InvalidFpsCannotCommitStalePreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = new AnimationWorkbenchBlendController(document);
                var view = new AnimationWorkbenchBlendView
                {
                    Controller = controller,
                };
                var window = Host(view);
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var outputFps = FindDescendants<TextBox>(view)
                        .Single(textBox => textBox.Name == "OutputFpsTextBox");
                    var applyButton = FindDescendants<Button>(view)
                        .Single(button => button.Name == "ApplyButton");

                    outputFps.Text = "0";
                    applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    NUnitAssert.That(controller.HasActivePreview, Is.False);
                    NUnitAssert.That(document.GetState().CanUndo, Is.False);
                    NUnitAssert.That(document.GetState().Result?.FrameCount, Is.EqualTo(36));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void BlendView_EnterOrEscapeFromInteractiveChildDoesNotCommitOrCancel()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = new AnimationWorkbenchBlendController(document);
                var view = new AnimationWorkbenchBlendView
                {
                    Controller = controller,
                };
                var window = Host(view);
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var cancelButton = FindDescendants<Button>(view)
                        .Single(button => button.Name == "CancelButton");
                    cancelButton.RaiseEvent(new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(view),
                        0,
                        Key.Enter)
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    });

                    NUnitAssert.That(controller.HasActivePreview, Is.True);
                    NUnitAssert.That(document.GetState().CanUndo, Is.False);

                    cancelButton.RaiseEvent(new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(view),
                        0,
                        Key.Escape)
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    });

                    NUnitAssert.That(controller.HasActivePreview, Is.True);
                    NUnitAssert.That(document.GetState().CanUndo, Is.False);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void BlendView_UnloadReleasesOwnedPreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = new AnimationWorkbenchBlendController(document);
                var view = new AnimationWorkbenchBlendView
                {
                    Controller = controller,
                };
                var window = Host(view);
                window.Show();
                window.UpdateLayout();

                window.Close();
                window.Dispatcher.Invoke(
                    () => { },
                    DispatcherPriority.ApplicationIdle);

                NUnitAssert.That(controller.HasActivePreview, Is.False);
                NUnitAssert.That(document.GetState().CanUndo, Is.False);
                NUnitAssert.That(document.GetState().Result?.FrameCount, Is.EqualTo(36));
            });
    }

    [Test]
    public void BlendView_ControllerSwitchReleasesOldPreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var firstController =
                    new AnimationWorkbenchBlendController(document);
                var view = new AnimationWorkbenchBlendView
                {
                    Controller = firstController,
                };
                var window = Host(view);
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var secondController =
                        new AnimationWorkbenchBlendController(document);

                    view.Controller = secondController;

                    NUnitAssert.That(
                        firstController.HasActivePreview,
                        Is.False);
                    NUnitAssert.That(
                        secondController.HasActivePreview,
                        Is.True);
                    NUnitAssert.That(
                        document.GetState().CanUndo,
                        Is.False);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static Window Host(UIElement content) => new()
    {
        Width = 1180,
        Height = 680,
        Content = content,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
    };

    private static RenderTargetBitmap Render(Window window)
    {
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
        return bitmap;
    }

    private static void SaveForVisualReview(
        RenderTargetBitmap bitmap,
        ThemeType theme)
    {
        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;
        Directory.CreateDirectory(outputDirectory);
        using var stream = File.Create(Path.Combine(
            outputDirectory,
            $"animation-workbench-blend-{theme}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
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

    private static AnimationWorkbenchDocument CreateLoadedDocument()
    {
        var skeleton = CreateSkeleton();
        var animationA = CreateClip(36, 30, 0);
        var animationB = CreateClip(24, 24, 10);
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animationA,
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            new AnimationWorkbenchSourceInput(
                "animation_b",
                animationB,
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            GameTypeEnum.Warhammer3,
            skeleton));
        return document;
    }

    private static AnimationClip CreateClip(
        int frameCount,
        double framesPerSecond,
        float offset)
    {
        var clip = new AnimationClip
        {
            Duration = AnimationTimebase.FromFramesPerSecond(
                frameCount,
                framesPerSecond).Duration,
        };
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.Add(new Vector3(offset + frameIndex * 0.1f, 0, 0));
            frame.Rotation.Add(Quaternion.Identity);
            frame.Scale.Add(Vector3.One);
            clip.DynamicFrames.Add(frame);
        }
        return clip;
    }

    private static GameSkeleton CreateSkeleton()
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "target_skeleton",
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = AnimationFile.BoneIndexNoParent,
                },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts.Add(part);
        return new GameSkeleton(skeletonFile, new AnimationPlayer());
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AssetEditor.CN.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Unable to locate the solution root.");
    }
}
