using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using Editors.AnimationVisualEditors.ContextMenu;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Ui.BaseDialogs.PackFileTree;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class AnimationWorkbenchShellTests
{
    [OneTimeSetUp]
    public void InitializeLocalization()
    {
        new LocalizationManager().LoadLanguage();
    }

    [Test]
    public void RegisterTools_EnablesWarhammer3Workbench()
    {
        var database = new EditorDatabase(null!, null!);

        new Editors.AnimationVisualEditors.DependencyInjectionContainer()
            .RegisterTools(database);

        var editor = database.GetEditorInfos().Single(item =>
            item.EditorEnum == EditorEnums.AnimationKeyFrame_Editor);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(editor.ViewModel,
                Is.EqualTo(typeof(AnimationWorkbenchViewModel)));
            NUnitAssert.That(editor.View,
                Is.EqualTo(typeof(AnimationWorkbenchView)));
            NUnitAssert.That(editor.AddToolbarButton, Is.True);
            NUnitAssert.That(editor.IsToolbarButtonEnabled, Is.True);
            NUnitAssert.That(editor.ToolbarName,
                Is.EqualTo("DisplayName.AnimationWorkbench"));
            NUnitAssert.That(editor.SupportedGames,
                Is.EqualTo(new[] { GameTypeEnum.Warhammer3 }));
            NUnitAssert.That(editor.Extensions, Is.Empty);
        });
    }

    [Test]
    public void OpenCommand_OnlyTargetsAnimFilesAndSelectsWorkbench()
    {
        var editorCreator = new Mock<IEditorCreator>();
        var workbench = new Mock<IEditorInterface>();
        var fileEditor = workbench.As<IFileEditor>();
        editorCreator.Setup(creator => creator.Create(
                EditorEnums.AnimationKeyFrame_Editor,
                It.IsAny<Action<IEditorInterface>>()))
            .Callback<EditorEnums, Action<IEditorInterface>?>(
                (_, initialize) => initialize?.Invoke(workbench.Object))
            .Returns(workbench.Object);
        var command = new OpenAnimationWorkbenchCommand(
            editorCreator.Object);
        var owner = new PackFileContainer("test.pack");
        var animation = PackFile.CreateFromBytes("idle.anim", [1]);
        var animationNode = new TreeNode(
            animation.Name,
            NodeType.File,
            owner,
            null,
            animation);
        var text = PackFile.CreateFromBytes("notes.txt", [1]);
        var textNode = new TreeNode(
            text.Name,
            NodeType.File,
            owner,
            null,
            text);

        command.Execute(animationNode);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(command.IsEnabled(animationNode), Is.True);
            NUnitAssert.That(command.IsEnabled(textNode), Is.False);
            NUnitAssert.That(command.GetDisplayName(animationNode),
                Is.EqualTo("在动画工作台中打开"));
        });
        editorCreator.Verify(creator => creator.Create(
            EditorEnums.AnimationKeyFrame_Editor,
            It.IsAny<Action<IEditorInterface>>()), Times.Once);
        fileEditor.Verify(editor => editor.LoadFile(animation), Times.Once);
    }

    [Test]
    public void ThreeKingdoms_LoadFileStaysDisabledWithoutReadingAnimation()
    {
        var dataSource = new Mock<IDataSource>(MockBehavior.Strict);
        var viewport = new Mock<IAnimationWorkbenchViewport>();
        var viewModel = new AnimationWorkbenchViewModel(
            viewport.Object,
            Mock.Of<IPackFileService>(),
            Mock.Of<ISkeletonAnimationLookUpHelper>(),
            Mock.Of<IStandardDialogs>(),
            new ApplicationSettingsService(GameTypeEnum.ThreeKingdoms));

        viewModel.LoadFile(new PackFile("idle.anim", dataSource.Object));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsWorkbenchEnabled, Is.False);
            NUnitAssert.That(viewModel.CanEdit, Is.False);
            NUnitAssert.That(viewModel.StatusText, Does.Contain("三国"));
            NUnitAssert.That(viewModel.Sources, Has.All.Matches<
                AnimationWorkbenchSourceItem>(source => !source.IsLoaded));
        });
        dataSource.VerifyNoOtherCalls();
        viewport.Verify(candidate => candidate.Show(
            It.IsAny<AnimationWorkbenchPreviewSnapshot>(),
            It.IsAny<CancellationToken>()), Times.Never);
        viewModel.Close();
    }

    [Test]
    public void Warhammer3_LoadFileEnablesEditingAndCreatesPreview()
    {
        var animationFile = CreateAnimationFile();
        var animation = PackFile.CreateFromBytes(
            "idle.anim",
            AnimationFile.ConvertToBytes(animationFile));
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(
                animation,
                null))
            .Returns("animations\\idle.anim");
        var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
        skeletonLookup.Setup(helper => helper.GetSkeletonFileFromName(
                "test_skeleton"))
            .Returns(CreateAnimationFile());
        var previewSession = new Mock<IAnimationWorkbenchPreviewSession>();
        var viewport = new Mock<IAnimationWorkbenchViewport>();
        viewport.Setup(candidate => candidate.Show(
                It.IsAny<AnimationWorkbenchPreviewSnapshot>(),
                It.IsAny<CancellationToken>()))
            .Returns(previewSession.Object);
        var viewModel = new AnimationWorkbenchViewModel(
            viewport.Object,
            packFileService.Object,
            skeletonLookup.Object,
            Mock.Of<IStandardDialogs>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));

        viewModel.LoadFile(animation);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsWorkbenchEnabled, Is.True);
            NUnitAssert.That(viewModel.CanEdit, Is.True);
            NUnitAssert.That(viewModel.Sources[0].IsLoaded, Is.True);
            NUnitAssert.That(viewModel.Sources[0].Name,
                Is.EqualTo("animations\\idle.anim"));
            NUnitAssert.That(viewModel.BoneNames,
                Is.EqualTo(new[] { "root" }));
        });
        viewport.Verify(candidate => candidate.Show(
            It.Is<AnimationWorkbenchPreviewSnapshot>(preview =>
                preview.Kind == AnimationWorkbenchPreviewKind.AnimationA),
            It.IsAny<CancellationToken>()), Times.Once);

        viewModel.ActivatePanel(AnimationWorkbenchPanelKind.Blend);
        var firstBlendController = viewModel.BlendController;
        viewModel.LoadFile(animation);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.ActivePanel,
                Is.EqualTo(AnimationWorkbenchPanelKind.Blend));
            NUnitAssert.That(viewModel.BlendController, Is.Not.Null);
            NUnitAssert.That(viewModel.BlendController,
                Is.Not.SameAs(firstBlendController));
        });
        viewModel.Close();
    }

    [Test]
    public void Warhammer3_LoadVersionEightStaticFileEnablesEditing()
    {
        var animationFile = CreateVersionEightStaticAnimationFile();
        var animation = PackFile.CreateFromBytes(
            "idle_v8.anim",
            AnimationFile.ConvertToBytes(animationFile));
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(
                animation,
                null))
            .Returns("animations\\idle_v8.anim");
        var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
        skeletonLookup.Setup(helper => helper.GetSkeletonFileFromName(
                "test_skeleton"))
            .Returns(CreateAnimationFile());
        var previewSession = new Mock<IAnimationWorkbenchPreviewSession>();
        var viewport = new Mock<IAnimationWorkbenchViewport>();
        viewport.Setup(candidate => candidate.Show(
                It.IsAny<AnimationWorkbenchPreviewSnapshot>(),
                It.IsAny<CancellationToken>()))
            .Returns(previewSession.Object);
        var viewModel = new AnimationWorkbenchViewModel(
            viewport.Object,
            packFileService.Object,
            skeletonLookup.Object,
            Mock.Of<IStandardDialogs>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));

        viewModel.LoadFile(animation);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsWorkbenchEnabled, Is.True);
            NUnitAssert.That(viewModel.CanEdit, Is.True);
            NUnitAssert.That(viewModel.Diagnostics, Is.Empty);
        });
        viewModel.Close();
    }

    [Test]
    public void Xaml_UsesFourZoneWorkspaceAndSharedSplitterStyles()
    {
        var root = FindSolutionRoot();
        var xamlPath = Path.Combine(
            root,
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source,
                Does.Contain("AeVerticalGridSplitterStyle"));
            NUnitAssert.That(source,
                Does.Contain("AeHorizontalGridSplitterStyle"));
            NUnitAssert.That(source,
                Does.Contain("AnimationWorkbenchTimelineView"));
            NUnitAssert.That(source,
                Does.Contain("AnimationWorkbenchBlendView"));
            NUnitAssert.That(source,
                Does.Contain("AnimationWorkbenchLayerView"));
            NUnitAssert.That(source,
                Does.Contain("AnimationWorkbenchRetargetView"));
            NUnitAssert.That(source,
                Does.Contain("AnimationWorkbenchMetaDataView"));
            NUnitAssert.That(source,
                Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(source, Does.Contain("AeFocus.Keyboard"));
            NUnitAssert.That(
                Regex.IsMatch(source, "#[0-9a-fA-F]{3,8}"),
                Is.False);
            NUnitAssert.That(document.Descendants().Count(element =>
                element.Name.LocalName == nameof(GridSplitter)),
                Is.EqualTo(3));
        });
    }

    [Test]
    public void ShellLocalization_ExplainsWarhammer3PreviewBoundary()
    {
        var languagePath = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json");
        using var json = JsonDocument.Parse(File.ReadAllText(languagePath));
        var keys = new[]
        {
            "DisplayName.AnimationWorkbench",
            "ContextMenu.OpenAnimationWorkbench",
            "AnimationWorkbench.Shell.Warhammer3Only",
            "AnimationWorkbench.Shell.ThreeKingdomsUnavailable",
            "AnimationWorkbench.Shell.SourceSkeletonMissing",
            "AnimationWorkbench.Shell.SaveUnavailable",
            "AnimationWorkbench.Shell.SourceSlotA",
            "AnimationWorkbench.Shell.SourceSlotB",
        };

        foreach (var key in keys)
        {
            NUnitAssert.That(
                json.RootElement.TryGetProperty(key, out var value),
                Is.True,
                key);
            NUnitAssert.That(value.GetString(), Is.Not.Empty, key);
        }
    }

    [Test]
    public void Shell_RendersAcrossRequiredThemes()
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
                        var view = new AnimationWorkbenchView();
                        var window = new Window
                        {
                            Width = 1600,
                            Height = 940,
                            Content = view,
                            ShowActivated = false,
                            ShowInTaskbar = false,
                            WindowStyle = WindowStyle.None,
                        };
                        try
                        {
                            window.Show();
                            window.UpdateLayout();
                            window.Dispatcher.Invoke(
                                () => { },
                                DispatcherPriority.ApplicationIdle);
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
                            NUnitAssert.That(bitmap.PixelWidth,
                                Is.GreaterThan(0), theme.ToString());
                            NUnitAssert.That(bitmap.PixelHeight,
                                Is.GreaterThan(0), theme.ToString());
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

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "AssetEditor.CN.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate the solution root.");
    }

    private static AnimationFile CreateAnimationFile()
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                SkeletonName = "test_skeleton",
                AnimationTotalPlayTimeInSec = 0.05f,
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
        part.TranslationMappings.Add(
            new AnimationFile.AnimationBoneMapping(0));
        part.RotationMappings.Add(
            new AnimationFile.AnimationBoneMapping(0));
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return file;
    }

    private static AnimationFile CreateVersionEightStaticAnimationFile()
    {
        var file = CreateAnimationFile();
        file.Header.Version = 8;
        file.Header.UnknownValue_v8 = 6;
        var part = file.AnimationParts.Single();
        part.TranslationMappings[0] =
            new AnimationFile.AnimationBoneMapping(10000);
        part.RotationMappings[0] =
            new AnimationFile.AnimationBoneMapping(10000);
        part.StaticFrame = part.DynamicFrames.Single();
        part.DynamicFrames.Clear();
        return file;
    }
}
