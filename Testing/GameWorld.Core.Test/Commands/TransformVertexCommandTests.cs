using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;

namespace Testing.GameWorld.Core.Commands
{
    [TestFixture]
    public class TransformVertexCommandTests
    {
        const float Epsilon = 0.0001f;

        static readonly Vector3 Pivot = new(0.35f, -0.2f, 0.45f);
        static readonly Matrix ForwardTransform =
            Matrix.CreateScale(1.2f, 0.75f, 1.4f) *
            Matrix.CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(0.4f, -0.25f, 0.15f)) *
            Matrix.CreateTranslation(0.6f, -0.3f, 0.2f);

        [Test]
        public void ObjectTransform_UndoRestoresInitialVertices_AndRedoRestoresPreview()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();

            RunRoundTrip(
                mesh,
                selection,
                command => { },
                () => ApplyPreview(mesh, Enumerable.Range(0, mesh.VertexCount())),
                new HashSet<int> { 0, 1, 2, 3 });
        }

        [Test]
        public void WeightedVertexTransform_UndoRestoresInitialVertices_AndRedoRestoresPreview()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new VertexSelectionState(selectable, 0)
            {
                SelectedVertices = new List<int> { 0 },
                VertexWeights = new List<float> { 1.0f, 0.5f, 0.0f, 0.25f }
            };

            RunRoundTrip(
                mesh,
                selection,
                command => { },
                () => ApplyWeightedPreview(mesh, selection.VertexWeights
                    .Select((weight, index) => new KeyValuePair<int, float>(index, weight))),
                new HashSet<int> { 0, 1, 3 });
        }

        [Test]
        public void FaceTransform_ChangesOnlyAffectedVertices_AndRoundTrips()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new FaceSelectionState
            {
                RenderObject = selectable,
                SelectedFaces = new List<int> { 0 }
            };
            var affected = new HashSet<int> { 0, 2 };

            RunRoundTrip(
                mesh,
                selection,
                command => command.AffectedVertexIndices = new HashSet<int>(affected),
                () => ApplyPreview(mesh, affected),
                affected);
        }

        [Test]
        public void EdgeTransform_ChangesOnlyAffectedVertices_AndRoundTrips()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new EdgeSelectionState
            {
                RenderObject = selectable,
                SelectedEdges = new HashSet<(int, int)> { (1, 3) }
            };
            var affected = new HashSet<int> { 1, 3 };

            RunRoundTrip(
                mesh,
                selection,
                command => command.AffectedVertexIndices = new HashSet<int>(affected),
                () => ApplyPreview(mesh, affected),
                affected);
        }

        [Test]
        public void FaceFalloffTransform_ChangesOnlyNonZeroWeightedVertices_AndRoundTrips()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new FaceSelectionState
            {
                RenderObject = selectable,
                SelectedFaces = new List<int> { 0 }
            };
            var affected = new HashSet<int> { 0, 1 };
            var falloffWeights = new Dictionary<int, float>
            {
                [0] = 1.0f,
                [1] = 1.0f,
                [2] = 0.4f,
                [3] = 0.0f
            };

            RunRoundTrip(
                mesh,
                selection,
                command =>
                {
                    command.AffectedVertexIndices = new HashSet<int>(affected);
                    command.FalloffWeights = new Dictionary<int, float>(falloffWeights);
                },
                () => ApplyWeightedPreview(mesh, falloffWeights),
                new HashSet<int> { 0, 1, 2 });
        }

        [Test]
        public void Configure_CapturesSelectionBeforeLivePreviewCommit()
        {
            var mesh = CreateMesh();
            var initialSelectable = new TestSelectableNode { Geometry = mesh };
            var replacementSelectable = new TestSelectableNode { Geometry = mesh };
            var initialSelection = new ObjectSelectionState();
            initialSelection.ModifySelectionSingleObject(initialSelectable, false);
            var replacementSelection = new ObjectSelectionState();
            replacementSelection.ModifySelectionSingleObject(replacementSelectable, false);
            var (selectionManager, commandExecutor) = CreateCommandContext(initialSelection);
            var command = new TransformVertexCommand(selectionManager);
            command.Configure(new List<MeshObject> { mesh }, Pivot);

            ApplyPreview(mesh, Enumerable.Range(0, mesh.VertexCount()));
            selectionManager.SetState(replacementSelection);
            command.Transform = ForwardTransform;
            commandExecutor.ExecuteCommand(command);
            commandExecutor.Undo();

            var restoredSelection = selectionManager.GetState<ObjectSelectionState>();
            Assert.That(restoredSelection.CurrentSelection(), Is.EquivalentTo(new[] { initialSelectable }));
        }

        static void RunRoundTrip(
            MeshObject mesh,
            ISelectionState selection,
            Action<TransformVertexCommand> configureForCommit,
            Action applyPreview,
            HashSet<int> changedIndices)
        {
            var (selectionManager, commandExecutor) = CreateCommandContext(selection);
            var command = new TransformVertexCommand(selectionManager);
            command.Configure(new List<MeshObject> { mesh }, Pivot);
            var initialVertices = mesh.VertexArray.ToArray();

            applyPreview();
            var previewVertices = mesh.VertexArray.ToArray();
            AssertPreviewChangedOnlyExpectedVertices(initialVertices, previewVertices, changedIndices);

            command.Transform = ForwardTransform;
            command.PivotPoint = Pivot;
            configureForCommit(command);
            commandExecutor.ExecuteCommand(command);

            commandExecutor.Undo();
            AssertVertices(mesh.VertexArray, initialVertices, "Undo");

            commandExecutor.Redo();
            AssertVertices(mesh.VertexArray, previewVertices, "Redo");
            Assert.That(command, Is.AssignableTo<IRedoableCommand>());
        }

        static (SelectionManager SelectionManager, CommandExecutor CommandExecutor) CreateCommandContext(ISelectionState selection)
        {
            var eventHub = new Mock<IEventHub>();
            var selectionManager = new SelectionManager(eventHub.Object, null, null, null);
            selectionManager.SetState(selection);
            return (selectionManager, new CommandExecutor(eventHub.Object));
        }

        static MeshObject CreateMesh()
        {
            var mesh = new MeshObject(new TestGeometryGraphicsContextFactory().Create(), string.Empty)
            {
                VertexArray = new[]
                {
                    CreateVertex(new Vector3(1.0f, 0.0f, 0.2f)),
                    CreateVertex(new Vector3(-0.5f, 1.3f, -0.4f)),
                    CreateVertex(new Vector3(0.25f, -0.8f, 1.5f)),
                    CreateVertex(new Vector3(1.4f, 0.9f, -1.1f))
                },
                IndexArray = new ushort[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.BuildBoundingBox();
            return mesh;
        }

        static VertexPositionNormalTextureCustom CreateVertex(Vector3 position)
        {
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(position, 1),
                Normal = Vector3.Normalize(new Vector3(0.2f, 0.4f, 1.0f)),
                Tangent = Vector3.Normalize(new Vector3(1.0f, -0.2f, 0.1f)),
                BiNormal = Vector3.Normalize(new Vector3(0.1f, 1.0f, -0.3f))
            };
        }

        static void ApplyPreview(MeshObject mesh, IEnumerable<int> vertexIndices)
        {
            var combinedTransform =
                Matrix.CreateTranslation(-Pivot) *
                ForwardTransform *
                Matrix.CreateTranslation(Pivot);
            var normalMatrix = Matrix.Transpose(Matrix.Invert(combinedTransform));
            foreach (var vertexIndex in vertexIndices)
                mesh.TransformVertex(vertexIndex, combinedTransform, normalMatrix);
            mesh.RebuildVertexBuffer();
        }

        static void ApplyWeightedPreview(MeshObject mesh, IEnumerable<KeyValuePair<int, float>> weights)
        {
            ForwardTransform.Decompose(out var scale, out var rotation, out var translation);
            foreach (var (vertexIndex, weight) in weights)
            {
                if (weight == 0)
                    continue;

                var weightedTransform =
                    Matrix.CreateScale(Vector3.Lerp(Vector3.One, scale, weight)) *
                    Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity, rotation, weight)) *
                    Matrix.CreateTranslation(translation * weight);
                var combinedTransform =
                    Matrix.CreateTranslation(-Pivot) *
                    weightedTransform *
                    Matrix.CreateTranslation(Pivot);
                var normalMatrix = Matrix.Transpose(Matrix.Invert(combinedTransform));
                mesh.TransformVertex(vertexIndex, combinedTransform, normalMatrix);
            }
            mesh.RebuildVertexBuffer();
        }

        static void AssertPreviewChangedOnlyExpectedVertices(
            VertexPositionNormalTextureCustom[] initial,
            VertexPositionNormalTextureCustom[] preview,
            HashSet<int> changedIndices)
        {
            for (var index = 0; index < initial.Length; index++)
            {
                if (changedIndices.Contains(index))
                {
                    Assert.That(
                        Vector3.Distance(initial[index].Position3(), preview[index].Position3()),
                        Is.GreaterThan(Epsilon),
                        $"Vertex {index} should move during preview");
                }
                else
                {
                    AssertVertex(preview[index], initial[index], $"Preview vertex {index}");
                }
            }
        }

        static void AssertVertices(
            VertexPositionNormalTextureCustom[] actual,
            VertexPositionNormalTextureCustom[] expected,
            string operation)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
                AssertVertex(actual[index], expected[index], $"{operation} vertex {index}");
        }

        static void AssertVertex(
            VertexPositionNormalTextureCustom actual,
            VertexPositionNormalTextureCustom expected,
            string message)
        {
            Assert.Multiple(() =>
            {
                AssertVector4(actual.Position, expected.Position, $"{message} position");
                AssertVector3(actual.Normal, expected.Normal, $"{message} normal");
                AssertVector3(actual.Tangent, expected.Tangent, $"{message} tangent");
                AssertVector3(actual.BiNormal, expected.BiNormal, $"{message} binormal");
            });
        }

        static void AssertVector3(Vector3 actual, Vector3 expected, string message)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(Epsilon), $"{message}.X");
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Epsilon), $"{message}.Y");
                Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Epsilon), $"{message}.Z");
            });
        }

        static void AssertVector4(Vector4 actual, Vector4 expected, string message)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(Epsilon), $"{message}.X");
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Epsilon), $"{message}.Y");
                Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Epsilon), $"{message}.Z");
                Assert.That(actual.W, Is.EqualTo(expected.W).Within(Epsilon), $"{message}.W");
            });
        }

        sealed class TestSelectableNode : SceneNode, ISelectable
        {
            public MeshObject Geometry { get; set; }
            public bool IsSelectable { get; set; } = true;

            public override ISceneNode CreateCopyInstance() => new TestSelectableNode();
        }
    }
}
