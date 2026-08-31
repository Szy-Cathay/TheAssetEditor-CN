using System.Text.Json;
using System.Text.RegularExpressions;
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
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public sealed class AnimationWorkbenchMetaDataViewTests
{
    [SetUp]
    public void InitializeLocalization() =>
        new LocalizationManager().LoadLanguage();

    [Test]
    public void MetaDataUiProblemsAndSaveDiagnosticsHaveChineseEntries()
    {
        var languagePath = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json");
        using var json = JsonDocument.Parse(File.ReadAllText(languagePath));
        var requiredKeys = new[]
        {
            "AnimationWorkbench.MetaData.Title",
            "AnimationWorkbench.MetaData.Subtitle",
            "AnimationWorkbench.MetaData.Synchronize",
            "AnimationWorkbench.MetaData.Summary",
            "AnimationWorkbench.MetaData.SummaryFormat",
            "AnimationWorkbench.MetaData.Unsaved",
            "AnimationWorkbench.MetaData.Saved",
            "AnimationWorkbench.MetaData.Disabled",
            "AnimationWorkbench.MetaData.Problems",
            "AnimationWorkbench.MetaData.ProblemsAutomationName",
            "AnimationWorkbench.MetaData.SelectProblemHint",
            "AnimationWorkbench.MetaData.Navigate",
            "AnimationWorkbench.MetaData.NavigationReady",
            "AnimationWorkbench.MetaData.NavigationUnavailable",
            "AnimationWorkbench.MetaData.DocumentChanged",
            "AnimationWorkbench.MetaData.Severity.Warning",
            "AnimationWorkbench.MetaData.Severity.Error",
            "AnimationWorkbench.MetaData.Source.AnimationA",
            "AnimationWorkbench.MetaData.Source.AnimationB",
            "AnimationWorkbench.MetaData.Source.Result",
            "AnimationWorkbench.MetaData.NotAvailable",
            "AnimationWorkbench.MetaData.TimeFormat",
            "AnimationWorkbench.MetaData.ProblemContextFormat",
            "AnimationWorkbench.MetaData.ProblemAutomationFormat",
        }
            .Concat(Enum.GetValues<AnimationWorkbenchMetaDataProblemCode>()
                .Select(code =>
                    $"AnimationWorkbench.MetaData.Problem.{code}"))
            .Concat(new[]
            {
                AnimationWorkbenchDiagnosticCode
                    .MetaDataSynchronizationDisabled,
                AnimationWorkbenchDiagnosticCode.MetaDataResultMissing,
                AnimationWorkbenchDiagnosticCode
                    .MetaDataCandidateSerializationFailed,
                AnimationWorkbenchDiagnosticCode
                    .MetaDataCandidateRoundTripMismatch,
            }.Select(code => $"AnimationWorkbench.Diagnostic.{code}"));

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
    public void Xaml_UsesSemanticResourcesLocalizationAndVirtualization()
    {
        var xamlPath = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchMetaDataView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeSurface.Panel"));
            NUnitAssert.That(source, Does.Contain("AeSurface.Control"));
            NUnitAssert.That(source, Does.Contain("AeInput.Switch"));
            NUnitAssert.That(source, Does.Contain("AeList.View"));
            NUnitAssert.That(source, Does.Contain("AeList.Item"));
            NUnitAssert.That(source, Does.Contain("AeButton.Quiet"));
            NUnitAssert.That(source, Does.Contain("AeFocus.Keyboard"));
            NUnitAssert.That(
                source,
                Does.Contain("VirtualizingPanel.IsVirtualizing=\"True\""));
            NUnitAssert.That(source, Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(
                Regex.IsMatch(source, "#[0-9a-fA-F]{3,8}"),
                Is.False);
            NUnitAssert.That(source, Does.Not.Contain("Padding=\"10,4\""));
            NUnitAssert.That(source, Does.Not.Contain("Margin=\"4,3\""));
            NUnitAssert.That(source, Does.Not.Contain("Margin=\"0,1,8,0\""));
            NUnitAssert.That(
                document.Descendants().Count(element =>
                    element.Name.LocalName == nameof(ListBox)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void MetaDataView_RendersProblemsAcrossRequiredThemes()
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
                        var view = new AnimationWorkbenchMetaDataView
                        {
                            Controller = new AnimationWorkbenchMetaDataController(
                                document),
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

                            var problemList = FindDescendants<ListBox>(view)
                                .Single(items => items.Name == "ProblemList");
                            var navigateButton = FindDescendants<Button>(view)
                                .Single(button => button.Name == "NavigateButton");
                            problemList.SelectedIndex = 0;
                            var bitmap = Render(window);
                            NUnitAssert.Multiple(() =>
                            {
                                NUnitAssert.That(
                                    problemList.Items.Count,
                                    Is.EqualTo(1));
                                NUnitAssert.That(
                                    navigateButton.IsEnabled,
                                    Is.False);
                                NUnitAssert.That(
                                    bitmap.PixelWidth,
                                    Is.GreaterThan(0));
                                NUnitAssert.That(
                                    bitmap.PixelHeight,
                                    Is.GreaterThan(0));
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

    private static AnimationWorkbenchDocument CreateLoadedDocument()
    {
        var skeleton = CreateSkeleton();
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var metadata = parser.GenerateBytes(
            2,
            new ParsedMetadataFile
            {
                Version = 2,
                Attributes =
                [
                    new Time
                    {
                        Name = "TIME",
                        Version = 10,
                        Data = [],
                        StartTime = 0.1f,
                        EndTime = 0.2f,
                    },
                ],
            });
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(),
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            GameTypeEnum.Warhammer3,
            skeleton,
            new AnimationWorkbenchMetaDataSourceInput(
                "animation_a.anim.meta",
                metadata),
            SynchronizeMetaData: false));
        return document;
    }

    private static AnimationClip CreateClip()
    {
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(1),
        };
        var frame = new AnimationClip.KeyFrame();
        frame.Position.Add(Vector3.Zero);
        frame.Rotation.Add(Quaternion.Identity);
        frame.Scale.Add(Vector3.One);
        clip.DynamicFrames.Add(frame);
        return clip;
    }

    private static GameSkeleton CreateSkeleton()
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test_skeleton",
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = -1,
                },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return new GameSkeleton(file, new AnimationPlayer());
    }

    private static Window Host(UIElement content) => new()
    {
        Width = 900,
        Height = 560,
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
            $"animation-workbench-metadata-{theme}.png"));
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
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AssetEditor.CN.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("AssetEditor.CN.sln");
    }
}
