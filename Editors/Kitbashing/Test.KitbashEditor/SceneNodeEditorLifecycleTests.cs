using Editors.KitbasherEditor.Events;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.SceneNodes;
using Moq;
using Shared.Core.Events;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class SceneNodeEditorLifecycleTests
    {
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
                factory.Object);

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
                factory.Object);

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
