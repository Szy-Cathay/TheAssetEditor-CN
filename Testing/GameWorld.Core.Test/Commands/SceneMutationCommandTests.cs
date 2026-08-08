using GameWorld.Core.Commands.Object;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Testing.GameWorld.Core.Commands
{
    [TestFixture]
    public class SceneMutationCommandTests
    {
        [Test]
        public void DeleteObjectsCommand_WithSceneNodes_RemovesAndRestoresEveryNode()
        {
            var selectionManager = CreateSelectionManager();
            var parentA = new GroupNode("Parent A");
            var parentB = new GroupNode("Parent B");
            var childA = parentA.AddObject(new GroupNode("Child A"));
            var childB = parentB.AddObject(new GroupNode("Child B"));
            var command = new DeleteObjectsCommand(selectionManager);
            command.Configure(new List<ISceneNode> { childA, childB });

            command.Execute();

            Assert.Multiple(() =>
            {
                Assert.That(parentA.Children, Does.Not.Contain(childA));
                Assert.That(parentB.Children, Does.Not.Contain(childB));
            });

            command.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(parentA.Children, Does.Contain(childA));
                Assert.That(parentB.Children, Does.Contain(childB));
            });
        }

        [Test]
        public void DeleteObjectsCommand_UndoRestoresOriginalSiblingOrder()
        {
            var selectionManager = CreateSelectionManager();
            var parent = new GroupNode("Parent");
            var first = parent.AddObject(new GroupNode("First"));
            var second = parent.AddObject(new GroupNode("Second"));
            var third = parent.AddObject(new GroupNode("Third"));
            var command = new DeleteObjectsCommand(selectionManager);
            command.Configure(new List<ISceneNode> { second });

            command.Execute();
            command.Undo();

            Assert.That(parent.Children, Is.EqualTo(new ISceneNode[] { first, second, third }));
        }

        [Test]
        public void SortSceneNodesCommand_SortsRecursivelyAndRestoresOriginalOrder()
        {
            var root = new GroupNode("Root");
            var group = root.AddObject(new GroupNode("Group"));
            var rootZ = root.AddObject(new GroupNode("Z"));
            var rootA = root.AddObject(new GroupNode("A"));
            var groupZ = group.AddObject(new GroupNode("Z"));
            var groupA = group.AddObject(new GroupNode("A"));
            var command = new SortSceneNodesCommand();
            command.Configure(root);

            command.Execute();

            Assert.Multiple(() =>
            {
                Assert.That(root.Children, Is.EqualTo(new[] { rootA, group, rootZ }));
                Assert.That(group.Children, Is.EqualTo(new[] { groupA, groupZ }));
            });

            command.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(root.Children, Is.EqualTo(new[] { group, rootZ, rootA }));
                Assert.That(group.Children, Is.EqualTo(new[] { groupZ, groupA }));
            });
        }

        [Test]
        public void MakeNodeEditableCommand_MovesMeshAndRestoresItsOriginalState()
        {
            var mainNode = new Rmv2ModelNode("Editable Model");
            var editableLod = mainNode.AddObject(new Rmv2LodNode("Lod 0", 0));
            var sourceModel = new Rmv2ModelNode("Source");
            var sourceLod = sourceModel.AddObject(new Rmv2LodNode("Lod 0", 0));
            var sourceMesh = sourceLod.AddObject(CreateMeshNode("Mesh"));
            sourceMesh.IsEditable = false;
            sourceMesh.IsSelectable = false;
            var command = new MakeNodeEditableCommand();
            command.Configure(mainNode, sourceMesh);

            command.Execute();

            Assert.Multiple(() =>
            {
                Assert.That(editableLod.Children, Does.Contain(sourceMesh));
                Assert.That(sourceLod.Children, Does.Not.Contain(sourceMesh));
                Assert.That(sourceMesh.Parent, Is.SameAs(editableLod));
                Assert.That(sourceMesh.IsEditable, Is.True);
                Assert.That(sourceMesh.IsSelectable, Is.True);
            });

            command.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(sourceLod.Children, Does.Contain(sourceMesh));
                Assert.That(editableLod.Children, Does.Not.Contain(sourceMesh));
                Assert.That(sourceMesh.Parent, Is.SameAs(sourceLod));
                Assert.That(sourceMesh.IsEditable, Is.False);
                Assert.That(sourceMesh.IsSelectable, Is.False);
            });
        }

        [Test]
        public void MakeNodeEditableCommand_RedoReaddsAnEditableLodCreatedByTheCommand()
        {
            var mainNode = new Rmv2ModelNode("Editable Model");
            var sourceLod = new Rmv2LodNode("Source Lod", 0);
            var sourceMesh = sourceLod.AddObject(CreateMeshNode("Mesh"));
            var command = new MakeNodeEditableCommand();
            command.Configure(mainNode, sourceMesh);

            command.Execute();
            command.Undo();
            command.Execute();

            Assert.Multiple(() =>
            {
                Assert.That(mainNode.GetLodNodes(), Has.Count.EqualTo(1));
                Assert.That(mainNode.GetLodNodes()[0].Children, Does.Contain(sourceMesh));
            });
        }

        [Test]
        public void CreateStaticMeshFromAnimationCommand_AddsClonedGroupAndUndoRemovesIt()
        {
            var parent = new GroupNode("Parent");
            var sourceMesh = CreateMeshNode("Mesh");
            var command = new CreateStaticMeshFromAnimationCommand();
            command.Configure(parent, [sourceMesh], new AnimationFrame());

            command.Execute();

            var createdGroup = parent.Children.Single();
            var createdMesh = createdGroup.Children.Single();
            Assert.Multiple(() =>
            {
                Assert.That(createdGroup.Name, Is.EqualTo("staticMesh"));
                Assert.That(createdMesh, Is.TypeOf<Rmv2MeshNode>());
                Assert.That(createdMesh, Is.Not.SameAs(sourceMesh));
                Assert.That(((Rmv2MeshNode)createdMesh).Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Static));
            });

            command.Undo();

            Assert.That(parent.Children, Is.Empty);
        }

        static Rmv2MeshNode CreateMeshNode(string name)
        {
            var graphicsContext = new Mock<IGraphicsCardGeometry>();
            graphicsContext.Setup(x => x.Clone()).Returns(graphicsContext.Object);
            var geometry = new MeshObject(graphicsContext.Object, string.Empty)
            {
                VertexArray = [],
                IndexArray = []
            };
            geometry.ChangeVertexType(UiVertexFormat.Cinematic);
            var material = new Mock<IRmvMaterial>();
            material.SetupGet(x => x.ModelName).Returns(name);
            material.Setup(x => x.Clone()).Returns(material.Object);
            return new Rmv2MeshNode(geometry, material.Object, new CloneableCapabilityMaterial(), null!);
        }

        private static SelectionManager CreateSelectionManager()
        {
            var selectionManager = new SelectionManager(
                Mock.Of<IEventHub>());
            selectionManager.CreateSelectionSate(
                GeometrySelectionMode.Object,
                null!,
                false);
            return selectionManager;
        }

        sealed class CloneableCapabilityMaterial : CapabilityMaterial
        {
            public CloneableCapabilityMaterial()
                : base(CapabilityMaterialsEnum.SpecGlossPbr_Default, ShaderTypes.Pbr_SpecGloss, null!)
            {
            }

            protected override CapabilityMaterial CreateCloneInstance()
            {
                return new CloneableCapabilityMaterial();
            }
        }
    }
}
