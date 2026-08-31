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
using Editors.AnimatioReTarget.Editor.BoneHandling;
using GameWorld.Core.Animation;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public sealed class AnimationWorkbenchRetargetViewTests
{
    [Test]
    public void RetargetUiAndDiagnosticsHaveChineseLocalizationEntries()
    {
        var languagePath = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json");
        using var json = JsonDocument.Parse(File.ReadAllText(languagePath));
        var requiredKeys = new[]
        {
            "AnimationWorkbench.Retarget.Title",
            "AnimationWorkbench.Retarget.Subtitle",
            "AnimationWorkbench.Retarget.Search",
            "AnimationWorkbench.Retarget.UnmappedOnly",
            "AnimationWorkbench.Retarget.TargetBone",
            "AnimationWorkbench.Retarget.SourceBone",
            "AnimationWorkbench.Retarget.Confidence",
            "AnimationWorkbench.Retarget.CoreBone",
            "AnimationWorkbench.Retarget.AutoMap",
            "AnimationWorkbench.Retarget.SaveProfile",
            "AnimationWorkbench.Retarget.Apply",
            "AnimationWorkbench.Retarget.Cancel",
        }.Concat(Enum.GetNames<AnimationWorkbenchDiagnosticCode>()
            .Where(name => name.StartsWith("Retarget", StringComparison.Ordinal))
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
        var xamlPath = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchRetargetView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeSurface.Panel"));
            NUnitAssert.That(source, Does.Contain("AeSurface.Control"));
            NUnitAssert.That(source, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(source, Does.Contain("AeInput.ComboBox"));
            NUnitAssert.That(source, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(source, Does.Contain("AeFocus.Keyboard"));
            NUnitAssert.That(source, Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(
                Regex.IsMatch(source, "#[0-9a-fA-F]{3,8}"),
                Is.False);
            NUnitAssert.That(
                document.Descendants().Count(element =>
                    element.Name.LocalName == nameof(ItemsControl)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void RetargetView_RendersMappingAcrossRequiredThemes()
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
                        var view = new AnimationWorkbenchRetargetView
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
                            var mappingList = FindDescendants<ItemsControl>(view)
                                .Single(items => items.Name == "MappingList");
                            var bitmap = Render(window);
                            NUnitAssert.Multiple(() =>
                            {
                                NUnitAssert.That(applyButton.IsEnabled, Is.True);
                                NUnitAssert.That(mappingList.Items.Count, Is.EqualTo(3));
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
    public void RetargetView_EnterOrEscapeFromSearchKeepsPreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = CreateController(document);
                var view = new AnimationWorkbenchRetargetView
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
                    NUnitAssert.That(
                        document.GetState().HasRetargetedAnimationA,
                        Is.False);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void RetargetView_UnloadReleasesOwnedPreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                using var document = CreateLoadedDocument();
                var controller = CreateController(document);
                var view = new AnimationWorkbenchRetargetView
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
                NUnitAssert.That(
                    document.GetState().HasActiveRetargetPreview,
                    Is.False);
            });
    }

    private static AnimationWorkbenchDocument CreateLoadedDocument()
    {
        var skeleton = CreateSkeleton();
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(skeleton),
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));
        return document;
    }

    private static AnimationWorkbenchRetargetController CreateController(
        AnimationWorkbenchDocument document) => new(
            document,
            AnimationWorkbenchSourceSlot.AnimationA,
            CharacterRetargetProfileStore.CreateForFile(Path.Combine(
                NUnit.Framework.TestContext.CurrentContext.WorkDirectory,
                $"animation-workbench-retarget-{Guid.NewGuid():N}.json")));

    private static GameSkeleton CreateSkeleton()
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test_skeleton",
            },
            Bones = new[]
            {
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = -1,
                },
                new AnimationFile.BoneInfo
                {
                    Id = 1,
                    Name = "spine_0",
                    ParentId = 0,
                },
                new AnimationFile.BoneInfo
                {
                    Id = 2,
                    Name = "cape",
                    ParentId = 0,
                },
            },
        };
        var frame = new AnimationFile.Frame();
        for (var index = 0; index < file.Bones.Length; index++)
        {
            frame.Transforms.Add(new RmvVector3(index, 0, 0));
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return new GameSkeleton(file, new AnimationPlayer());
    }

    private static AnimationClip CreateClip(GameSkeleton skeleton)
    {
        var frame = new AnimationClip.KeyFrame();
        for (var index = 0; index < skeleton.BoneCount; index++)
        {
            frame.Position.Add(skeleton.Translation[index]);
            frame.Rotation.Add(skeleton.Rotation[index]);
            frame.Scale.Add(Microsoft.Xna.Framework.Vector3.One);
        }
        var clip = new AnimationClip
        {
            Duration = AnimationTimebase.FromFramesPerSecond(1, 1).Duration,
        };
        clip.DynamicFrames.Add(frame);
        return clip;
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
            $"animation-workbench-retarget-{theme}.png"));
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

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
