using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Gizmo;
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

        [Test]
        public void ObjectTranslation_PreservesNonUnitBasisThroughUndoAndRedo()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoTranslateEvent(
                    new Vector3(0.6f, -0.3f, 0.2f),
                    PivotType.ObjectCenter));
        }

        [Test]
        public void WeightedVertexScale_TwoIncrementalUpdatesRoundTripExactly()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new VertexSelectionState(selectable, 0)
            {
                SelectedVertices = new List<int> { 0 },
                VertexWeights = new List<float> { 1.0f, 0.5f, 0.0f, 0.25f }
            };
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () =>
                {
                    context.Wrapper.GizmoScaleEvent(new Vector3(0.1f, 0, 0), PivotType.ObjectCenter);
                    context.Wrapper.GizmoScaleEvent(new Vector3(0.1f, 0, 0), PivotType.ObjectCenter);
                },
                (initial, preview) =>
                {
                    Assert.That(
                        Vector3.Distance(initial[0][1].Position3(), preview[0][1].Position3()),
                        Is.GreaterThan(Epsilon));
                    AssertVertex(preview[0][2], initial[0][2], "Zero-weight preview vertex");
                });
        }

        [Test]
        public void ObjectNegativeNonUniformScale_IsAcceptedAndRoundTrips()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoScaleEvent(
                    new Vector3(-2.2f, -0.2f, 0.1f),
                    PivotType.ObjectCenter),
                (initial, preview) => Assert.That(
                    Vector3.Distance(initial[0][0].Position3(), preview[0][0].Position3()),
                    Is.GreaterThan(Epsilon)));
        }

        [Test]
        public void FaceScale_ChangesOnlySelectedFaceVertices_AndRoundTrips()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new FaceSelectionState
            {
                RenderObject = selectable,
                SelectedFaces = new List<int> { 0 }
            };
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoScaleEvent(
                    new Vector3(0.2f, -0.1f, 0.15f),
                    PivotType.ObjectCenter),
                (initial, preview) =>
                {
                    Assert.That(
                        Vector3.Distance(initial[0][0].Position3(), preview[0][0].Position3()),
                        Is.GreaterThan(Epsilon));
                    AssertVertex(preview[0][3], initial[0][3], "Unselected face vertex");
                });
        }

        [Test]
        public void EdgeRotation_PreservesNonUnitBasisOnAffectedVertices_AndRoundTrips()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 1);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new EdgeSelectionState
            {
                RenderObject = selectable,
                SelectedEdges = new HashSet<(int, int)> { (1, 3) }
            };
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoRotateEvent(
                    Matrix.CreateFromAxisAngle(Vector3.UnitZ, 0.35f),
                    PivotType.ObjectCenter),
                (initial, preview) =>
                {
                    Assert.That(
                        Vector3.Distance(initial[0][1].Position3(), preview[0][1].Position3()),
                        Is.GreaterThan(Epsilon));
                    AssertVertex(preview[0][0], initial[0][0], "Unselected edge vertex");
                });
        }

        [Test]
        public void FaceFalloff_ZeroWeightVertexRemainsByteForByteUntouched_AndRoundTrips()
        {
            var mesh = CreateFalloffMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new FaceSelectionState
            {
                RenderObject = selectable,
                SelectedFaces = new List<int> { 0 }
            };
            var context = CreateTransformContext(selection, mesh);
            context.Wrapper.SetFalloffDistance(1.0f);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoTranslateEvent(
                    new Vector3(0.4f, -0.2f, 0.1f),
                    PivotType.ObjectCenter),
                (initial, preview) =>
                {
                    Assert.That(
                        Vector3.Distance(initial[0][0].Position3(), preview[0][0].Position3()),
                        Is.GreaterThan(Epsilon));
                    Assert.That(preview[0][3], Is.EqualTo(initial[0][3]));
                });
        }

        [Test]
        public void ObjectZeroScale_IsRejectedWithoutMutatingGeometry()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initial = mesh.VertexArray.ToArray();
            context.Wrapper.Start(context.CommandExecutor);

            context.Wrapper.GizmoScaleEvent(new Vector3(-1.0f, 0, 0), PivotType.ObjectCenter);

            AssertVertices(mesh.VertexArray, initial, "Rejected zero scale");
            AssertVerticesAreFinite(mesh.VertexArray);
        }

        [Test]
        public void WeightedSingularScale_IsRejectedWithoutMutatingGeometry()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 1);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new VertexSelectionState(selectable, 0)
            {
                SelectedVertices = new List<int> { 0 },
                VertexWeights = new List<float> { 1.0f, 0.5f, 0.0f, 0.25f }
            };
            var context = CreateTransformContext(selection, mesh);
            var initial = mesh.VertexArray.ToArray();
            context.Wrapper.Start(context.CommandExecutor);

            context.Wrapper.GizmoScaleEvent(new Vector3(-2.0f, 0, 0), PivotType.ObjectCenter);

            AssertVertices(mesh.VertexArray, initial, "Rejected weighted-singular scale");
            AssertVerticesAreFinite(mesh.VertexArray);
        }

        [Test]
        public void NonFiniteTranslation_IsRejectedWithoutMutatingGeometry()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initial = mesh.VertexArray.ToArray();
            context.Wrapper.Start(context.CommandExecutor);

            context.Wrapper.GizmoTranslateEvent(
                new Vector3(float.NaN, 0, 0),
                PivotType.ObjectCenter);

            AssertVertices(mesh.VertexArray, initial, "Rejected non-finite translation");
            AssertVerticesAreFinite(mesh.VertexArray);
        }

        [Test]
        public void ObjectReplay_RebuildsEachMeshOncePerUndoAndRedo()
        {
            var firstMesh = CreateMesh(out var firstContext);
            var secondMesh = CreateMesh(out var secondContext);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, firstMesh, secondMesh);
            context.Wrapper.Start(context.CommandExecutor);
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.3f, 0.2f, -0.1f),
                PivotType.ObjectCenter);
            context.Wrapper.Stop(context.CommandExecutor);
            firstContext.ResetRebuildCounts();
            secondContext.ResetRebuildCounts();

            context.CommandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(firstContext.VertexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(secondContext.VertexBufferRebuildCount, Is.EqualTo(1));
            });

            firstContext.ResetRebuildCounts();
            secondContext.ResetRebuildCounts();
            context.CommandExecutor.Redo();

            Assert.Multiple(() =>
            {
                Assert.That(firstContext.VertexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(secondContext.VertexBufferRebuildCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Configure_CapturesSelectionUsedByCommandReplay()
        {
            var mesh = CreateMesh();
            var initialSelectable = new TestSelectableNode { Geometry = mesh };
            var replacementSelectable = new TestSelectableNode { Geometry = mesh };
            var initialSelection = new ObjectSelectionState();
            initialSelection.ModifySelectionSingleObject(initialSelectable, false);
            var replacementSelection = new ObjectSelectionState();
            replacementSelection.ModifySelectionSingleObject(replacementSelectable, false);
            var (selectionManager, commandExecutor) = CreateCommandContext(initialSelection);
            var command = new TransformVertexCommand(selectionManager)
            {
                Transform = Matrix.Identity
            };
            command.Configure(new List<MeshObject> { mesh }, Vector3.Zero);

            selectionManager.SetState(replacementSelection);
            commandExecutor.ExecuteCommand(command);
            commandExecutor.Undo();

            var restoredSelection = selectionManager.GetState<ObjectSelectionState>();
            Assert.That(restoredSelection.CurrentSelection(), Is.EquivalentTo(new[] { initialSelectable }));
        }

        static void RunRoundTrip(
            TransformContext context,
            IReadOnlyList<MeshObject> meshes,
            Action applyPreview,
            Action<VertexPositionNormalTextureCustom[][], VertexPositionNormalTextureCustom[][]> assertPreview = null)
        {
            var initialVertices = Snapshot(meshes);
            context.Wrapper.Start(context.CommandExecutor);

            applyPreview();

            var previewVertices = Snapshot(meshes);
            assertPreview?.Invoke(initialVertices, previewVertices);
            context.Wrapper.Stop(context.CommandExecutor);

            context.CommandExecutor.Undo();
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                AssertVertices(meshes[meshIndex].VertexArray, initialVertices[meshIndex], $"Undo mesh {meshIndex}");

            context.CommandExecutor.Redo();
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                AssertVertices(meshes[meshIndex].VertexArray, previewVertices[meshIndex], $"Redo mesh {meshIndex}");
        }

        static TransformContext CreateTransformContext(ISelectionState selection, params MeshObject[] meshes)
        {
            var (selectionManager, commandExecutor) = CreateCommandContext(selection);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
                .Returns(() => new TransformVertexCommand(selectionManager));
            var commandFactory = new CommandFactory(serviceProvider.Object, commandExecutor);
            var wrapper = new TransformGizmoWrapper(commandFactory, meshes.ToList(), selection);
            return new TransformContext(commandExecutor, wrapper);
        }

        static (SelectionManager SelectionManager, CommandExecutor CommandExecutor) CreateCommandContext(ISelectionState selection)
        {
            var eventHub = new Mock<IEventHub>();
            var selectionManager = new SelectionManager(eventHub.Object, null, null, null);
            selectionManager.SetState(selection);
            return (selectionManager, new CommandExecutor(eventHub.Object));
        }

        static MeshObject CreateMesh(int nonUnitBasisVertex = -1)
        {
            return CreateMesh(out _, nonUnitBasisVertex);
        }

        static MeshObject CreateMesh(out TestGraphicsCardGeometry context, int nonUnitBasisVertex = -1)
        {
            context = new TestGraphicsCardGeometry();
            var mesh = new MeshObject(context, string.Empty)
            {
                VertexArray = new[]
                {
                    CreateVertex(new Vector3(1.0f, 0.0f, 0.2f), nonUnitBasisVertex == 0),
                    CreateVertex(new Vector3(-0.5f, 1.3f, -0.4f), nonUnitBasisVertex == 1),
                    CreateVertex(new Vector3(0.25f, -0.8f, 1.5f), nonUnitBasisVertex == 2),
                    CreateVertex(new Vector3(1.4f, 0.9f, -1.1f), nonUnitBasisVertex == 3)
                },
                IndexArray = new ushort[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.BuildBoundingBox();
            return mesh;
        }

        static MeshObject CreateFalloffMesh()
        {
            var mesh = new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
            {
                VertexArray = new[]
                {
                    CreateVertex(new Vector3(0, 0, 0)),
                    CreateVertex(new Vector3(1, 0, 0)),
                    CreateVertex(new Vector3(0, 1, 0)),
                    CreateVertex(new Vector3(2, 0, 0), nonUnitBasis: true)
                },
                IndexArray = new ushort[] { 0, 1, 2, 1, 3, 2 }
            };
            mesh.BuildBoundingBox();
            return mesh;
        }

        static VertexPositionNormalTextureCustom CreateVertex(Vector3 position, bool nonUnitBasis = false)
        {
            var magnitude = nonUnitBasis ? 2.5f : 1.0f;
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(position, 1),
                Normal = Vector3.Normalize(new Vector3(0.2f, 0.4f, 1.0f)) * magnitude,
                Tangent = Vector3.Normalize(new Vector3(1.0f, -0.2f, 0.1f)) * magnitude,
                BiNormal = Vector3.Normalize(new Vector3(0.1f, 1.0f, -0.3f)) * magnitude
            };
        }

        static VertexPositionNormalTextureCustom[][] Snapshot(IReadOnlyList<MeshObject> meshes)
        {
            return meshes.Select(mesh => mesh.VertexArray.ToArray()).ToArray();
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

        static void AssertVerticesAreFinite(IEnumerable<VertexPositionNormalTextureCustom> vertices)
        {
            foreach (var vertex in vertices)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(IsFinite(vertex.Position), Is.True);
                    Assert.That(IsFinite(vertex.Normal), Is.True);
                    Assert.That(IsFinite(vertex.Tangent), Is.True);
                    Assert.That(IsFinite(vertex.BiNormal), Is.True);
                });
            }
        }

        static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z);
        }

        static bool IsFinite(Vector4 value)
        {
            return IsFinite(new Vector3(value.X, value.Y, value.Z)) &&
                   float.IsFinite(value.W);
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

        sealed record TransformContext(
            CommandExecutor CommandExecutor,
            TransformGizmoWrapper Wrapper);

        sealed class TestSelectableNode : SceneNode, ISelectable
        {
            public MeshObject Geometry { get; set; }
            public bool IsSelectable { get; set; } = true;

            public override ISceneNode CreateCopyInstance() => new TestSelectableNode();
        }
    }
}
