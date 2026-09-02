using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Channels;
using System.Windows.Threading;
using System.Windows.Data;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using AnimationEditor.Common.AnimationPlayer;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using Editors.AnimationVisualEditors.ContextMenu;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.ByteParsing;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.Common.OperationProgress;

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
    public void RegisterTools_EnablesWarhammer3TrustedPreview()
    {
        var database = new EditorDatabase(null!, null!);

        new Editors.AnimationVisualEditors.DependencyInjectionContainer()
            .RegisterTools(database);

        var editor = database.GetEditorInfos().Single(item =>
            item.EditorEnum == EditorEnums.AnimationKeyFrame_Editor);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(editor.ViewModel,
                Is.EqualTo(typeof(TrustedAnimationPreviewViewModel)));
            NUnitAssert.That(editor.View,
                Is.EqualTo(typeof(TrustedAnimationPreviewView)));
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
    public void OpenCommand_TargetsSupportedModelsAndSelectsTrustedPreview()
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
            editorCreator.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));
        var threeKingdomsCommand = new OpenAnimationWorkbenchCommand(
            editorCreator.Object,
            new ApplicationSettingsService(GameTypeEnum.ThreeKingdoms));
        var owner = new PackFileContainer("test.pack");
        var model = PackFile.CreateFromBytes(
            "character.rigid_model_v2",
            [1]);
        var modelNode = new TreeNode(
            model.Name,
            NodeType.File,
            owner,
            null,
            model);
        var animation = PackFile.CreateFromBytes("idle.anim", [1]);
        var animationNode = new TreeNode(
            animation.Name,
            NodeType.File,
            owner,
            null,
            animation);
        var wsModel = PackFile.CreateFromBytes("character.wsmodel", [1]);
        var wsModelNode = new TreeNode(
            wsModel.Name,
            NodeType.File,
            owner,
            null,
            wsModel);
        var variantMesh = PackFile.CreateFromBytes(
            "character.variantmeshdefinition",
            [1]);
        var variantMeshNode = new TreeNode(
            variantMesh.Name,
            NodeType.File,
            owner,
            null,
            variantMesh);

        command.Execute(modelNode);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(command.IsEnabled(modelNode), Is.True);
            NUnitAssert.That(command.IsEnabled(wsModelNode), Is.True);
            NUnitAssert.That(command.IsEnabled(variantMeshNode), Is.True);
            NUnitAssert.That(command.IsEnabled(animationNode), Is.False);
            NUnitAssert.That(
                threeKingdomsCommand.IsEnabled(modelNode),
                Is.False);
            NUnitAssert.That(command.GetDisplayName(modelNode),
                Is.EqualTo("在可信动画预览中打开"));
        });
        editorCreator.Verify(creator => creator.Create(
            EditorEnums.AnimationKeyFrame_Editor,
            It.IsAny<Action<IEditorInterface>>()), Times.Once);
        fileEditor.Verify(editor => editor.LoadFile(model), Times.Once);
    }

    [Test]
    public void TrustedSession_ModelDeterminesConcreteSkeletonAndPageState()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile("humanoid01.anim", "humanoid01");
        var modelOwner = new PackFileContainer("my_mod.pack");
        var skeletonOwner = new PackFileContainer("data.pack")
        {
            IsCaPackFile = true,
        };
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(model, null))
            .Returns("variantmeshes\\wh_variantmodels\\character.rigid_model_v2");
        packFileService.Setup(service => service.GetPackFileContainer(model))
            .Returns(modelOwner);
        packFileService.Setup(service => service.FindFile(
                "animations\\skeletons\\humanoid01.anim",
                null))
            .Returns(skeleton);
        packFileService.Setup(service => service.GetFullPath(skeleton, null))
            .Returns("animations\\skeletons\\humanoid01.anim");
        packFileService.Setup(service => service.GetPackFileContainer(skeleton))
            .Returns(skeletonOwner);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(candidate => candidate.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.IsReady, Is.True);
            NUnitAssert.That(session.State.Model.Path, Is.EqualTo(
                "variantmeshes\\wh_variantmodels\\character.rigid_model_v2"));
            NUnitAssert.That(session.State.Model.Source, Is.EqualTo("my_mod.pack"));
            NUnitAssert.That(session.State.Skeleton.Path, Is.EqualTo(
                "animations\\skeletons\\humanoid01.anim"));
            NUnitAssert.That(session.State.Skeleton.Source, Is.EqualTo("data.pack"));
            NUnitAssert.That(session.State.Animation.IsResolved, Is.False);
            NUnitAssert.That(session.State.MeshCount, Is.EqualTo(3));
        });
        viewport.Verify(candidate => candidate.Load(model, skeleton), Times.Once);
    }

    [Test]
    public void TrustedSession_UnreadableSkeletonReportsDetailsOnSkeletonRow()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = PackFile.CreateFromBytes("humanoid01.anim", [1]);
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.IsReady, Is.False);
            NUnitAssert.That(session.State.Model.Diagnostic, Is.Empty);
            NUnitAssert.That(session.State.Skeleton.Diagnostic,
                Does.Contain("技术详情"));
        });
        viewport.Verify(candidate => candidate.Load(
            It.IsAny<PackFile>(),
            It.IsAny<PackFile>()), Times.Never);
    }

    [Test]
    public void TrustedSession_MismatchedSkeletonIdentityBlocksViewport()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "different_skeleton");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.IsReady, Is.False);
            NUnitAssert.That(session.State.Model.Diagnostic, Is.Empty);
            NUnitAssert.That(session.State.Skeleton.Diagnostic,
                Does.Contain("different_skeleton"));
        });
        viewport.Verify(candidate => candidate.Load(
            It.IsAny<PackFile>(),
            It.IsAny<PackFile>()), Times.Never);
    }

    [Test]
    public void TrustedSession_ViewportSkeletonFailureStaysOnSkeletonRow()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile("humanoid01.anim", "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(candidate => candidate.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Failure(
                TrustedAnimationPreviewResourceKind.Skeleton,
                "骨架详细错误"));
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.Model.Diagnostic, Is.Empty);
            NUnitAssert.That(session.State.Skeleton.Diagnostic,
                Is.EqualTo("骨架详细错误"));
        });
    }

    [Test]
    public void SceneObjectEditor_ClearSkeletonAlsoClearsSkeletonNode()
    {
        var player = new AnimationPlayer();
        var skeleton = new GameSkeleton(CreateAnimationFile(), player);
        var sceneObject = new SceneObject("trusted-preview")
        {
            ParentNode = new GroupNode("preview"),
            Player = player,
            Skeleton = skeleton,
            SkeletonSceneNode = new SkeletonNode(skeleton),
        };
        var editor = new SceneObjectEditor(
            null!,
            null!,
            Mock.Of<IPackFileService>(),
            null!,
            null!,
            Mock.Of<IEventHub>());

        editor.SetSkeleton(sceneObject, null!);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sceneObject.Skeleton, Is.Null);
            NUnitAssert.That(sceneObject.SkeletonSceneNode.Skeleton, Is.Null);
        });
    }

    [Test]
    public void TrustedSession_MissingSkeletonBlocksViewportWithRowDiagnostic()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "missing_skeleton");
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(model, null))
            .Returns("models\\character.rigid_model_v2");
        packFileService.Setup(service => service.GetPackFileContainer(model))
            .Returns(new PackFileContainer("my_mod.pack"));
        packFileService.Setup(service => service.FindFile(
                "animations\\skeletons\\missing_skeleton.anim",
                null))
            .Returns((PackFile?)null);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.IsReady, Is.False);
            NUnitAssert.That(session.State.Model.IsResolved, Is.True);
            NUnitAssert.That(session.State.Skeleton.Path, Is.EqualTo(
                "animations\\skeletons\\missing_skeleton.anim"));
            NUnitAssert.That(session.State.Skeleton.IsResolved, Is.False);
            NUnitAssert.That(session.State.Skeleton.Diagnostic,
                Is.Not.Empty);
        });
        viewport.Verify(candidate => candidate.Clear(), Times.Once);
        viewport.Verify(candidate => candidate.Load(
            It.IsAny<PackFile>(),
            It.IsAny<PackFile>()), Times.Never);
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
        viewModel.ActivatePanel(AnimationWorkbenchPanelKind.BaseAnimation);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.BaseAnimationController, Is.Not.Null);
            NUnitAssert.That(
                viewModel.BaseAnimationController?.Items,
                Is.Empty);
            NUnitAssert.That(
                viewModel.BaseAnimationController?.CanGenerate,
                Is.False);
            NUnitAssert.That(
                viewModel.BaseAnimationController?.CanSave,
                Is.False);
        });
        viewModel.Close();
    }

    [Test]
    public void PreviewModel_LoadsTogglesAndClearsWithoutReplacingSkeleton()
    {
        var animationFile = CreateAnimationFile();
        var animation = PackFile.CreateFromBytes(
            "idle.anim",
            AnimationFile.ConvertToBytes(animationFile));
        var model = PackFile.CreateFromBytes(
            "yangjian.rigid_model_v2",
            [1]);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(
                animation,
                null))
            .Returns("animations\\idle.anim");
        packFileService.Setup(service => service.GetFullPath(
                model,
                null))
            .Returns("test\\yangjian.rigid_model_v2");
        var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
        skeletonLookup.Setup(helper => helper.GetSkeletonFileFromName(
                "test_skeleton"))
            .Returns(CreateAnimationFile());
        var viewport = new Mock<IAnimationWorkbenchViewport>();
        viewport.Setup(candidate => candidate.Show(
                It.IsAny<AnimationWorkbenchPreviewSnapshot>(),
                It.IsAny<CancellationToken>()))
            .Returns(Mock.Of<IAnimationWorkbenchPreviewSession>());
        viewport.Setup(candidate => candidate.LoadModel(model))
            .Returns(new AnimationWorkbenchPreviewModelState(
                "other_skeleton",
                "test_skeleton",
                true));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(candidate => candidate.DisplayBrowseDialog(
                It.IsAny<List<string>>()))
            .Returns(new BrowseDialogResultFile(true, model));
        var viewModel = new AnimationWorkbenchViewModel(
            viewport.Object,
            packFileService.Object,
            skeletonLookup.Object,
            dialogs.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));
        viewModel.LoadFile(animation);

        viewModel.BrowsePreviewModelCommand.Execute(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.HasPreviewModel, Is.True);
            NUnitAssert.That(viewModel.CanClearPreviewModel, Is.True);
            NUnitAssert.That(viewModel.Sources, Has.Count.EqualTo(3));
            NUnitAssert.That(viewModel.Sources[2].Name,
                Is.EqualTo("yangjian.rigid_model_v2"));
            NUnitAssert.That(viewModel.Sources[2].FullPath,
                Is.EqualTo("test\\yangjian.rigid_model_v2"));
            NUnitAssert.That(viewModel.Sources[2].Details,
                Does.Contain("other_skeleton"));
            NUnitAssert.That(viewModel.Diagnostics,
                Has.Some.Contains("不匹配"));
        });
        dialogs.Verify(candidate => candidate.DisplayBrowseDialog(
                It.Is<List<string>>(extensions => extensions.SequenceEqual(
                    new[]
                    {
                        ".variantmeshdefinition",
                        ".wsmodel",
                        ".rigid_model_v2",
                    }))),
            Times.Once);

        viewModel.ShowPreviewModel = false;
        viewModel.ShowPreviewSkeleton = false;
        viewport.Verify(candidate => candidate.SetModelVisible(false),
            Times.Once);
        viewport.Verify(candidate => candidate.SetSkeletonVisible(false),
            Times.Once);

        viewModel.ClearPreviewModelCommand.Execute(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.HasPreviewModel, Is.False);
            NUnitAssert.That(viewModel.CanClearPreviewModel, Is.False);
            NUnitAssert.That(viewModel.Sources[2].IsLoaded, Is.False);
        });
        viewport.Verify(candidate => candidate.ClearModel(), Times.Once);
        viewModel.Close();
    }

    [Test]
    public void BaseAnimationTab_BindsControllerAndBrowseCommand()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
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
                var skeletonLookup = new Mock<
                    ISkeletonAnimationLookUpHelper>();
                skeletonLookup.Setup(helper => helper.GetSkeletonFileFromName(
                        "test_skeleton"))
                    .Returns(CreateAnimationFile());
                var viewport = new Mock<IAnimationWorkbenchViewport>();
                viewport.Setup(candidate => candidate.Show(
                        It.IsAny<AnimationWorkbenchPreviewSnapshot>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Mock.Of<IAnimationWorkbenchPreviewSession>());
                var dialogs = new Mock<IStandardDialogs>();
                dialogs.Setup(candidate => candidate.DisplayBrowseDialog(
                        It.IsAny<List<string>>()))
                    .Returns(new BrowseDialogResultFile(false, null!));
                var viewModel = new AnimationWorkbenchViewModel(
                    viewport.Object,
                    packFileService.Object,
                    skeletonLookup.Object,
                    dialogs.Object,
                    new ApplicationSettingsService(
                        GameTypeEnum.Warhammer3));
                viewModel.LoadFile(animation);
                var view = new AnimationWorkbenchView
                {
                    DataContext = viewModel,
                };
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
                    var tabs = (TabControl)view.FindName("ToolTabs");
                    tabs.SelectedItem = tabs.Items
                        .OfType<TabItem>()
                        .Single(item => Equals(item.Tag, "BaseAnimation"));
                    window.Dispatcher.Invoke(
                        () => { },
                        DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    SaveWindowForVisualReview(window, "shell-base");

                    var baseView = FindDescendants<
                            AnimationWorkbenchBaseAnimationView>(view)
                        .Single();
                    var selectButton = FindDescendants<Button>(baseView)
                        .Single(button =>
                            AutomationProperties.GetName(button) ==
                            LocalizationManager.Instance.Get(
                                "AnimationWorkbench.BaseAnimation.SelectDonor"));

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            viewModel.BaseAnimationController,
                            Is.Not.Null);
                        NUnitAssert.That(
                            baseView.Controller,
                            Is.SameAs(viewModel.BaseAnimationController));
                        NUnitAssert.That(selectButton.Command, Is.Not.Null);
                    });

                    selectButton.Command!.Execute(null);
                    dialogs.Verify(candidate => candidate.DisplayBrowseDialog(
                            It.Is<List<string>>(extensions =>
                                extensions.SequenceEqual(new[] { ".anim" }))),
                        Times.Once);
                }
                finally
                {
                    window.Close();
                    viewModel.Close();
                }
            });
    }

    [Test]
    public void MetaDataTab_BindsController()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var viewModel = new AnimationWorkbenchViewModel(
                    Mock.Of<IAnimationWorkbenchViewport>(),
                    Mock.Of<IPackFileService>(),
                    Mock.Of<ISkeletonAnimationLookUpHelper>(),
                    Mock.Of<IStandardDialogs>(),
                    new ApplicationSettingsService(
                        GameTypeEnum.Warhammer3));
                var view = new AnimationWorkbenchView
                {
                    DataContext = viewModel,
                };
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
                    var tabs = (TabControl)view.FindName("ToolTabs");
                    tabs.SelectedItem = tabs.Items
                        .OfType<TabItem>()
                        .Single(item => Equals(item.Tag, "MetaData"));
                    window.Dispatcher.Invoke(
                        () => { },
                        DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();

                    var metaDataView = FindDescendants<
                            AnimationWorkbenchMetaDataView>(view)
                        .Single();
                    NUnitAssert.That(
                        metaDataView.Controller,
                        Is.SameAs(viewModel.MetaDataController));
                }
                finally
                {
                    window.Close();
                    viewModel.Close();
                }
            });
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
        var workbenchViews = Directory.GetFiles(
                Path.GetDirectoryName(xamlPath)!,
                "*View.xaml")
            .Select(File.ReadAllText)
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source,
                Does.Contain("AeVerticalGridSplitterStyle"));
            NUnitAssert.That(source,
                Does.Contain("AeHorizontalGridSplitterStyle"));
            NUnitAssert.That(
                Regex.Matches(source, "AeSurface\\.Panel").Count,
                Is.EqualTo(1));
            NUnitAssert.That(
                workbenchViews.All(view =>
                    !view.Contains(
                        "AeSurface.Control",
                        StringComparison.Ordinal)),
                Is.True);
            NUnitAssert.That(source,
                Does.Not.Contain(
                    "HorizontalScrollBarVisibility=\"Auto\""));
            NUnitAssert.That(source,
                Does.Not.Contain("MinWidth=\"1060\""));
            NUnitAssert.That(source,
                Does.Not.Contain("MinWidth=\"1120\""));
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
                Does.Contain("AnimationWorkbenchBaseAnimationView"));
            NUnitAssert.That(source,
                Does.Contain("AnimationWorkbench.Shell.LoadPreviewModel"));
            NUnitAssert.That(source,
                Does.Contain("AeButton.VisibilityToggle"));
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

    [TestCase(7u)]
    [TestCase(8u)]
    public void TrustedSession_LoadsExactReadOnlyAnimationOnCurrentSkeleton(
        uint version)
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var animationFile = CreateAnimationFile("humanoid01");
        animationFile.Header.Version = version;
        var animation = PackFile.CreateFromBytes(
            "idle.anim",
            AnimationFile.ConvertToBytes(animationFile));
        var parsedAnimation = AnimationFile.Create(animation);
        var candidate = new TrustedAnimationCandidate(
            animation,
            "idle",
            @"animations\battle\idle.anim",
            "my_mod.pack",
            @"C:\mods\my_mod.pack",
            TrustedAnimationModelSourceRole.FolderProject,
            version,
            1,
            0.05,
            20);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        viewport.Setup(item => item.LoadAnimation(
                parsedAnimation,
                candidate.Path))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);
        session.LoadAnimation(candidate, parsedAnimation);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.Animation.IsResolved, Is.True);
            NUnitAssert.That(session.State.Animation.Path,
                Is.EqualTo(candidate.Path));
            NUnitAssert.That(session.State.Animation.Source,
                Is.EqualTo(candidate.SourcePack));
            NUnitAssert.That(session.SkeletonIdentity, Is.Not.Null);
        });
        viewport.Verify(item => item.LoadAnimation(
            parsedAnimation,
            candidate.Path), Times.Once);
    }

    [Test]
    public void TrustedSession_DoesNotBlockStaticOrMultipartReadOnlyFormats()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var staticAnimation = CreateVersionEightStaticAnimationFile();
        staticAnimation.Header.SkeletonName = "humanoid01";
        var multipartAnimation = CreateAnimationFile("humanoid01");
        multipartAnimation.Header.Version = 8;
        multipartAnimation.AnimationParts.Add(
            multipartAnimation.AnimationParts.Single());
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        viewport.Setup(item => item.LoadAnimation(
                It.IsAny<AnimationFile>(),
                It.IsAny<string>()))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);
        session.LoadModel(model);

        foreach (var (animation, path, partCount, hasStatic) in new[]
                 {
                     (staticAnimation,
                         @"animations\battle\static.anim",
                         1,
                         true),
                     (multipartAnimation,
                         @"animations\battle\multipart.anim",
                         2,
                         false),
                 })
        {
            var candidate = new TrustedAnimationCandidate(
                PackFile.CreateFromBytes(
                    Path.GetFileName(path),
                    [1]),
                Path.GetFileNameWithoutExtension(path),
                path,
                "my_mod.pack",
                @"C:\mods\my_mod.pack",
                TrustedAnimationModelSourceRole.FolderProject,
                8,
                1,
                animation.Header.AnimationTotalPlayTimeInSec,
                animation.Header.FrameRate,
                partCount,
                hasStatic,
                hasStatic);

            session.LoadAnimation(candidate, animation);
            NUnitAssert.That(
                session.State.Animation.IsResolved,
                Is.True,
                path);
        }

        viewport.Verify(item => item.LoadAnimation(
            It.IsAny<AnimationFile>(),
            It.IsAny<string>()), Times.Exactly(2));
    }

    [Test]
    public void TrustedViewModel_ExposesFrameStepAndLoopControls()
    {
        var playback = new TrustedAnimationPlaybackState(
            true,
            false,
            1,
            3,
            1d / 30,
            0.1,
            30,
            true,
            false,
            false,
            2);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.SetupGet(item => item.PlaybackState)
            .Returns(() => playback);
        viewport.Setup(item => item.PreviousFrame())
            .Callback(() => playback = playback with
            {
                CurrentFrame = 0,
                CurrentTimeSeconds = 0,
                IsPlaying = false,
            });
        viewport.Setup(item => item.NextFrame())
            .Callback(() => playback = playback with
            {
                CurrentFrame = 1,
                CurrentTimeSeconds = 1d / 30,
                IsPlaying = false,
            });
        viewport.Setup(item => item.SetLooping(false))
            .Callback(() => playback = playback with
            {
                IsLooping = false,
            });
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            Mock.Of<IPackFileService>(),
            Mock.Of<ITrustedAnimationModelDiscovery>(),
            Mock.Of<ITrustedAnimationDiscovery>());

        viewModel.PreviousFrameCommand.Execute(null);
        viewModel.NextFrameCommand.Execute(null);
        viewModel.IsLooping = false;

        NUnitAssert.Multiple(() =>
        {
            viewport.Verify(item => item.PreviousFrame(), Times.Once);
            viewport.Verify(item => item.NextFrame(), Times.Once);
            viewport.Verify(item => item.SetLooping(false), Times.Once);
            NUnitAssert.That(viewModel.IsLooping, Is.False);
        });
        viewModel.Close();
    }

    [Test]
    public void TrustedSession_RejectsHelperBoneWithActionableAnimationDiagnostic()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var animationFile = CreateAnimationFile("humanoid01");
        animationFile.Header.Version = 8;
        animationFile.Bones =
        [
            .. animationFile.Bones,
            new AnimationFile.BoneInfo
            {
                Id = 1,
                Name = "helper",
                ParentId = 0,
            },
        ];
        var candidate = new TrustedAnimationCandidate(
            PackFile.CreateFromBytes("helper.anim", [1]),
            "helper",
            @"animations\battle\helper.anim",
            "reference.pack",
            @"C:\packs\reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack,
            8,
            1,
            0.05,
            20);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(model);
        session.LoadAnimation(candidate, animationFile);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.Animation.IsResolved, Is.False);
            NUnitAssert.That(session.State.Animation.Diagnostic,
                Does.Contain(candidate.Path));
            NUnitAssert.That(session.State.Animation.Diagnostic,
                Does.Contain(candidate.SourcePack));
            NUnitAssert.That(session.State.Animation.Diagnostic,
                Does.Contain("2 个骨骼"));
            NUnitAssert.That(session.State.Animation.Diagnostic,
                Does.Contain("1 个骨骼"));
        });
        viewport.Verify(item => item.LoadAnimation(
            It.IsAny<AnimationFile>(),
            It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TrustedViewModel_ModelReloadRejectsLateAnimationResults()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        viewport.SetupGet(item => item.PlaybackState)
            .Returns(TrustedAnimationPlaybackState.Empty);
        var discovery = new ControllableAnimationDiscovery();
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            packFileService.Object,
            Mock.Of<ITrustedAnimationModelDiscovery>(),
            discovery);
        viewModel.LoadFile(model);

        var scan = viewModel.StartAnimationDiscoveryAsync();
        await discovery.Started;
        viewModel.LoadFile(model);
        await discovery.Writer.WriteAsync(new[]
        {
            new TrustedAnimationCandidate(
                PackFile.CreateFromBytes("late.anim", [1]),
                "late",
                @"animations\battle\late.anim",
                "data.pack",
                @"C:\packs\data.pack",
                TrustedAnimationModelSourceRole.CaPack,
                8,
                10,
                0.5,
                20),
        });
        discovery.Writer.TryComplete();
        await scan;

        NUnitAssert.That(viewModel.AnimationCandidates, Is.Empty);
        viewModel.Close();
    }

    [Test]
    public void TrustedSession_LoadsResolvedWsModelRootWithConcreteSkeleton()
    {
        var root = PackFile.CreateFromBytes(
            "character.wsmodel",
            [1]);
        var geometry = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            geometry,
            skeleton,
            "humanoid01");
        var rootOwner = new PackFileContainer("my_mod.pack")
        {
            Role = PackFileContainerRole.ProjectWorkspace,
        };
        rootOwner.FileList["models\\character.wsmodel"] = root;
        packFileService.Setup(service => service.GetFullPath(root, null))
            .Returns("models\\character.wsmodel");
        packFileService.Setup(service => service.GetPackFileContainer(root))
            .Returns(rootOwner);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(root, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(5));
        var session = new TrustedAnimationPreviewFeatureSession(
            viewport.Object,
            packFileService.Object);

        session.LoadModel(root, geometry, skeleton);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(session.State.IsReady, Is.True);
            NUnitAssert.That(session.State.Model.Path,
                Is.EqualTo("models\\character.wsmodel"));
            NUnitAssert.That(session.State.MeshCount, Is.EqualTo(5));
            NUnitAssert.That(session.SkeletonIdentity?.Name,
                Is.EqualTo("humanoid01"));
        });
        viewport.Verify(item => item.Load(root, skeleton), Times.Once);
    }

    [TestCase("character.wsmodel")]
    [TestCase("character.variantmeshdefinition")]
    public async Task TrustedViewModel_RejectsLateCompositeModelResolution(
        string rootName)
    {
        var root = PackFile.CreateFromBytes(
            rootName,
            [1]);
        var directModel = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            directModel,
            skeleton,
            "humanoid01");
        var rootOwner = new PackFileContainer("my_mod.pack")
        {
            Role = PackFileContainerRole.ProjectWorkspace,
        };
        var rootPath = $"models\\{rootName}";
        rootOwner.FileList[rootPath] = root;
        packFileService.Setup(service => service.GetFullPath(root, null))
            .Returns(rootPath);
        packFileService.Setup(service => service.GetPackFileContainer(root))
            .Returns(rootOwner);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(directModel, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        viewport.SetupGet(item => item.PlaybackState)
            .Returns(TrustedAnimationPlaybackState.Empty);
        var completion = new TaskCompletionSource<
            TrustedWsModelResolutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new Mock<ITrustedWsModelResolver>();
        resolver.Setup(item => item.ResolveAsync(
                root,
                It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            packFileService.Object,
            Mock.Of<ITrustedAnimationModelDiscovery>(),
            Mock.Of<ITrustedAnimationDiscovery>(),
            resolver.Object);

        var lateLoad = viewModel.LoadFileAsync(root);
        await viewModel.LoadFileAsync(directModel);
        completion.SetResult(TrustedWsModelResolutionResult.Success(
            new TrustedWsModelResolution(
                root,
                directModel,
                skeleton,
                [],
                1,
                0)));
        await lateLoad;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.Model.Path,
                Is.EqualTo("models\\character.rigid_model_v2"));
            NUnitAssert.That(viewModel.IsReady, Is.True);
        });
        viewport.Verify(item => item.Load(root, skeleton), Times.Never);
        viewport.Verify(item => item.Load(directModel, skeleton), Times.Once);
        viewModel.Close();
    }

    [Test]
    public async Task TrustedViewModel_LoadsResolvedVariantMeshRoot()
    {
        var root = PackFile.CreateFromBytes(
            "character.variantmeshdefinition",
            [1]);
        var geometry = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            geometry,
            skeleton,
            "humanoid01");
        var rootOwner = new PackFileContainer("my_mod.pack")
        {
            Role = PackFileContainerRole.ProjectWorkspace,
        };
        rootOwner.FileList[
            "variants\\character.variantmeshdefinition"] = root;
        packFileService.Setup(service => service.GetFullPath(root, null))
            .Returns("variants\\character.variantmeshdefinition");
        packFileService.Setup(service => service.GetPackFileContainer(root))
            .Returns(rootOwner);
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(root, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(6));
        viewport.SetupGet(item => item.PlaybackState)
            .Returns(TrustedAnimationPlaybackState.Empty);
        var resolver = new Mock<ITrustedWsModelResolver>();
        resolver.Setup(item => item.ResolveAsync(
                root,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TrustedWsModelResolutionResult.Success(
                new TrustedWsModelResolution(
                    root,
                    geometry,
                    skeleton,
                    [],
                    2,
                    1)));
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            packFileService.Object,
            Mock.Of<ITrustedAnimationModelDiscovery>(),
            Mock.Of<ITrustedAnimationDiscovery>(),
            resolver.Object);

        await viewModel.LoadFileAsync(root);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsReady, Is.True);
            NUnitAssert.That(viewModel.Model.Path,
                Is.EqualTo("variants\\character.variantmeshdefinition"));
            NUnitAssert.That(viewModel.MeshCount, Is.EqualTo(6));
        });
        resolver.Verify(item => item.ResolveAsync(
            root,
            It.IsAny<CancellationToken>()), Times.Once);
        viewport.Verify(item => item.Load(root, skeleton), Times.Once);
        viewModel.Close();
    }

    [Test]
    public async Task TrustedPreview_SelectionCommandsUseStandardPackBrowser()
    {
        var model = CreateRigidModelHeaderFile(
            "character.rigid_model_v2",
            "humanoid01");
        var skeleton = CreateSkeletonPackFile(
            "humanoid01.anim",
            "humanoid01");
        var packFileService = CreateTrustedPreviewPackService(
            model,
            skeleton,
            "humanoid01");
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.DisplayBrowseDialog(
                It.Is<List<string>>(extensions =>
                    extensions.Contains(".rigid_model_v2"))))
            .Returns(new BrowseDialogResultFile(true, model));
        dialogs.Setup(item => item.DisplayBrowseDialog(
                It.Is<List<string>>(extensions =>
                    extensions.SequenceEqual(new[] { ".anim" }))))
            .Returns(new BrowseDialogResultFile(false, null!));
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        viewport.Setup(item => item.Load(model, skeleton))
            .Returns(TrustedAnimationPreviewViewportResult.Success(3));
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            packFileService.Object,
            Mock.Of<ITrustedAnimationModelDiscovery>(),
            Mock.Of<ITrustedAnimationDiscovery>(),
            Mock.Of<ITrustedWsModelResolver>(),
            dialogs.Object);

        await viewModel.OpenModelPickerCommand.ExecuteAsync(null);
        await viewModel.OpenAnimationPickerCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            dialogs.Verify(item => item.DisplayBrowseDialog(
                It.Is<List<string>>(extensions =>
                    extensions.SequenceEqual(new[]
                    {
                        ".variantmeshdefinition",
                        ".wsmodel",
                        ".rigid_model_v2",
                    }))), Times.Once);
            dialogs.Verify(item => item.DisplayBrowseDialog(
                It.Is<List<string>>(extensions =>
                    extensions.SequenceEqual(new[] { ".anim" }))),
                Times.Once);
            NUnitAssert.That(viewModel.IsReady, Is.True);
        });
        viewModel.Close();
    }

    [Test]
    public void TrustedPreviewXaml_UsesStandardEditorLayoutAndControls()
    {
        var xamlPath = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "TrustedAnimationPreviewView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                Regex.Matches(source, "AeSurface\\.Panel").Count,
                Is.EqualTo(1));
            NUnitAssert.That(source, Does.Contain(
                "AnimationWorkbench.TrustedPreview.Model"));
            NUnitAssert.That(source, Does.Contain(
                "AnimationWorkbench.TrustedPreview.Skeleton"));
            NUnitAssert.That(source, Does.Contain(
                "AnimationWorkbench.TrustedPreview.Animation"));
            NUnitAssert.That(source, Does.Contain(
                "Content=\"{Binding GameWorld}\""));
            NUnitAssert.That(
                Regex.Matches(source, "AeButton\\.VisibilityToggle").Count,
                Is.EqualTo(2));
            NUnitAssert.That(source, Does.Contain(
                "<player:AnimationPlayerView"));
            NUnitAssert.That(source, Does.Contain(
                "x:Name=\"ResourceInspector\""));
            NUnitAssert.That(source, Does.Contain(
                "OpenModelPickerCommand"));
            NUnitAssert.That(source, Does.Contain(
                "OpenAnimationPickerCommand"));
            NUnitAssert.That(source, Does.Not.Contain(
                "AeEditor.PlaybackSlider"));
            NUnitAssert.That(source, Does.Not.Contain(
                "AeSurface.Overlay"));
            NUnitAssert.That(source, Does.Not.Contain(
                "ModelCandidatesView"));
            NUnitAssert.That(source, Does.Not.Contain(
                "AnimationCandidatesView"));
            NUnitAssert.That(source, Does.Not.Contain("▶"));
            NUnitAssert.That(source, Does.Not.Contain("TabControl"));
            NUnitAssert.That(source, Does.Not.Contain(
                "AnimationWorkbenchBlendView"));
            NUnitAssert.That(source, Does.Not.Contain(
                "AnimationWorkbenchBaseAnimationView"));
            NUnitAssert.That(source, Does.Not.Contain(
                "SaveAsNewResource"));
            NUnitAssert.That(document.Descendants().Count(element =>
                element.Name.LocalName == nameof(Expander)),
                Is.EqualTo(3));
            NUnitAssert.That(document.Descendants().Count(element =>
                element.Name.LocalName == nameof(GridSplitter)),
                Is.EqualTo(1));
            NUnitAssert.That(document.Descendants().Count(element =>
                element.Name.LocalName == nameof(ListBox)),
                Is.EqualTo(0));
            NUnitAssert.That(document.Descendants().Count(element =>
                element.Name.LocalName == nameof(ProgressBar)),
                Is.EqualTo(0));
        });
    }

    [Test]
    public void TrustedPreview_RendersAcrossRequiredThemes()
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
                        var view = new TrustedAnimationPreviewView
                        {
                            DataContext = new TrustedPreviewShellState(),
                        };
                        var window = new Window
                        {
                            Width = 1280,
                            Height = 820,
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

                            var bitmap = new RenderTargetBitmap(
                                (int)window.ActualWidth,
                                (int)window.ActualHeight,
                                96,
                                96,
                                PixelFormats.Pbgra32);
                            bitmap.Render(window);
                            NUnitAssert.That(
                                bitmap.PixelWidth,
                                Is.GreaterThan(0),
                                theme.ToString());
                            NUnitAssert.That(
                                bitmap.PixelHeight,
                                Is.GreaterThan(0),
                                theme.ToString());
                            SaveWindowForVisualReview(
                                window,
                                $"trusted-{theme}");
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
    public void TrustedPreview_RendersAtReleaseResolutionsAndScales()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                try
                {
                    ThemesController.SetTheme(ThemeType.DarkTheme);
                    var cases = new[]
                    {
                        (1366, 768, 1.0),
                        (1366, 768, 1.5),
                        (1920, 1080, 1.0),
                        (1920, 1080, 1.5),
                        (2560, 1440, 1.0),
                        (2560, 1440, 1.5),
                    };
                    foreach (var (pixelWidth, pixelHeight, scale) in cases)
                    {
                        var view = new TrustedAnimationPreviewView
                        {
                            DataContext = new TrustedPreviewShellState(
                                showPickers: false),
                        };
                        var window = new Window
                        {
                            Width = pixelWidth / scale,
                            Height = pixelHeight / scale,
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

                            var viewport = FindDescendants<ContentControl>(view)
                                .Single(control =>
                                    AutomationProperties.GetName(control) ==
                                    LocalizationManager.Instance.Get(
                                        "AnimationWorkbench.TrustedPreview.Viewport"));
                            var timeline = FindDescendants<Slider>(view)
                                .Single();
                            var resourceFields = FindDescendants<TextBox>(view)
                                .Where(field => field.IsReadOnly &&
                                                field.IsVisible &&
                                                field.ActualHeight > 0)
                                .ToArray();
                            var toggles = FindDescendants<ToggleButton>(view)
                                .Where(toggle => toggle.IsVisible &&
                                    (AutomationProperties.GetName(toggle) ==
                                     LocalizationManager.Instance.Get(
                                         "AnimationWorkbench.TrustedPreview.ShowModel") ||
                                     AutomationProperties.GetName(toggle) ==
                                     LocalizationManager.Instance.Get(
                                         "AnimationWorkbench.TrustedPreview.ShowSkeleton")))
                                .ToArray();
                            var resourceInspectorScrollViewer =
                                (ScrollViewer)view.FindName(
                                    "ResourceInspectorScrollViewer");
                            var bitmap = new RenderTargetBitmap(
                                pixelWidth,
                                pixelHeight,
                                96 * scale,
                                96 * scale,
                                PixelFormats.Pbgra32);
                            bitmap.Render(window);

                            NUnitAssert.Multiple(() =>
                            {
                                var caseName =
                                    $"{pixelWidth}x{pixelHeight} @{scale:P0}";
                                NUnitAssert.That(resourceFields.Length,
                                    Is.GreaterThanOrEqualTo(3),
                                    caseName);
                                NUnitAssert.That(toggles.Length,
                                    Is.EqualTo(2),
                                    caseName);
                                NUnitAssert.That(
                                    IsFullyVisible(view, viewport),
                                    Is.True,
                                    $"viewport {caseName}");
                                NUnitAssert.That(
                                    IsFullyVisible(view, timeline),
                                    Is.True,
                                    $"timeline {caseName}");
                                NUnitAssert.That(viewport.ActualHeight,
                                    Is.GreaterThan(200),
                                    caseName);
                                NUnitAssert.That(timeline.ActualWidth,
                                    Is.GreaterThan(200),
                                    caseName);
                                NUnitAssert.That(
                                    resourceInspectorScrollViewer.ScrollableWidth,
                                    Is.LessThanOrEqualTo(0.5),
                                    $"horizontal scroll {caseName}");
                                NUnitAssert.That(
                                    resourceInspectorScrollViewer
                                        .ComputedHorizontalScrollBarVisibility,
                                    Is.Not.EqualTo(Visibility.Visible),
                                    $"horizontal scroll {caseName}");
                                NUnitAssert.That(bitmap.PixelWidth,
                                    Is.EqualTo(pixelWidth));
                                NUnitAssert.That(bitmap.PixelHeight,
                                    Is.EqualTo(pixelHeight));
                            });
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
    public void TrustedPreview_DoesNotExposeSaveableEditorState()
    {
        NUnitAssert.That(
            typeof(ISaveableEditor).IsAssignableFrom(
                typeof(TrustedAnimationPreviewViewModel)),
            Is.False);
    }

    [Test]
    public void TrustedPreview_RendersResourceInspectorOnTheRight()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                try
                {
                    ThemesController.SetTheme(ThemeType.DarkTheme);
                    var view = new TrustedAnimationPreviewView
                    {
                        DataContext = new TrustedPreviewShellState(),
                    };
                    var window = new Window
                    {
                        Width = 1280,
                        Height = 820,
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
                        var inspector = (FrameworkElement)view.FindName(
                            "ResourceInspector");
                        var viewport = FindDescendants<ContentControl>(view)
                            .Single(control =>
                                AutomationProperties.GetName(control) ==
                                LocalizationManager.Instance.Get(
                                    "AnimationWorkbench.TrustedPreview.Viewport"));
                        var player = FindDescendants<AnimationPlayerView>(view)
                            .Single();
                        var inspectorOrigin = inspector.TranslatePoint(
                            new Point(),
                            view);
                        var viewportOrigin = viewport.TranslatePoint(
                            new Point(),
                            view);
                        NUnitAssert.Multiple(() =>
                        {
                            NUnitAssert.That(inspectorOrigin.X,
                                Is.GreaterThanOrEqualTo(
                                    viewportOrigin.X +
                                    viewport.ActualWidth));
                            NUnitAssert.That(inspector.ActualWidth,
                                Is.GreaterThanOrEqualTo(280));
                            NUnitAssert.That(player.IsVisible, Is.True);
                            NUnitAssert.That(player.ActualWidth,
                                Is.GreaterThan(400));
                            NUnitAssert.That(
                                FindDescendants<ListBox>(view),
                                Is.Empty);
                            NUnitAssert.That(window.ActualWidth,
                                Is.EqualTo(1280).Within(1));
                        });
                        SaveWindowForVisualReview(
                            window,
                            "trusted-standard-layout");
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
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
            "AnimationWorkbench.TrustedPreview.Model",
            "AnimationWorkbench.TrustedPreview.Skeleton",
            "AnimationWorkbench.TrustedPreview.Animation",
            "AnimationWorkbench.TrustedPreview.NotSelected",
            "AnimationWorkbench.TrustedPreview.Diagnostic",
            "AnimationWorkbench.TrustedPreview.ShowModel",
            "AnimationWorkbench.TrustedPreview.ShowSkeleton",
            "AnimationWorkbench.TrustedPreview.Viewport",
            "AnimationWorkbench.TrustedPreview.FocusModel",
            "AnimationWorkbench.TrustedPreview.FrontView",
            "AnimationWorkbench.TrustedPreview.ResetCamera",
            "AnimationWorkbench.TrustedPreview.NoAnimation",
            "AnimationWorkbench.TrustedPreview.ReadOnlyTimeline",
            "AnimationWorkbench.TrustedPreview.SourceUnknown",
            "AnimationWorkbench.TrustedPreview.ModelTypeInvalid",
            "AnimationWorkbench.TrustedPreview.ModelUnreadableDetails",
            "AnimationWorkbench.TrustedPreview.SkeletonUndeclared",
            "AnimationWorkbench.TrustedPreview.SkeletonMissing",
            "AnimationWorkbench.TrustedPreview.ViewportLoadFailed",
            "AnimationWorkbench.TrustedPreview.ViewportLoadFailedDetails",
            "AnimationWorkbench.ModelPicker.Open",
            "AnimationWorkbench.ModelPicker.Title",
            "AnimationWorkbench.ModelPicker.Close",
            "AnimationWorkbench.ModelPicker.Search",
            "AnimationWorkbench.ModelPicker.Results",
            "AnimationWorkbench.ModelPicker.Cancel",
            "AnimationWorkbench.ModelPicker.UseSelected",
            "AnimationWorkbench.ModelPicker.Ready",
            "AnimationWorkbench.ModelPicker.Scanning",
            "AnimationWorkbench.ModelPicker.Complete",
            "AnimationWorkbench.ModelPicker.Cancelled",
            "AnimationWorkbench.ModelPicker.Failed",
            "AnimationWorkbench.ModelPicker.Loading",
            "AnimationWorkbench.ModelPicker.Loaded",
            "AnimationWorkbench.ModelPicker.SourceProject",
            "AnimationWorkbench.ModelPicker.SourceReference",
            "AnimationWorkbench.ModelPicker.SourceCa",
            "AnimationWorkbench.Shell.Warhammer3Only",
            "AnimationWorkbench.Shell.ThreeKingdomsUnavailable",
            "AnimationWorkbench.Shell.SourceSkeletonMissing",
            "AnimationWorkbench.Shell.SaveUnavailable",
            "AnimationWorkbench.Shell.SourceSlotA",
            "AnimationWorkbench.Shell.SourceSlotB",
            "AnimationWorkbench.Shell.LoadPreviewModel",
            "AnimationWorkbench.Shell.PreviewModelSlot",
            "AnimationWorkbench.Shell.PreviewModelLoadFailed",
            "AnimationWorkbench.Shell.PreviewModelSkeletonMismatch",
            "AnimationWorkbench.Shell.PreviewModelVisibility",
            "AnimationWorkbench.Shell.PreviewSkeletonVisibility",
            "AnimationWorkbench.Shell.ClearPreviewModel",
            "AnimationWorkbench.Shell.BaseAnimation",
            "AnimationWorkbench.BaseAnimation.Title",
            "AnimationWorkbench.BaseAnimation.AnimationSetHint",
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
                        var view = new AnimationWorkbenchView
                        {
                            DataContext = new WorkbenchShellPreviewState(),
                        };
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
                            SaveWindowForVisualReview(
                                window,
                                $"shell-{theme}");
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
    public void BaseAnimationView_UsesSharedStylesAndRendersAcrossThemes()
    {
        var root = FindSolutionRoot();
        var xamlPath = Path.Combine(
            root,
            "Editors",
            "AnimationEditor",
            "AnimationWorkbench",
            "AnimationWorkbenchBaseAnimationView.xaml");
        var source = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Not.Contain("AeSurface.Panel"));
            NUnitAssert.That(source, Does.Not.Contain("AeSurface.Control"));
            NUnitAssert.That(source, Does.Contain("AeTable.Grid"));
            NUnitAssert.That(source,
                Does.Contain(
                    "ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\""));
            NUnitAssert.That(source,
                Does.Contain("OperationProgressWindowHost"));
            NUnitAssert.That(source,
                Does.Contain("ActiveCancelCommand"));
            NUnitAssert.That(source,
                Does.Contain("IsProgressIndeterminate"));
            NUnitAssert.That(source,
                Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(
                Regex.IsMatch(source, "#[0-9a-fA-F]{3,8}"),
                Is.False);
            NUnitAssert.That(
                document.Descendants().Count(element =>
                    element.Name.LocalName is nameof(DataGridTextColumn)
                        or nameof(DataGridTemplateColumn)),
                Is.EqualTo(4));
        });

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
                        var window = new Window
                        {
                            Width = 720,
                            Height = 820,
                            Content =
                                new AnimationWorkbenchBaseAnimationView(),
                            ShowActivated = false,
                            ShowInTaskbar = false,
                            WindowStyle = WindowStyle.None,
                        };
                        try
                        {
                            window.Show();
                            window.UpdateLayout();
                            NUnitAssert.That(window.ActualWidth,
                                Is.GreaterThan(0), theme.ToString());
                            NUnitAssert.That(window.ActualHeight,
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

    [Test]
    public void BaseAnimationView_RendersSelectedErrorAndProgressStates()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var row = new BaseAnimationRowState
                {
                    IsSelected = true,
                    Role = AnimationWorkbenchBaseAnimationRole.Death,
                    SourcePath = @"animations\battle\donor\death.anim",
                    OutputPath = @"animations\battle\external\death.anim",
                    StatusText = "失败",
                    DetailText = "输出路径与原始动画相同",
                };
                ICommand cancelCommand = new RelayCommand(() => { });
                var loadingState = new BaseAnimationViewState
                {
                    Items = [row],
                    SelectedItem = row,
                    StatusText = "正在生成基础动画",
                    IsBusy = true,
                    IsProgressIndeterminate = false,
                    ProgressValue = 2,
                    ProgressMaximum = 5,
                    ProgressDetail = row.SourcePath,
                    ActiveCancelCommand = cancelCommand,
                };

                RenderAndAssertBaseAnimationState(
                    loadingState,
                    view =>
                    {
                        var grid = FindDescendants<DataGrid>(view).Single();
                        var progress = FindDescendants<
                            OperationProgressWindowHost>(view).Single();
                        var selectedCheckBox = FindDescendants<CheckBox>(view)
                            .Single(checkBox =>
                                AutomationProperties.GetName(checkBox) ==
                                LocalizationManager.Instance.Get(
                                    "AnimationWorkbench.BaseAnimation.Selected"));
                        NUnitAssert.Multiple(() =>
                        {
                            NUnitAssert.That(grid.SelectedItem, Is.SameAs(row));
                            NUnitAssert.That(
                                ScrollViewer.GetHorizontalScrollBarVisibility(
                                    grid),
                                Is.EqualTo(ScrollBarVisibility.Disabled));
                            NUnitAssert.That(
                                grid.Columns.Sum(column => column.ActualWidth),
                                Is.LessThanOrEqualTo(grid.ActualWidth + 1));
                            NUnitAssert.That(selectedCheckBox.IsChecked, Is.True);
                            NUnitAssert.That(progress.IsOperationActive, Is.True);
                            NUnitAssert.That(
                                progress.IsProgressIndeterminate,
                                Is.False);
                            NUnitAssert.That(
                                progress.CancelCommand,
                                Is.SameAs(cancelCommand));
                            NUnitAssert.That(
                                progress.CurrentDetailText,
                                Is.EqualTo(row.SourcePath));
                        });
                    });

                var savingState = new BaseAnimationViewState
                {
                    Items = [row],
                    SelectedItem = row,
                    StatusText = "正在保存基础动画",
                    IsBusy = true,
                    IsProgressIndeterminate = true,
                    ProgressMaximum = 1,
                    ActiveCancelCommand = null,
                };
                RenderAndAssertBaseAnimationState(
                    savingState,
                    view =>
                    {
                        var progress = FindDescendants<
                            OperationProgressWindowHost>(view).Single();
                        NUnitAssert.Multiple(() =>
                        {
                            NUnitAssert.That(progress.IsOperationActive, Is.True);
                            NUnitAssert.That(
                                progress.IsProgressIndeterminate,
                                Is.True);
                            NUnitAssert.That(progress.CancelCommand, Is.Null);
                        });
                    });
            });
    }

    [Test]
    public void BaseAnimationView_RendersDenseRecipeWithoutHorizontalScroll()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var rows = Enumerable.Range(1, 24)
                    .Select(index => new BaseAnimationRowState
                    {
                        IsSelected = index % 4 == 0,
                        Role = AnimationWorkbenchBaseAnimationRole.Death,
                        SourcePath =
                            $@"animations\battle\humanoid01\2handed_sword\candidate_{index:00}.anim",
                        OutputPath =
                            $@"animations\battle\external\base\candidate_{index:00}.anim",
                        StatusText = index % 3 == 0 ? "候选" : "待生成",
                        DetailText = "已匹配目标骨架，等待生成预览。",
                    })
                    .ToArray();
                var selected = rows[7];
                var state = new BaseAnimationViewState
                {
                    Items = rows,
                    SelectedItem = selected,
                    StatusText = "已选择 24 个候选动作",
                    CanGenerate = true,
                    CanPreview = true,
                };

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
                        RenderAndAssertBaseAnimationState(
                            state,
                            view =>
                            {
                                var grid = FindDescendants<DataGrid>(view)
                                    .Single();
                                NUnitAssert.Multiple(() =>
                                {
                                    NUnitAssert.That(
                                        grid.Items.Count,
                                        Is.EqualTo(24));
                                    NUnitAssert.That(
                                        grid.Columns,
                                        Has.Count.EqualTo(4));
                                    NUnitAssert.That(
                                        ScrollViewer
                                            .GetHorizontalScrollBarVisibility(
                                                grid),
                                        Is.EqualTo(
                                            ScrollBarVisibility.Disabled));
                                    NUnitAssert.That(
                                        grid.Columns.Sum(column =>
                                            column.ActualWidth),
                                        Is.LessThanOrEqualTo(
                                            grid.ActualWidth + 1));
                                });
                            },
                            $"dense-{theme}");
                    }
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    private static void RenderAndAssertBaseAnimationState(
        BaseAnimationViewState state,
        Action<AnimationWorkbenchBaseAnimationView> assert,
        string? captureName = null)
    {
        var view = new AnimationWorkbenchBaseAnimationView
        {
            DataContext = state,
        };
        var window = new Window
        {
            Width = 720,
            Height = 820,
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
            assert(view);
            SaveWindowForVisualReview(window, captureName);
        }
        finally
        {
            window.Close();
        }
    }

    private static void SaveWindowForVisualReview(
        Window window,
        string? captureName)
    {
        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory) ||
            string.IsNullOrWhiteSpace(captureName))
        {
            return;
        }

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

        Directory.CreateDirectory(outputDirectory);
        using var stream = File.Create(Path.Combine(
            outputDirectory,
            $"animation-workbench-{captureName}.png"));
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

    private static bool IsFullyVisible(
        FrameworkElement ancestor,
        FrameworkElement element)
    {
        if (!element.IsVisible ||
            element.ActualWidth <= 0 ||
            element.ActualHeight <= 0)
        {
            return false;
        }

        var bounds = element.TransformToAncestor(ancestor).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Left >= -0.5 &&
               bounds.Top >= -0.5 &&
               bounds.Right <= ancestor.ActualWidth + 0.5 &&
               bounds.Bottom <= ancestor.ActualHeight + 0.5;
    }

    private sealed class BaseAnimationViewState
    {
        public IReadOnlyList<BaseAnimationRowState> Items { get; init; } = [];
        public BaseAnimationRowState? SelectedItem { get; init; }
        public string DonorSummary { get; init; } = "已选择基础动画族";
        public string OutputFolder { get; init; } =
            @"animations\battle\external\base";
        public string OutputPrefix { get; init; } = "ext_";
        public string AnimationSetOutputPath { get; init; } =
            @"animations\database\battle\bin\ext_external_base.animpack";
        public AnimationWorkbenchBaseAnimationStyleMode StyleMode { get; init; } =
            AnimationWorkbenchBaseAnimationStyleMode.PreserveMotion;
        public double StyleWeight { get; init; } = 0.25;
        public bool IncludeRootMotion { get; init; }
        public bool OverwriteExisting { get; init; }
        public IReadOnlyList<AnimationWorkbenchBaseAnimationStyleOption>
            StyleOptions
        { get; } =
            [
                new(
                    AnimationWorkbenchBaseAnimationStyleMode.PreserveMotion,
                    "仅保留动态习惯"),
            ];
        public IReadOnlyList<AnimationWorkbenchBaseAnimationRoleOption>
            RoleOptions
        { get; } =
            [
                new(AnimationWorkbenchBaseAnimationRole.Death, "死亡"),
            ];
        public string StatusText { get; init; } = string.Empty;
        public bool CanGenerate { get; init; }
        public bool CanPreview { get; init; }
        public bool CanSave { get; init; }
        public bool IsBusy { get; init; }
        public bool IsProgressIndeterminate { get; init; }
        public long ProgressValue { get; init; }
        public long ProgressMaximum { get; init; } = 1;
        public string ProgressDetail { get; init; } = string.Empty;
        public ICommand? ActiveCancelCommand { get; init; }
    }

    private sealed class WorkbenchShellPreviewState
    {
        public bool IsWorkbenchEnabled => true;
        public bool CanBrowseAnimationB => true;
        public bool CanBrowsePreviewModel => true;
        public bool CanEdit => true;
        public bool CanSelectAnimationB => true;
        public bool CanSelectResult => true;
        public bool CanSave => true;
        public bool HasPreviewModel => true;
        public bool CanClearPreviewModel => true;
        public bool ShowPreviewModel { get; set; } = true;
        public bool ShowPreviewSkeleton { get; set; } = true;
        public string StatusText =>
            "预览模型已加载，现在可以直接观察动画在模型上的实际效果。";
        public string SaveUnavailableReason => "可以另存为新动画。";
        public IReadOnlyList<AnimationWorkbenchSourceItem> Sources { get; } =
        [
            new("A", "yangjian_idle.anim",
                "241 帧 · 4.017 秒 · yangjian_skeleton",
                true,
                @"test\yangjian_idle.anim"),
            new("B", "动画 B（可选）", string.Empty, false, string.Empty),
            new("模型", "yangjian.rigid_model_v2",
                "模型骨架 yangjian_skeleton · 动画骨架 yangjian_skeleton · 8 个网格",
                true,
                @"test\yangjian.rigid_model_v2"),
        ];
        public IReadOnlyList<string> BoneNames { get; } =
            ["root", "pelvis", "spine_01", "spine_02", "head"];
        public IReadOnlyList<string> Diagnostics { get; } = [];
    }

    private sealed class TrustedPreviewShellState
    {
        public TrustedPreviewShellState(
            bool showAnimationPicker = false,
            bool showPickers = true)
        {
            var candidates = new ObservableCollection<
                TrustedAnimationModelCandidate>
            {
                new(
                    PackFile.CreateFromBytes(
                        "project.rigid_model_v2",
                        [1]),
                    @"models\project.rigid_model_v2",
                    "test",
                    @"E:\mods\test",
                    TrustedAnimationModelSourceRole.FolderProject),
                new(
                    PackFile.CreateFromBytes(
                        "reference.wsmodel",
                        [1]),
                    @"variantmeshes\reference.wsmodel",
                    "reference.pack",
                    @"E:\packs\reference.pack",
                    TrustedAnimationModelSourceRole.ReferencePack),
                new(
                    PackFile.CreateFromBytes(
                        "ca.variantmeshdefinition",
                        [1]),
                    @"variantmeshes\ca.variantmeshdefinition",
                    "data.pack",
                    @"E:\games\data.pack",
                    TrustedAnimationModelSourceRole.CaPack),
            };
            ModelCandidatesView = CollectionViewSource.GetDefaultView(
                candidates);
            ModelCandidatesView.GroupDescriptions.Add(
                new PropertyGroupDescription(
                    nameof(TrustedAnimationModelCandidate.SourceGroup)));
            var animations = new ObservableCollection<
                TrustedAnimationCandidate>
            {
                new(
                    PackFile.CreateFromBytes("idle.anim", [1]),
                    "humanoid_idle",
                    @"animations\battle\humanoid_idle.anim",
                    "my_mod.pack",
                    @"E:\mods\my_mod.pack",
                    TrustedAnimationModelSourceRole.FolderProject,
                    8,
                    120,
                    4,
                    30),
                new(
                    PackFile.CreateFromBytes("attack.anim", [1]),
                    "humanoid_attack",
                    @"animations\battle\humanoid_attack.anim",
                    "data.pack",
                    @"E:\games\data.pack",
                    TrustedAnimationModelSourceRole.CaPack,
                    8,
                    72,
                    2.4,
                    30),
            };
            AnimationCandidatesView = CollectionViewSource.GetDefaultView(
                animations);
            IsModelPickerOpen = showPickers && !showAnimationPicker;
            IsAnimationPickerOpen = showPickers && showAnimationPicker;
            Player.IsEnabled.Value = true;
        }

        public TrustedAnimationPreviewResourceState Model { get; } = new(
            @"variantmeshes\wh_variantmodels\character.rigid_model_v2",
            "my_mod.pack",
            true,
            string.Empty);
        public TrustedAnimationPreviewResourceState Skeleton { get; } = new(
            @"animations\skeletons\humanoid01.anim",
            "data.pack",
            true,
            string.Empty);
        public TrustedAnimationPreviewResourceState Animation { get; } =
            TrustedAnimationPreviewResourceState.Empty;
        public string AnimationPathText => "未选择动画";
        public bool ShowModel { get; set; } = true;
        public bool ShowSkeleton { get; set; } = true;
        public bool IsReady => true;
        public bool HasModelDiagnostic => false;
        public bool HasSkeletonDiagnostic => false;
        public bool HasAnimationDiagnostic => false;
        public bool IsModelPickerOpen { get; }
        public bool IsAnimationPickerOpen { get; }
        public bool IsModelScanRunning => true;
        public bool IsAnimationScanRunning => true;
        public string ModelSearchText { get; set; } = string.Empty;
        public string AnimationSearchText { get; set; } = string.Empty;
        public string ModelScanStatus =>
            "正在后台扫描，已找到 3 个有效模型。结果可立即搜索和选择。";
        public ICollectionView ModelCandidatesView { get; }
        public ICollectionView AnimationCandidatesView { get; }
        public string AnimationScanStatus =>
            "正在后台扫描，已找到 2 个严格兼容动画。";
        public TrustedAnimationModelCandidate? SelectedModelCandidate
        {
            get;
            set;
        }
        public TrustedAnimationCandidate? SelectedAnimationCandidate
        {
            get;
            set;
        }
        public object? GameWorld => null;
        public AnimationPlayerViewModel Player { get; } = new();
        public bool HasAnimation => true;
        public bool IsPlaying => false;
        public double PlaybackMaximum => 4;
        public double CurrentTimeSeconds { get; set; }
        public string PlaybackSummary =>
            "只读 · 0.00/4.00 秒 · 第 0/120 帧 · 30.00 FPS";
        public ICommand? OpenModelPickerCommand => null;
        public ICommand? OpenAnimationPickerCommand => null;
        public ICommand? CloseModelPickerCommand => null;
        public ICommand? CloseAnimationPickerCommand => null;
        public ICommand? CancelModelScanCommand => null;
        public ICommand? CancelAnimationScanCommand => null;
        public ICommand? UseSelectedModelCommand => null;
        public ICommand? UseSelectedAnimationCommand => null;
        public ICommand? TogglePlaybackCommand => null;
        public ICommand? FocusModelCommand => null;
        public ICommand? ShowFrontCommand => null;
        public ICommand? ResetCameraCommand => null;
    }

    private sealed class ControllableAnimationDiscovery :
        ITrustedAnimationDiscovery
    {
        private readonly Channel<
            IReadOnlyList<TrustedAnimationCandidate>> _channel =
            Channel.CreateUnbounded<
                IReadOnlyList<TrustedAnimationCandidate>>();
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public ChannelWriter<IReadOnlyList<TrustedAnimationCandidate>>
            Writer => _channel.Writer;

        public async IAsyncEnumerable<
            IReadOnlyList<TrustedAnimationCandidate>> DiscoverAsync(
                TrustedAnimationSkeletonIdentity requiredSkeleton,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await foreach (var batch in _channel.Reader.ReadAllAsync())
                yield return batch;
        }
    }

    private sealed class BaseAnimationRowState
    {
        public bool IsSelected { get; init; }
        public AnimationWorkbenchBaseAnimationRole Role { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public string OutputPath { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public string DetailText { get; init; } = string.Empty;
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

    private static PackFile CreateRigidModelHeaderFile(
        string fileName,
        string skeletonName)
    {
        var header = new RmvFileHeader
        {
            _fileType = "RMV2"u8.ToArray(),
            Version = RmvVersionEnum.RMV2_V8,
            LodCount = 0,
            SkeletonName = skeletonName,
        };
        return PackFile.CreateFromBytes(
            fileName,
            ByteHelper.GetBytes(header));
    }

    private static Mock<IPackFileService> CreateTrustedPreviewPackService(
        PackFile model,
        PackFile skeleton,
        string skeletonName)
    {
        var modelOwner = new PackFileContainer("my_mod.pack")
        {
            Role = PackFileContainerRole.ProjectWorkspace,
        };
        modelOwner.FileList["models\\character.rigid_model_v2"] = model;
        var skeletonOwner = new PackFileContainer("data.pack")
        {
            IsCaPackFile = true,
        };
        skeletonOwner.FileList[
            $"animations\\skeletons\\{skeletonName}.anim"] = skeleton;
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(model, null))
            .Returns("models\\character.rigid_model_v2");
        packFileService.Setup(service => service.GetPackFileContainer(model))
            .Returns(modelOwner);
        packFileService.Setup(service => service.FindFile(
                $"animations\\skeletons\\{skeletonName}.anim",
                null))
            .Returns(skeleton);
        packFileService.Setup(service => service.GetFullPath(skeleton, null))
            .Returns($"animations\\skeletons\\{skeletonName}.anim");
        packFileService.Setup(service => service.GetPackFileContainer(skeleton))
            .Returns(skeletonOwner);
        packFileService.Setup(service => service.GetAllPackfileContainers())
            .Returns([skeletonOwner, modelOwner]);
        packFileService.Setup(service => service.GetFileEntriesSnapshot(
                It.IsAny<PackFileContainer>()))
            .Returns((PackFileContainer container) =>
                container.FileList.ToArray());
        return packFileService;
    }

    private static PackFile CreateSkeletonPackFile(
        string fileName,
        string skeletonName) =>
        PackFile.CreateFromBytes(
            fileName,
            AnimationFile.ConvertToBytes(CreateAnimationFile(skeletonName)));

    private static AnimationFile CreateAnimationFile(
        string skeletonName = "test_skeleton")
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                SkeletonName = skeletonName,
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
