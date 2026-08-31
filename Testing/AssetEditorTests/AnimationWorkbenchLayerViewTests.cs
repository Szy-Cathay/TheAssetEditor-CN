using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
public class AnimationWorkbenchLayerViewTests
{
    [Test]
    public void LayerUiAndDiagnosticsHaveChineseLocalizationEntries()
    {
        var languagePath = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json");
        using var json = JsonDocument.Parse(File.ReadAllText(languagePath));
        var requiredKeys = new[]
        {
            "AnimationWorkbench.Layer.Title",
            "AnimationWorkbench.Layer.Subtitle",
            "AnimationWorkbench.Layer.SearchBones",
            "AnimationWorkbench.Layer.SavedMasks",
            "AnimationWorkbench.Layer.MaskName",
            "AnimationWorkbench.Layer.SaveMask",
            "AnimationWorkbench.Layer.LoadMask",
            "AnimationWorkbench.Layer.Mode.Override",
            "AnimationWorkbench.Layer.Mode.Additive",
            "AnimationWorkbench.Layer.Reference.FirstFrame",
            "AnimationWorkbench.Layer.Reference.RestPose",
            "AnimationWorkbench.Layer.Weight",
            "AnimationWorkbench.Layer.SelectedBones",
            "AnimationWorkbench.Layer.OutputImpact",
            "AnimationWorkbench.Layer.Apply",
            "AnimationWorkbench.Layer.Cancel",
        }.Concat(Enum.GetNames<AnimationWorkbenchDiagnosticCode>()
            .Where(name => name.StartsWith("Layer", StringComparison.Ordinal))
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
    public void Xaml_UsesSemanticDesignSystemAndBoneTree()
    {
        var xamlPath = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchLayerView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeSurface.Panel"));
            NUnitAssert.That(source, Does.Contain("AeSurface.Control"));
            NUnitAssert.That(source, Does.Contain("AeEditor.PlaybackSlider"));
            NUnitAssert.That(source, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(source, Does.Contain("AeInput.ComboBox"));
            NUnitAssert.That(source, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(source, Does.Contain("AeTree.Item"));
            NUnitAssert.That(source, Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(source, Does.Not.Contain("AeBrush.BorderSubtle"));
            NUnitAssert.That(
                Regex.IsMatch(
                    source,
                    "(?:Margin|Padding)=\"[^\"]*(?<!\\d)(?:6|10|14|18)(?!\\d)"),
                Is.False);
            NUnitAssert.That(
                Regex.IsMatch(source, "#[0-9a-fA-F]{3,8}"),
                Is.False);
            NUnitAssert.That(
                document.Descendants().Count(element =>
                    element.Name.LocalName == nameof(TreeView)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void LayerView_RendersAcrossRequiredThemes()
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
                        var controller = CreateController(document);
                        var view = new AnimationWorkbenchLayerView
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
                            var selectedBones = FindDescendants<TextBlock>(view)
                                .Single(text => text.Name == "SelectedBonesText");
                            var bitmap = Render(window);
                            NUnitAssert.Multiple(() =>
                            {
                                NUnitAssert.That(applyButton.IsEnabled, Is.True);
                                NUnitAssert.That(selectedBones.Text, Is.Not.Empty);
                                NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
                                NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
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
    public void LayerView_EnterOrEscapeFromSearchDoesNotCommitOrCancel()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = CreateController(document);
                var view = new AnimationWorkbenchLayerView
                {
                    Controller = controller,
                };
                var window = Host(view);
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var search = FindDescendants<TextBox>(view)
                        .Single(textBox => textBox.Name == "SearchTextBox");
                    foreach (var key in new[] { Key.Enter, Key.Escape })
                    {
                        search.RaiseEvent(new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(view),
                            0,
                            key)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        });
                    }

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
    public void LayerView_UnloadReleasesOwnedPreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = CreateController(document);
                var view = new AnimationWorkbenchLayerView
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
            });
    }

    private static AnimationWorkbenchLayerController CreateController(
        AnimationWorkbenchDocument document)
    {
        var path = Path.Combine(
            NUnit.Framework.TestContext.CurrentContext.WorkDirectory,
            $"animation-workbench-mask-{Guid.NewGuid():N}.json");
        return new AnimationWorkbenchLayerController(
            document,
            AnimationWorkbenchBoneMaskStore.CreateForFile(path));
    }

    private static Window Host(UIElement content) => new()
    {
        Width = 1120,
        Height = 720,
        Content = content,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
    };

    private static RenderTargetBitmap Render(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
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
            $"animation-workbench-layer-{theme}.png"));
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
        var animationA = CreateClip(12, 30, 0);
        var animationB = CreateClip(8, 20, 5);
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
            for (var boneIndex = 0; boneIndex < 3; boneIndex++)
            {
                frame.Position.Add(new Vector3(
                    offset + frameIndex * 0.1f,
                    boneIndex,
                    0));
                frame.Rotation.Add(Quaternion.Identity);
                frame.Scale.Add(Vector3.One);
            }
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
                CreateBone(0, "root", AnimationFile.BoneIndexNoParent),
                CreateBone(1, "spine", 0),
                CreateBone(2, "hand_r", 1),
            ],
        };
        var frame = new AnimationFile.Frame();
        for (var boneIndex = 0; boneIndex < 3; boneIndex++)
        {
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts.Add(part);
        return new GameSkeleton(skeletonFile, new AnimationPlayer());
    }

    private static AnimationFile.BoneInfo CreateBone(
        int id,
        string name,
        int parentId) => new()
    {
        Id = id,
        Name = name,
        ParentId = parentId,
    };

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
