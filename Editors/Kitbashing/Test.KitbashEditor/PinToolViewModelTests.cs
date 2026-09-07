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
        [OneTimeSetUp]
        public void LoadLocalization() => new LocalizationManager().LoadLanguage();

        [Test]
        public void StaticPinSource_ShowsAnErrorAndCannotBeApplied()
        {
            var source = CreateRiggedMesh();
            source.Geometry.ChangeVertexType(Shared.GameFormats.RigidModel.UiVertexFormat.Static, false);
            var selection = CreateSelectionManager(new ObjectSelectionState());
            var state = new VertexSelectionState(source, 0);
            state.ModifySelection([0], false);
            selection.SetState(state);
            var dialogs = new Mock<IStandardDialogs>();
            using var model = CreateViewModel(selection, dialogs.Object);
            model.AffectedMeshCollection.Add(CreateRiggedMesh());
            model.PinMode.SetSelectionCommand.Execute(null);
            Assert.That(model.PinMode.SelectedMesh, Is.Null);
            Assert.That(model.CanApply, Is.False);
            dialogs.Verify(x => x.ShowDialogBox(LocalizationManager.Instance.Get("Msg.Kitbash.PinSourceMustBeAnimated"),
                It.IsAny<string>()), Times.Once);
            model.PinMode.SelectedMesh = source;
            model.PinMode.SelectedVertex = [0];
            Assert.That(model.CanApply, Is.False);
        }

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

        [Test]
        public void CanApply_FollowsTargetsSourceAndRiggingMode()
        {
            using var viewModel = CreateViewModel(CreateSelectionManager(new ObjectSelectionState()), Mock.Of<IStandardDialogs>());
            var target = new Rmv2MeshNode(null!, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!);
            var source = CreateRiggedMesh();
            Assert.That(viewModel.CanApply, Is.False);
            Assert.That(viewModel.ClearAffectedMeshCollectionCommand.CanExecute(null), Is.False);

            viewModel.AffectedMeshCollection.Add(target);
            Assert.That(viewModel.CanApply, Is.False);
            Assert.That(viewModel.ClearAffectedMeshCollectionCommand.CanExecute(null), Is.True);
            viewModel.PinMode.SelectedMesh = source;
            Assert.That(viewModel.CanApply, Is.False);
            viewModel.PinMode.SelectedVertex = [0];
            Assert.That(viewModel.CanApply, Is.True);

            viewModel.SelectedRiggingMode = RiggingMode.SkinWrap;
            Assert.That(viewModel.CanApply, Is.False);
            viewModel.SkinWrapMode.TakeAnimationFromMesh = source;
            Assert.That(viewModel.CanApply, Is.True);
            viewModel.ClearAffectedMeshCollectionCommand.Execute(null);
            Assert.That(viewModel.CanApply, Is.False);
            Assert.That(viewModel.ClearAffectedMeshCollectionCommand.CanExecute(null), Is.False);
        }

        [TestCase(RiggingMode.Pin)]
        [TestCase(RiggingMode.SkinWrap)]
        public void CanApply_SourceCannotAlsoBeATarget(RiggingMode mode)
        {
            using var viewModel = CreateViewModel(CreateSelectionManager(new ObjectSelectionState()), Mock.Of<IStandardDialogs>());
            var source = new Rmv2MeshNode(null!, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!);
            viewModel.SelectedRiggingMode = mode;
            viewModel.PinMode.SelectedMesh = source;
            viewModel.PinMode.SelectedVertex = [0];
            viewModel.SkinWrapMode.TakeAnimationFromMesh = source;
            viewModel.AffectedMeshCollection.Add(source);
            Assert.That(viewModel.CanApply, Is.False);
        }

        [Test]
        public void InvalidSourceSelection_ClearsTheDisplayedSourceAndDisablesApply()
        {
            using var viewModel = CreateViewModel(CreateSelectionManager(new ObjectSelectionState()), Mock.Of<IStandardDialogs>());
            viewModel.AffectedMeshCollection.Add(new Rmv2MeshNode(null!, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!));
            viewModel.PinMode.SelectedMesh = new Rmv2MeshNode(null!, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!);
            viewModel.PinMode.SelectedVertex = [0];
            viewModel.PinMode.Description = "previous source";
            var changes = new List<string?>();
            viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

            viewModel.PinMode.SetSelectionCommand.Execute(null);

            Assert.That(viewModel.PinMode.Description, Is.Empty);
            Assert.That(viewModel.CanApply, Is.False);
            Assert.That(changes, Does.Contain(nameof(viewModel.CanApply)));
        }

        static SelectionManager CreateSelectionManager(ObjectSelectionState state)
        {
            var selectionManager = new SelectionManager(Mock.Of<IEventHub>());
            selectionManager.SetState(state);
            return selectionManager;
        }

        static Rmv2MeshNode CreateRiggedMesh()
        {
            var geometry = new MeshObject(Mock.Of<IGraphicsCardGeometry>(), "skeleton")
            {
                VertexArray = [new GameWorld.Core.Rendering.VertexPositionNormalTextureCustom
                {
                    Position = new Microsoft.Xna.Framework.Vector4(0, 0, 0, 1),
                    BlendWeights = new Microsoft.Xna.Framework.Vector4(1, 0, 0, 0)
                }],
                IndexArray = []
            };
            geometry.ChangeVertexType(Shared.GameFormats.RigidModel.UiVertexFormat.Cinematic, false);
            return new Rmv2MeshNode(geometry, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!);
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
