using System.Windows;
using System.Windows.Controls;
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
public class AnimationWorkbenchTimelineViewTests
{
    [Test]
    public void Xaml_UsesDesignSystemAndRecyclingVirtualization()
    {
        var root = FindSolutionRoot();
        var xamlPath = Path.Combine(
            root,
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchTimelineView.xaml");
        var document = XDocument.Load(xamlPath);
        var source = File.ReadAllText(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                source,
                Does.Contain("AeEditor.PlaybackSlider"));
            NUnitAssert.That(source, Does.Contain("AeList.View"));
            NUnitAssert.That(source, Does.Contain("AeScrollBar.Compact"));
            NUnitAssert.That(
                source,
                Does.Contain("VirtualizingPanel.IsVirtualizing=\"True\""));
            NUnitAssert.That(
                source,
                Does.Contain("VirtualizationMode=\"Recycling\""));
            NUnitAssert.That(
                source,
                Does.Contain("AnimationWorkbenchTimelineTrack"));
            NUnitAssert.That(
                document.Descendants().Any(element =>
                    element.Name.LocalName == nameof(ListBox)),
                Is.True);
        });
    }

    [Test]
    public void DenseTimeline_RecyclesBoneRowsAndDrawsOnlyVisibleFrames()
    {
        using var document = CreateLoadedDocument(
            frameCount: 400,
            boneCount: 120);
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.AllSamples);
        controller.SelectFrame(6, extendRange: false, toggle: false);
        controller.SelectFrame(12, extendRange: true, toggle: false);

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var view = new AnimationWorkbenchTimelineView
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

                    var list = FindDescendants<ListBox>(view)
                        .Single(item => item.Name == "BoneTrackList");
                    var track = FindDescendants<
                            AnimationWorkbenchTimelineTrack>(view)
                        .First(item => item.IsRuler == false);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(list.Items.Count, Is.EqualTo(120));
                        NUnitAssert.That(
                            list.ItemContainerGenerator.ContainerFromIndex(0),
                            Is.Not.Null);
                        NUnitAssert.That(
                            list.ItemContainerGenerator.ContainerFromIndex(119),
                            Is.Null);
                        NUnitAssert.That(
                            controller.VisibleFrameIndices.Count,
                            Is.LessThan(controller.Timeline.FrameCount));
                        NUnitAssert.That(
                            track.LastRenderedMarkerCount,
                            Is.GreaterThan(0));
                        NUnitAssert.That(
                            track.LastRenderedMarkerCount,
                            Is.LessThanOrEqualTo(
                                controller.VisibleFrameIndices.Count));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void Timeline_RendersAcrossRequiredThemes()
    {
        using var document = CreateLoadedDocument(
            frameCount: 48,
            boneCount: 24);
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.AllSamples);
        controller.SelectFrame(6, extendRange: false, toggle: false);
        controller.SelectFrame(12, extendRange: true, toggle: false);

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
                        var view = new AnimationWorkbenchTimelineView
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

                            var bitmap = Render(window);
                            NUnitAssert.That(
                                bitmap.PixelWidth,
                                Is.GreaterThan(0),
                                theme.ToString());
                            NUnitAssert.That(
                                bitmap.PixelHeight,
                                Is.GreaterThan(0),
                                theme.ToString());
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
            $"animation-workbench-timeline-{theme}.png"));
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

    private static AnimationWorkbenchDocument CreateLoadedDocument(
        int frameCount,
        int boneCount)
    {
        var skeleton = CreateSkeleton(boneCount);
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(frameCount * 0.05),
        };
        for (var frameIndex = 0;
             frameIndex < frameCount;
             frameIndex++)
        {
            var frame = new AnimationClip.KeyFrame();
            for (var boneIndex = 0;
                 boneIndex < boneCount;
                 boneIndex++)
            {
                frame.Position.Add(new Vector3(frameIndex, boneIndex, 0));
                frame.Rotation.Add(Quaternion.Identity);
                frame.Scale.Add(Vector3.One);
            }
            clip.DynamicFrames.Add(frame);
        }

        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                clip,
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));
        return document;
    }

    private static GameSkeleton CreateSkeleton(int boneCount)
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "target_skeleton",
            },
            Bones = Enumerable.Range(0, boneCount)
                .Select(index => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = index == 0 ? "root" : $"bone_{index:D3}",
                    ParentId = index == 0
                        ? AnimationFile.BoneIndexNoParent
                        : index - 1,
                })
                .ToArray(),
        };
        var frame = new AnimationFile.Frame();
        for (var index = 0; index < boneCount; index++)
        {
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
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
