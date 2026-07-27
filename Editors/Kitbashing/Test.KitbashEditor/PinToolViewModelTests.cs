using Editors.KitbasherEditor.ChildEditors.PinTool;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Moq;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class PinToolViewModelTests
    {
        [Test]
        public void AddSelectionToAffectedMeshes_EmptySelection_ShowsError()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var selectionManager = CreateSelectionManager(new ObjectSelectionState());
            var viewModel = CreateViewModel(selectionManager, dialogs.Object);

            viewModel.AddSelectionToAffectMeshCollectionCommand.Execute(null);

            Assert.That(viewModel.AffectedMeshCollection, Is.Empty);
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void AddSelectionToAffectedMeshes_NonMeshSelection_ShowsError()
        {
            var selectionState = new ObjectSelectionState();
            selectionState.ModifySelectionSingleObject(new TestSelectableNode(), false);
            var dialogs = new Mock<IStandardDialogs>();
            var selectionManager = CreateSelectionManager(selectionState);
            var viewModel = CreateViewModel(selectionManager, dialogs.Object);

            viewModel.AddSelectionToAffectMeshCollectionCommand.Execute(null);

            Assert.That(viewModel.AffectedMeshCollection, Is.Empty);
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        static SelectionManager CreateSelectionManager(ObjectSelectionState state)
        {
            var selectionManager = new SelectionManager(Mock.Of<IEventHub>(), null!, null!, null!);
            selectionManager.SetState(state);
            return selectionManager;
        }

        static PinToolViewModel CreateViewModel(
            SelectionManager selectionManager,
            IStandardDialogs dialogs)
        {
            return new PinToolViewModel(
                selectionManager,
                new CommandFactory(null!, null!),
                dialogs);
        }

        sealed class TestSelectableNode : SceneNode, ISelectable
        {
            public MeshObject Geometry { get; set; } = null!;
            public bool IsSelectable { get; set; } = true;

            public override ISceneNode CreateCopyInstance() => new TestSelectableNode();
        }
    }
}
