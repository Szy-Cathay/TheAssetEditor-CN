using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Tint;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.RigidModel.Types;
using Shared.Ui.BaseDialogs.MathViews;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class SceneNodeEditorPropertyTests
    {
        [Test]
        public void Update_TracksDocumentStateAndSupportsUndoRedo()
        {
            var commandExecutor = new CommandExecutor(Mock.Of<IEventHub>());
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var value = "before";

            propertyEditor.Update(value, "after", newValue => value = newValue);

            Assert.Multiple(() =>
            {
                Assert.That(value, Is.EqualTo("after"));
                Assert.That(commandExecutor.CurrentDocumentStateId, Is.Not.Zero);
                Assert.That(commandExecutor.CanUndo(), Is.True);
            });

            commandExecutor.Undo();
            Assert.That(value, Is.EqualTo("before"));

            commandExecutor.Redo();
            Assert.That(value, Is.EqualTo("after"));
        }

        [Test]
        public void Update_WithUnchangedValue_DoesNotCreateCommand()
        {
            var commandExecutor = new CommandExecutor(Mock.Of<IEventHub>());
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var value = "same";

            propertyEditor.Update(value, value, newValue => value = newValue);

            Assert.Multiple(() =>
            {
                Assert.That(value, Is.EqualTo("same"));
                Assert.That(commandExecutor.CurrentDocumentStateId, Is.Zero);
                Assert.That(commandExecutor.CanUndo(), Is.False);
            });
        }

        [Test]
        public void GroupName_InitializesWithoutMutationAndTracksUserEdit()
        {
            var commandExecutor = new CommandExecutor(Mock.Of<IEventHub>());
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var node = new GroupNode("Original");
            var viewModel = new GroupNodeViewModel(propertyEditor);

            viewModel.Initialize(node);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.GroupName, Is.EqualTo("Original"));
                Assert.That(commandExecutor.CurrentDocumentStateId, Is.Zero);
            });

            viewModel.GroupName = "Changed";

            Assert.Multiple(() =>
            {
                Assert.That(node.Name, Is.EqualTo("Changed"));
                Assert.That(commandExecutor.CanUndo(), Is.True);
            });

            commandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(node.Name, Is.EqualTo("Original"));
                Assert.That(viewModel.GroupName, Is.EqualTo("Original"));
            });
        }

        [Test]
        public void SkeletonScale_LoadsActualValueAndTracksUserEdit()
        {
            var commandExecutor = new CommandExecutor(Mock.Of<IEventHub>());
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var node = new SkeletonNode(null)
            {
                SkeletonScale = 2.5f
            };
            var viewModel = new SkeletonSceneNodeViewModel(propertyEditor);

            viewModel.Initialize(node);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.BoneScale, Is.EqualTo(2.5f));
                Assert.That(commandExecutor.CurrentDocumentStateId, Is.Zero);
            });

            viewModel.BoneScale = 3.5f;

            Assert.Multiple(() =>
            {
                Assert.That(node.SkeletonScale, Is.EqualTo(3.5f));
                Assert.That(commandExecutor.CanUndo(), Is.True);
            });

            commandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(node.SkeletonScale, Is.EqualTo(2.5f));
                Assert.That(viewModel.BoneScale, Is.EqualTo(2.5f));
            });
        }

        [Test]
        public void AnimationInitialize_WithMissingSkeleton_PreservesAttachmentData()
        {
            var commandExecutor = new CommandExecutor(Mock.Of<IEventHub>());
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            var rootScene = new KitbasherRootScene(
                new AnimationsContainerComponent(),
                Mock.Of<IPackFileService>(),
                Mock.Of<IEventHub>());
            var meshNode = CreateMeshNode();
            meshNode.AttachmentPointName = "unresolved_bone";
            var modelNode = new Rmv2ModelNode("Model");
            var lodNode = modelNode.AddObject(new Rmv2LodNode("Lod 0", 0));
            lodNode.AddObject(meshNode);
            var viewModel = new AnimationViewModel(
                rootScene,
                lookup.Object,
                propertyEditor);

            viewModel.Initialize(meshNode);

            lookup.Verify(
                item => item.GetSkeletonFileFromName("missing_skeleton"),
                Times.Once);
            Assert.Multiple(() =>
            {
                Assert.That(meshNode.AttachmentPointName, Is.EqualTo("unresolved_bone"));
                Assert.That(viewModel.AnimatedBones, Is.Empty);
                Assert.That(commandExecutor.CurrentDocumentStateId, Is.Zero);
            });
        }

        [Test]
        public void MeshName_TracksDocumentStateAndUndoUpdatesSidebar()
        {
            var eventHub = Mock.Of<IEventHub>();
            var commandExecutor = new CommandExecutor(eventHub);
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var rootScene = new KitbasherRootScene(
                new AnimationsContainerComponent(),
                Mock.Of<IPackFileService>(),
                eventHub);
            var sceneManager = new SceneManager(null!, null!, eventHub);
            var meshNode = CreateMeshNode();
            var viewModel = new MeshViewModel(
                rootScene,
                sceneManager,
                propertyEditor);
            viewModel.Initialize(meshNode);

            viewModel.ModelName = "Renamed";

            Assert.Multiple(() =>
            {
                Assert.That(meshNode.Name, Is.EqualTo("Renamed"));
                Assert.That(commandExecutor.CanUndo(), Is.True);
            });

            commandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(meshNode.Name, Is.EqualTo("Mesh"));
                Assert.That(viewModel.ModelName, Is.EqualTo("Mesh"));
            });
        }

        [Test]
        public void FactionPreview_UpdatesSceneColourWithoutDirtyingDocument()
        {
            var commandExecutor = new CommandExecutor(Mock.Of<IEventHub>());
            var sceneParameters = new SceneRenderParametersStore();
            var capability = new TintCapability();
            var viewModel = new TintViewModel(capability, sceneParameters);

            viewModel.FactionColour0.PickedColor =
                System.Windows.Media.Color.FromRgb(64, 128, 255);
            viewModel.FactionColour0.OnHandleColourChanged();

            Assert.Multiple(() =>
            {
                Assert.That(
                    sceneParameters.FactionColour0.X,
                    Is.EqualTo(64f / 256f));
                Assert.That(
                    sceneParameters.FactionColour0.Y,
                    Is.EqualTo(128f / 256f));
                Assert.That(
                    sceneParameters.FactionColour0.Z,
                    Is.EqualTo(255f / 256f));
                Assert.That(commandExecutor.CanUndo(), Is.False);
            });

            viewModel.ApplyFactionColours = false;

            Assert.Multiple(() =>
            {
                Assert.That(capability.ApplyCapability, Is.False);
                Assert.That(commandExecutor.CanUndo(), Is.False);
            });
        }

        [Test]
        public void ResetAttachmentPoints_TracksDocumentStateAndSupportsUndo()
        {
            var eventHub = Mock.Of<IEventHub>();
            var commandExecutor = new CommandExecutor(eventHub);
            var propertyEditor = new SceneNodePropertyEditor(commandExecutor);
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup
                .Setup(item => item.GetAllSkeletonFileNames())
                .Returns([]);
            var rootScene = new KitbasherRootScene(
                new AnimationsContainerComponent(),
                Mock.Of<IPackFileService>(),
                eventHub);
            var mainNode = new MainEditableNode(
                SpecialNodes.EditableModel,
                new SkeletonNode(null),
                Mock.Of<IPackFileService>());
            mainNode.SetAttachmentPoints(
                [
                    new RmvAttachmentPoint
                    {
                        BoneIndex = 3,
                        Name = "original",
                        Matrix = RmvMatrix3x4.Identity()
                    }
                ],
                false);
            var viewModel = new MainEditableNodeViewModel(
                rootScene,
                lookup.Object,
                null!,
                Mock.Of<IUiCommandFactory>(),
                Mock.Of<IStandardDialogs>(),
                propertyEditor);
            viewModel.Initialize(mainNode);

            viewModel.ResetAttachmentPointListCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(mainNode.AttachmentPoints, Is.Empty);
                Assert.That(commandExecutor.CanUndo(), Is.True);
            });

            commandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(mainNode.AttachmentPoints, Has.Count.EqualTo(1));
                Assert.That(mainNode.AttachmentPoints[0].Name, Is.EqualTo("original"));
                Assert.That(viewModel.AttachmentPointList, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void Vector4Set_UsesFourthComponent()
        {
            var viewModel = new Vector4ViewModel(0, 0, 0, 0);

            viewModel.Set(1, 2, 3, 4);

            Assert.That(viewModel.W.Value, Is.EqualTo(4));
        }

        private static Rmv2MeshNode CreateMeshNode()
        {
            var graphicsContext = new Mock<IGraphicsCardGeometry>();
            graphicsContext.Setup(context => context.Clone()).Returns(graphicsContext.Object);
            var geometry = new MeshObject(graphicsContext.Object, "missing_skeleton")
            {
                VertexArray = [],
                IndexArray = []
            };
            geometry.ChangeVertexType(UiVertexFormat.Weighted);

            return new Rmv2MeshNode(
                geometry,
                new WeightedMaterial
                {
                    ModelName = "Mesh"
                },
                null!,
                null!);
        }
    }
}
