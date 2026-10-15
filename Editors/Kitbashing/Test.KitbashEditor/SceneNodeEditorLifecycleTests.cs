using Editors.KitbasherEditor.Events;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Components;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Test.KitbashEditor
{
    [TestFixture]
    [NonParallelizable]
    public class SceneNodeEditorLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void HeaderRename_TracksOneCommandAndUndoTargetsOriginalNode(bool mesh)
        {
            Action<SceneNodeSelectedEvent>? select = null;
            var events = new Mock<IEventHub>();
            events.Setup(hub => hub.Register(It.IsAny<object>(), It.IsAny<Action<SceneNodeSelectedEvent>>()))
                .Callback<object, Action<SceneNodeSelectedEvent>>((_, callback) => select = callback);
            var commands = new CommandExecutor(events.Object);
            var factory = new Mock<ISceneNodeEditorFactory>();
            factory.Setup(item => item.Create(It.IsAny<ISceneNode>())).Returns(Mock.Of<ISceneNodeEditor>());
            using var viewModel = new SceneNodeEditorViewModel(events.Object, factory.Object, new SceneNodePropertyEditor(commands));
            ISceneNode first = mesh
                ? new Rmv2MeshNode(null!, new WeightedMaterial { ModelName = "First" }, null!, null!)
                : new GroupNode("First");
            var second = new GroupNode("Second");

            select!(new SceneNodeSelectedEvent([first]));
            Assert.That(viewModel.CanRenameSelectedNode, Is.True);
            Assert.That(commands.CurrentDocumentStateId, Is.Zero, "Selection and initialization must not dirty the document.");
            viewModel.SelectedNodeName = "Changed";
            Assert.That(first.Name, Is.EqualTo("Changed"));
            commands.Undo();
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("First"));
            Assert.That(commands.CanUndo(), Is.False, "A rename must produce exactly one command.");
            commands.Redo();
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("Changed"));

            select(new SceneNodeSelectedEvent([second]));
            commands.Undo();
            Assert.That(first.Name, Is.EqualTo("First"));
            Assert.That(second.Name, Is.EqualTo("Second"));
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("Second"));
            var state = commands.CurrentDocumentStateId;
            second.Name = "External rename";
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("External rename"));
            Assert.That(commands.CurrentDocumentStateId, Is.EqualTo(state));
        }

        [Test]
        public void HeaderRename_DoesNotMutateEmptyMultipleSystemOrUnsupportedSelections()
        {
            Action<SceneNodeSelectedEvent>? select = null;
            var events = new Mock<IEventHub>();
            events.Setup(hub => hub.Register(It.IsAny<object>(), It.IsAny<Action<SceneNodeSelectedEvent>>()))
                .Callback<object, Action<SceneNodeSelectedEvent>>((_, callback) => select = callback);
            var commands = new CommandExecutor(events.Object);
            var factory = new Mock<ISceneNodeEditorFactory>();
            var skeleton = new SkeletonNode(null);
            factory.Setup(item => item.Create(skeleton)).Returns(Mock.Of<ISceneNodeEditor>());
            var main = new MainEditableNode(SpecialNodes.EditableModel, skeleton, null!);
            factory.Setup(item => item.Create(main)).Returns(Mock.Of<ISceneNodeEditor>());
            using var viewModel = new SceneNodeEditorViewModel(events.Object, factory.Object, new SceneNodePropertyEditor(commands));
            var first = new GroupNode("First");
            var second = new GroupNode("Second");
            foreach (var selection in new List<ISceneNode>[] { [], [first, second], [first], [skeleton], [main] })
            {
                select!(new SceneNodeSelectedEvent(selection));
                var original = viewModel.SelectedNodeName;
                Assert.That(viewModel.IsSelectedNodeNameReadOnly, Is.True);
                viewModel.SelectedNodeName = "Must not rename";
                Assert.That(viewModel.SelectedNodeName, Is.EqualTo(original));
                Assert.That(commands.CurrentDocumentStateId, Is.Zero);
            }
            Assert.That(first.Name, Is.EqualTo("First"));
            Assert.That(second.Name, Is.EqualTo("Second"));
        }

        [Test]
        public void MultipleAndEmptySelections_RefreshTheHintWithoutAnActiveEditor()
        {
            new LocalizationManager().LoadLanguage();
            Action<SceneNodeSelectedEvent>? selectionChanged = null;
            var eventHub = new Mock<IEventHub>();
            eventHub.Setup(hub => hub.Register(
                    It.IsAny<object>(), It.IsAny<Action<SceneNodeSelectedEvent>>()))
                .Callback<object, Action<SceneNodeSelectedEvent>>((_, callback) => selectionChanged = callback);
            var factory = new Mock<ISceneNodeEditorFactory>();
            using var viewModel = new SceneNodeEditorViewModel(eventHub.Object, factory.Object,
                new SceneNodePropertyEditor(new CommandExecutor(eventHub.Object)));
            var nodes = new List<ISceneNode> { new GroupNode("A"), new GroupNode("B"), new GroupNode("C") };

            Assert.Multiple(() =>
            {
                selectionChanged!(new SceneNodeSelectedEvent(nodes.Take(2).ToList()));
                Assert.That(viewModel.EmptyStateText, Is.EqualTo(
                    LocalizationManager.Instance.GetFormat("Kitbash.Sidebar.MultipleSelected", 2)));
                selectionChanged(new SceneNodeSelectedEvent(nodes));
                Assert.That(viewModel.EmptyStateText, Is.EqualTo(
                    LocalizationManager.Instance.GetFormat("Kitbash.Sidebar.MultipleSelected", 3)));
                selectionChanged(new SceneNodeSelectedEvent([]));
                Assert.That(viewModel.EmptyStateText, Is.EqualTo(
                    LocalizationManager.Instance.Get("Kitbash.Sidebar.SelectNode")));
                Assert.That(viewModel.CurrentEditor, Is.Null);
            });
            factory.Verify(item => item.Create(It.IsAny<ISceneNode>()), Times.Never);
        }

        [Test]
        public void SelectedName_FollowsRenamesAndDetachesFromPreviousNodes()
        {
            Action<SceneNodeSelectedEvent>? selectionChanged = null;
            var eventHub = new Mock<IEventHub>();
            eventHub.Setup(hub => hub.Register(
                    It.IsAny<object>(), It.IsAny<Action<SceneNodeSelectedEvent>>()))
                .Callback<object, Action<SceneNodeSelectedEvent>>((_, callback) => selectionChanged = callback);
            var factory = new Mock<ISceneNodeEditorFactory>();
            var viewModel = new SceneNodeEditorViewModel(eventHub.Object, factory.Object,
                new SceneNodePropertyEditor(new CommandExecutor(eventHub.Object)));
            var first = new GroupNode("First");
            var second = new GroupNode("Second");

            selectionChanged!(new SceneNodeSelectedEvent([first]));
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("First"));
            first.Name = "Renamed";
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("Renamed"));

            selectionChanged(new SceneNodeSelectedEvent([second]));
            first.Name = "No longer selected";
            Assert.That(viewModel.SelectedNodeName, Is.EqualTo("Second"));
            selectionChanged(new SceneNodeSelectedEvent([first, second]));
            second.Name = "Not selected either";
            Assert.That(viewModel.SelectedNodeName, Is.Empty);

            selectionChanged(new SceneNodeSelectedEvent([second]));
            viewModel.Dispose();
            second.Name = "After disposal";
            Assert.That(viewModel.SelectedNodeName, Is.Empty);
        }

        [Test]
        public void RepeatedSelectionOfSameNode_ReusesCurrentEditor()
        {
            Action<SceneNodeSelectedEvent>? selectionChanged = null;
            var eventHub = new Mock<IEventHub>();
            eventHub
                .Setup(hub => hub.Register(
                    It.IsAny<object>(),
                    It.IsAny<Action<SceneNodeSelectedEvent>>()))
                .Callback<object, Action<SceneNodeSelectedEvent>>(
                    (_, callback) => selectionChanged = callback);
            var factory = new Mock<ISceneNodeEditorFactory>();
            var editor = new Mock<ISceneNodeEditor>();
            var node = new GroupNode("Group");
            factory.Setup(item => item.Create(node)).Returns(editor.Object);
            var viewModel = new SceneNodeEditorViewModel(
                eventHub.Object,
                factory.Object,
                new SceneNodePropertyEditor(new CommandExecutor(eventHub.Object)));

            selectionChanged!(new SceneNodeSelectedEvent([node]));
            selectionChanged(new SceneNodeSelectedEvent([node]));

            factory.Verify(item => item.Create(node), Times.Once);
            editor.Verify(item => item.Dispose(), Times.Never);
            Assert.That(viewModel.CurrentEditor, Is.SameAs(editor.Object));
        }

        [Test]
        public void EmptySelection_DisposesCurrentEditorAndShowsEmptyState()
        {
            Action<SceneNodeSelectedEvent>? selectionChanged = null;
            var eventHub = new Mock<IEventHub>();
            eventHub
                .Setup(hub => hub.Register(
                    It.IsAny<object>(),
                    It.IsAny<Action<SceneNodeSelectedEvent>>()))
                .Callback<object, Action<SceneNodeSelectedEvent>>(
                    (_, callback) => selectionChanged = callback);
            var factory = new Mock<ISceneNodeEditorFactory>();
            var editor = new Mock<ISceneNodeEditor>();
            var node = new GroupNode("Group");
            factory.Setup(item => item.Create(node)).Returns(editor.Object);
            var viewModel = new SceneNodeEditorViewModel(
                eventHub.Object,
                factory.Object,
                new SceneNodePropertyEditor(new CommandExecutor(eventHub.Object)));

            selectionChanged!(new SceneNodeSelectedEvent([node]));
            selectionChanged(new SceneNodeSelectedEvent([]));

            editor.Verify(item => item.Dispose(), Times.Once);
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.CurrentEditor, Is.Null);
                Assert.That(viewModel.EmptyStateText, Is.Not.Empty);
            });
        }
    }
}
