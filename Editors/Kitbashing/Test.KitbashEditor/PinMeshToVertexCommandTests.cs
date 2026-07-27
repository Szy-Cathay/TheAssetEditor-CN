using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class PinMeshToVertexCommandTests
    {
        [Test]
        public void Undo_RestoresOriginalGeometryAndPivot()
        {
            var selectionManager = new SelectionManager(Mock.Of<IEventHub>(), null!, null!, null!);
            selectionManager.CreateSelectionSate(GeometrySelectionMode.Object, null!, false);
            var source = CreateMesh(UiVertexFormat.Cinematic);
            source.Geometry.VertexArray[0].BlendIndices = new Vector4(4, 3, 2, 1);
            source.Geometry.VertexArray[0].BlendWeights = new Vector4(0.4f, 0.3f, 0.2f, 0.1f);
            var target = CreateMesh(UiVertexFormat.Static);
            var originalGeometry = target.Geometry;
            var originalPivot = new Vector3(2, 3, 4);
            target.PivotPoint = originalPivot;
            var command = new PinMeshToVertexCommand(selectionManager);
            command.Configure([target], source, 0);

            command.Execute();
            command.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(target.Geometry, Is.SameAs(originalGeometry));
                Assert.That(target.PivotPoint, Is.EqualTo(originalPivot));
            });
        }

        static Rmv2MeshNode CreateMesh(UiVertexFormat vertexFormat)
        {
            var graphicsContext = new Mock<IGraphicsCardGeometry>();
            graphicsContext.Setup(x => x.Clone()).Returns(graphicsContext.Object);
            var geometry = new MeshObject(graphicsContext.Object, "skeleton")
            {
                VertexArray =
                [
                    new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(0, 0, 0, 1),
                        BlendWeights = new Vector4(1, 0, 0, 0)
                    }
                ],
                IndexArray = []
            };
            geometry.ChangeVertexType(vertexFormat, false);
            var material = new Mock<IRmvMaterial>();
            material.SetupGet(x => x.ModelName).Returns("Mesh");
            return new Rmv2MeshNode(geometry, material.Object, null!, null!);
        }
    }
}
