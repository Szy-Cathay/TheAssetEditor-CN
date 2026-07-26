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
                    AssertVector3(
                        preview[0][1].Position3(),
                        new Vector3(-0.65375f, 1.3f, -0.4f),
                        "Weighted two-update preview vertex 1");
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
                (_, preview) => AssertVector3(
                    preview[0][0].Position3(),
                    new Vector3(-0.21f, 0.05f, 0.2f),
                    "Negative nonuniform object-pivot preview"));
        }

        [TestCase(GeometrySelectionMode.Vertex)]
        [TestCase(GeometrySelectionMode.Face)]
        [TestCase(GeometrySelectionMode.Edge)]
        public void EditModeOneNegativeAxisScale_NeverChangesIndices(
            GeometrySelectionMode selectionMode)
        {
            var mesh = CreateMesh();
            var selection = CreateEditSelection(selectionMode, mesh);
            var context = CreateTransformContext(selection, mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, 0, 0),
                PivotType.ObjectCenter);
            var previewIndices = mesh.IndexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoIndices = mesh.IndexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoIndices = mesh.IndexArray.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(previewIndices, Is.EqualTo(initialIndices));
                Assert.That(undoIndices, Is.EqualTo(initialIndices));
                Assert.That(redoIndices, Is.EqualTo(initialIndices));
            });
        }

        [Test]
        public void ObjectOneNegativeAxisScale_ReversesPreviewAndRoundTripsIndices()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            var reversedIndices = new ushort[] { 2, 1, 0, 3, 2, 0 };
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, 0, 0),
                PivotType.ObjectCenter);
            var previewIndices = mesh.IndexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoIndices = mesh.IndexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoIndices = mesh.IndexArray.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(previewIndices, Is.EqualTo(reversedIndices));
                Assert.That(undoIndices, Is.EqualTo(initialIndices));
                Assert.That(redoIndices, Is.EqualTo(reversedIndices));
            });
        }

        [Test]
        public void ObjectTwoNegativeAxisScale_NeverChangesIndices()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, -2.2f, 0),
                PivotType.ObjectCenter);
            var previewIndices = mesh.IndexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoIndices = mesh.IndexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoIndices = mesh.IndexArray.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(previewIndices, Is.EqualTo(initialIndices));
                Assert.That(undoIndices, Is.EqualTo(initialIndices));
                Assert.That(redoIndices, Is.EqualTo(initialIndices));
            });
        }

        [Test]
        public void ObjectScale_CrossingDeterminantSignTwice_ReversesWindingExactlyTwice()
        {
            var mesh = CreateMesh(out var graphicsContext);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            var reversedIndices = new ushort[] { 2, 1, 0, 3, 2, 0 };
            context.Wrapper.BeginTransform();
            graphicsContext.ResetRebuildCounts();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, 0, 0),
                PivotType.ObjectCenter);
            var firstPreviewIndices = mesh.IndexArray.ToArray();
            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, 0, 0),
                PivotType.ObjectCenter);
            var secondPreviewIndices = mesh.IndexArray.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(firstPreviewIndices, Is.EqualTo(reversedIndices));
                Assert.That(secondPreviewIndices, Is.EqualTo(initialIndices));
                Assert.That(graphicsContext.IndexBufferRebuildCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void ObjectEmptySecondGesture_DoesNotInheritPriorWindingParity()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            var reversedIndices = new ushort[] { 2, 1, 0, 3, 2, 0 };
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, 0, 0),
                PivotType.ObjectCenter);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            context.Wrapper.BeginTransform();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            var secondPreviewIndices = mesh.IndexArray.ToArray();
            context.CommandExecutor.Undo();
            var secondUndoIndices = mesh.IndexArray.ToArray();
            context.CommandExecutor.Redo();
            var secondRedoIndices = mesh.IndexArray.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(secondPreviewIndices, Is.EqualTo(reversedIndices));
                Assert.That(secondUndoIndices, Is.EqualTo(initialIndices));
                Assert.That(secondRedoIndices, Is.EqualTo(reversedIndices));
            });
        }

        [Test]
        public void ObjectBeginningReplacementGesture_CancelsPriorPreviewAndResetsWindingParity()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoScaleEvent(
                new Vector3(-2.2f, 0, 0),
                PivotType.ObjectCenter);

            context.Wrapper.BeginTransform();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(mesh.IndexArray, Is.EqualTo(initialIndices));
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
            });
        }

        [Test]
        public void ObjectNearSingularScale_IsRejectedAcrossCommitUndoAndRedo()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            var initialIndices = mesh.IndexArray.ToArray();
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-1.00001f, 0, 0),
                PivotType.ObjectCenter);
            var previewVertices = mesh.VertexArray.ToArray();
            var previewIndices = mesh.IndexArray.ToArray();
            var previewScale = context.Wrapper.Scale;
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            AssertVertices(undoVertices, initialVertices, "Rejected near-singular scale Undo");
            AssertVertices(previewVertices, initialVertices, "Rejected near-singular scale preview");
            AssertVertices(redoVertices, initialVertices, "Rejected near-singular scale Redo");
            Assert.Multiple(() =>
            {
                Assert.That(previewScale, Is.EqualTo(Vector3.One));
                Assert.That(previewIndices, Is.EqualTo(initialIndices));
                Assert.That(mesh.IndexArray, Is.EqualTo(initialIndices));
            });
        }

        [Test]
        public void ObjectScale_PivotTenAcrossMeshes_IsRejectedAcrossCommitUndoAndRedo()
        {
            var stableMesh = CreatePivotTenMesh(collapsedX: true);
            var unstableMesh = CreatePivotTenMesh(collapsedX: false);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, stableMesh, unstableMesh);
            var initialVertices = Snapshot(new[] { stableMesh, unstableMesh });
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-0.9989f, 0, 0),
                PivotType.ObjectCenter);
            var previewVertices = Snapshot(new[] { stableMesh, unstableMesh });
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = Snapshot(new[] { stableMesh, unstableMesh });
            context.CommandExecutor.Redo();
            var redoVertices = Snapshot(new[] { stableMesh, unstableMesh });

            for (var meshIndex = 0; meshIndex < initialVertices.Length; meshIndex++)
            {
                AssertVertices(
                    previewVertices[meshIndex],
                    initialVertices[meshIndex],
                    $"Pivot-ten rejected scale preview mesh {meshIndex}");
                AssertVertices(
                    undoVertices[meshIndex],
                    initialVertices[meshIndex],
                    $"Pivot-ten rejected scale Undo mesh {meshIndex}");
                AssertVertices(
                    redoVertices[meshIndex],
                    initialVertices[meshIndex],
                    $"Pivot-ten rejected scale Redo mesh {meshIndex}");
            }
        }

        [Test]
        public void ObjectScale_PivotTenCommonFactor_IsAcceptedAndRoundTrips()
        {
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoScaleEvent(
                    new Vector3(-0.5f, 0, 0),
                    PivotType.ObjectCenter),
                (_, preview) => AssertVector3(
                    preview[0][0].Position3(),
                    new Vector3(10.5f, 0, 0),
                    "Pivot-ten common scale preview"));
        }

        [Test]
        public void ObjectScale_PivotTenWith240IncrementalUpdates_RoundTripsWithinTolerance()
        {
            const int updateCount = 240;
            const float scaleDelta = -0.001f;
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            var aggregateScale = MathF.Pow(1.0f + scaleDelta, updateCount);
            context.Wrapper.BeginTransform();

            for (var updateIndex = 0; updateIndex < updateCount; updateIndex++)
            {
                context.Wrapper.GizmoScaleEvent(
                    new Vector3(scaleDelta, 0, 0),
                    PivotType.ObjectCenter);
            }

            var previewVertices = mesh.VertexArray.ToArray();
            var previewScale = context.Wrapper.Scale;
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            var undoError = MathF.Abs(
                undoVertices[0].Position.X -
                initialVertices[0].Position.X);
            TestContext.Out.WriteLine(
                $"240-update vertex 0 Undo X error: {undoError:R}; " +
                $"preview X: {previewVertices[0].Position.X:R}; " +
                $"expected preview X: {10.0f + aggregateScale:R}; " +
                $"Redo X: {redoVertices[0].Position.X:R}");
            for (var vertexIndex = 0; vertexIndex < initialVertices.Length; vertexIndex++)
            {
                var initialPosition = initialVertices[vertexIndex].Position3();
                var expectedPreviewPosition = new Vector3(
                    10.0f + (initialPosition.X - 10.0f) * aggregateScale,
                    initialPosition.Y,
                    initialPosition.Z);
                AssertVector3(
                    previewVertices[vertexIndex].Position3(),
                    expectedPreviewPosition,
                    $"Long incremental preview vertex {vertexIndex}");
            }

            Assert.That(
                previewScale.X,
                Is.EqualTo(1.0f + updateCount * scaleDelta).Within(Epsilon));
            AssertVertices(undoVertices, initialVertices, "Long incremental Undo");
            for (var vertexIndex = 0; vertexIndex < initialVertices.Length; vertexIndex++)
            {
                var initialPosition = initialVertices[vertexIndex].Position3();
                var expectedPreviewPosition = new Vector3(
                    10.0f + (initialPosition.X - 10.0f) * aggregateScale,
                    initialPosition.Y,
                    initialPosition.Z);
                AssertVector3(
                    redoVertices[vertexIndex].Position3(),
                    expectedPreviewPosition,
                    $"Long incremental Redo vertex {vertexIndex}");
            }

            AssertVertices(redoVertices, previewVertices, "Long incremental Redo");
        }

        [Test]
        public void WeightedVertexScale_PivotTenWith240IncrementalUpdates_RoundTripsWithinTolerance()
        {
            const int updateCount = 240;
            const float scaleDelta = -0.001f;
            var weights = new[] { 1.0f, 0.5f, 0.0f, 0.25f };
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new VertexSelectionState(selectable, 0)
            {
                SelectedVertices = new List<int> { 0, 1 },
                VertexWeights = weights.ToList()
            };
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            Assert.That(context.Wrapper.Position.X, Is.EqualTo(10.0f).Within(Epsilon));
            context.Wrapper.BeginTransform();

            for (var updateIndex = 0; updateIndex < updateCount; updateIndex++)
            {
                context.Wrapper.GizmoScaleEvent(
                    new Vector3(scaleDelta, 0, 0),
                    PivotType.ObjectCenter);
            }

            var previewVertices = mesh.VertexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            TestContext.Out.WriteLine(
                $"240-update weighted vertex 0 Undo X error: " +
                $"{MathF.Abs(undoVertices[0].Position.X - initialVertices[0].Position.X):R}");
            AssertVector3(
                previewVertices[0].Position3(),
                new Vector3(
                    10.0f + MathF.Pow(1.0f + scaleDelta, updateCount),
                    0,
                    0),
                "Long weighted full-weight preview vertex");
            Assert.That(previewVertices[1].Position.X, Is.GreaterThan(initialVertices[1].Position.X));
            AssertVertex(previewVertices[2], initialVertices[2], "Long weighted zero-weight preview vertex");
            Assert.That(previewVertices[3].Position.X, Is.LessThan(initialVertices[3].Position.X));

            AssertVertices(undoVertices, initialVertices, "Long weighted Undo");
            AssertVertices(redoVertices, previewVertices, "Long weighted Redo");
        }

        [Test]
        public void ObjectTranslation_LargeCoordinateWith240IncrementalUpdates_RendersAndRoundTripsFromBaseline()
        {
            const int updateCount = 240;
            var delta = new Vector3(0.001f, 0, 0);
            var mesh = CreateLargeCoordinateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            context.Wrapper.BeginTransform();

            for (var updateIndex = 0; updateIndex < updateCount; updateIndex++)
                context.Wrapper.GizmoTranslateEvent(delta, PivotType.WorldOrigin);

            var previewVertices = mesh.VertexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            var expectedTranslation = delta * updateCount;
            for (var vertexIndex = 0; vertexIndex < initialVertices.Length; vertexIndex++)
            {
                AssertVector3(
                    previewVertices[vertexIndex].Position3(),
                    initialVertices[vertexIndex].Position3() + expectedTranslation,
                    $"Long translation preview vertex {vertexIndex}");
            }

            TestContext.Out.WriteLine(
                $"240-update translation Undo X error: " +
                $"{MathF.Abs(undoVertices[0].Position.X - initialVertices[0].Position.X):R}; " +
                $"preview X: {previewVertices[0].Position.X:R}; " +
                $"Redo X: {redoVertices[0].Position.X:R}");
            AssertVertices(undoVertices, initialVertices, "Long translation Undo");
            AssertVertices(redoVertices, previewVertices, "Long translation Redo");
        }

        [Test]
        public void ObjectScale_PivotTenRejectsTwentyFirstHalfScaleAndKeepsLastValidPreview()
        {
            const int acceptedUpdateCount = 20;
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            context.Wrapper.BeginTransform();

            for (var updateIndex = 0; updateIndex < acceptedUpdateCount; updateIndex++)
            {
                context.Wrapper.GizmoScaleEvent(
                    new Vector3(-0.5f, 0, 0),
                    PivotType.ObjectCenter);
            }

            var lastValidPreview = mesh.VertexArray.ToArray();
            var lastValidScale = context.Wrapper.Scale;
            Assert.That(lastValidPreview[0].Position.X, Is.GreaterThan(10.0f));
            Assert.That(
                lastValidPreview[0].Position.X,
                Is.EqualTo(10.0f + MathF.Pow(0.5f, acceptedUpdateCount)).Within(Epsilon));

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-0.5f, 0, 0),
                PivotType.ObjectCenter);
            AssertVertices(mesh.VertexArray, lastValidPreview, "Rejected cumulative contraction preview");
            Assert.That(context.Wrapper.Scale, Is.EqualTo(lastValidScale));
            context.Wrapper.CommitTransform(context.CommandExecutor);

            context.CommandExecutor.Undo();
            AssertVertices(mesh.VertexArray, initialVertices, "Cumulative contraction Undo");
            context.CommandExecutor.Redo();
            AssertVertices(mesh.VertexArray, lastValidPreview, "Cumulative contraction Redo");
        }

        [Test]
        public void ObjectRotation_LargeCoordinateWith240SmallUpdates_PreservesNonUnitBasisAndRoundTrips()
        {
            const int updateCount = 240;
            var rotation = Matrix.CreateFromAxisAngle(Vector3.UnitZ, 0.001f);
            var mesh = CreateLargeCoordinateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            context.Wrapper.BeginTransform();

            for (var updateIndex = 0; updateIndex < updateCount; updateIndex++)
                context.Wrapper.GizmoRotateEvent(rotation, PivotType.WorldOrigin);

            var previewVertices = mesh.VertexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            Assert.That(
                Vector3.Distance(
                    previewVertices[0].Position3(),
                    initialVertices[0].Position3()),
                Is.GreaterThan(1.0f));
            Assert.That(previewVertices[0].Normal.Length(), Is.EqualTo(2.5f).Within(Epsilon));
            Assert.That(previewVertices[0].Tangent.Length(), Is.EqualTo(2.5f).Within(Epsilon));
            Assert.That(previewVertices[0].BiNormal.Length(), Is.EqualTo(2.5f).Within(Epsilon));
            TestContext.Out.WriteLine(
                $"240-update rotation Undo position error: " +
                $"{Vector3.Distance(undoVertices[0].Position3(), initialVertices[0].Position3()):R}; " +
                $"Redo-preview position error: " +
                $"{Vector3.Distance(redoVertices[0].Position3(), previewVertices[0].Position3()):R}");
            AssertVertices(undoVertices, initialVertices, "Long rotation Undo");
            AssertVertices(redoVertices, previewVertices, "Long rotation Redo");
        }

        [Test]
        public void MixedModeAndPivotGesture_RendersAggregateFromBaselineAndRoundTrips()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            var translation = new Vector3(0.25f, -0.1f, 0.05f);
            var rotation = Matrix.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
            var scale = Matrix.CreateScale(1.1f, 0.9f, 1.05f);
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoTranslateEvent(translation, PivotType.WorldOrigin);
            var rotationPivot = context.Wrapper.Position;
            context.Wrapper.GizmoRotateEvent(rotation, PivotType.ObjectCenter);
            context.Wrapper.GizmoScaleEvent(new Vector3(0.1f, -0.1f, 0.05f), PivotType.WorldOrigin);
            var previewVertices = mesh.VertexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            var aggregate =
                Matrix.CreateTranslation(translation) *
                Matrix.CreateTranslation(-rotationPivot) *
                rotation *
                Matrix.CreateTranslation(rotationPivot) *
                scale;
            var expectedPosition = Vector4.Transform(initialVertices[0].Position, aggregate);
            AssertVector4(previewVertices[0].Position, expectedPosition, "Mixed gesture preview");
            AssertVertices(undoVertices, initialVertices, "Mixed gesture Undo");
            AssertVertices(redoVertices, previewVertices, "Mixed gesture Redo");
        }

        [Test]
        public void NormalGesture_StartCapturesAndStopClearsTransientBackup()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            Assert.Multiple(() =>
            {
                Assert.That(context.Wrapper.HasBackup, Is.False);
                Assert.That(mesh.DeferBoundingBoxRebuild, Is.False);
            });

            context.Wrapper.BeginTransform();
            Assert.Multiple(() =>
            {
                Assert.That(context.Wrapper.HasBackup, Is.True);
                Assert.That(mesh.DeferBoundingBoxRebuild, Is.True);
            });
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.2f, 0, 0),
                PivotType.WorldOrigin);
            var previewVertices = mesh.VertexArray.ToArray();

            context.Wrapper.CommitTransform(context.CommandExecutor);
            Assert.Multiple(() =>
            {
                Assert.That(context.Wrapper.HasBackup, Is.False);
                Assert.That(mesh.DeferBoundingBoxRebuild, Is.False);
            });
            context.CommandExecutor.Undo();
            context.CommandExecutor.Redo();
            AssertVertices(mesh.VertexArray, previewVertices, "Backup lifecycle Redo");
        }

        [Test]
        public void MixedValidThenCoordinateInvalidScale_StoresOnlyValidOperation()
        {
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(0.1f, 0, 0),
                PivotType.ObjectCenter);
            var validPreview = mesh.VertexArray.ToArray();
            AssertVector3(
                validPreview[0].Position3(),
                new Vector3(11.1f, 0, 0),
                "Valid preview before rejected delta");

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-0.9989f, 0, 0),
                PivotType.ObjectCenter);
            AssertVertices(mesh.VertexArray, validPreview, "Rejected delta after valid preview");
            Assert.That(context.Wrapper.Scale, Is.EqualTo(new Vector3(1.1f, 1, 1)));
            context.Wrapper.CommitTransform(context.CommandExecutor);

            context.CommandExecutor.Undo();
            AssertVertices(mesh.VertexArray, initialVertices, "Mixed valid-invalid Undo");
            context.CommandExecutor.Redo();
            AssertVertices(mesh.VertexArray, validPreview, "Mixed valid-invalid Redo");
        }

        [Test]
        public void VertexScaleCoordinatePreflight_RejectsAffectedPositions()
        {
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new VertexSelectionState(selectable, 0)
            {
                SelectedVertices = new List<int> { 0, 1 },
                VertexWeights = new List<float> { 1.0f, 1.0f, 0.0f, 0.0f }
            };
            var context = CreateTransformContext(selection, mesh);

            AssertRejectedScaleRoundTrip(context, new[] { mesh }, "Vertex coordinate preflight");
        }

        [Test]
        public void FaceScaleCoordinatePreflight_RejectsAffectedPositions()
        {
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new FaceSelectionState
            {
                RenderObject = selectable,
                SelectedFaces = new List<int> { 0 }
            };
            var context = CreateTransformContext(selection, mesh);

            AssertRejectedScaleRoundTrip(context, new[] { mesh }, "Face coordinate preflight");
        }

        [Test]
        public void EdgeScaleCoordinatePreflight_RejectsAffectedPositions()
        {
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new EdgeSelectionState
            {
                RenderObject = selectable,
                SelectedEdges = new HashSet<(int, int)> { (0, 1) }
            };
            var context = CreateTransformContext(selection, mesh);

            AssertRejectedScaleRoundTrip(context, new[] { mesh }, "Edge coordinate preflight");
        }

        [Test]
        public void FaceFalloffScaleCoordinatePreflight_RejectsWeightedPositions()
        {
            var mesh = CreatePivotTenMesh(collapsedX: false);
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new FaceSelectionState
            {
                RenderObject = selectable,
                SelectedFaces = new List<int> { 0 }
            };
            var context = CreateTransformContext(selection, mesh);
            context.Wrapper.SetFalloffDistance(2.0f);

            AssertRejectedScaleRoundTrip(context, new[] { mesh }, "Falloff coordinate preflight");
        }

        [Test]
        public void WeightedNearSingularScale_IsRejectedAcrossCommitUndoAndRedo()
        {
            var mesh = CreateMesh();
            var selectable = new TestSelectableNode { Geometry = mesh };
            var selection = new VertexSelectionState(selectable, 0)
            {
                SelectedVertices = new List<int> { 0 },
                VertexWeights = new List<float> { 1.0f, 0.5f, 0.0f, 0.25f }
            };
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            var initialIndices = mesh.IndexArray.ToArray();
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-1.99998f, 0, 0),
                PivotType.ObjectCenter);
            var previewVertices = mesh.VertexArray.ToArray();
            var previewIndices = mesh.IndexArray.ToArray();
            var previewScale = context.Wrapper.Scale;
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = mesh.VertexArray.ToArray();
            context.CommandExecutor.Redo();
            var redoVertices = mesh.VertexArray.ToArray();

            AssertVertices(previewVertices, initialVertices, "Rejected weighted near-singular preview");
            AssertVertices(undoVertices, initialVertices, "Rejected weighted near-singular Undo");
            AssertVertices(redoVertices, initialVertices, "Rejected weighted near-singular Redo");
            Assert.Multiple(() =>
            {
                Assert.That(previewScale, Is.EqualTo(Vector3.One));
                Assert.That(previewIndices, Is.EqualTo(initialIndices));
                Assert.That(mesh.IndexArray, Is.EqualTo(initialIndices));
            });
        }

        [Test]
        public void ObjectScale_PreservesNonUnitBasisMagnitudeThroughUndoAndRedo()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);

            RunRoundTrip(
                context,
                new[] { mesh },
                () => context.Wrapper.GizmoScaleEvent(
                    new Vector3(0.2f, -0.1f, 0.15f),
                    PivotType.ObjectCenter),
                (initial, preview) =>
                {
                    AssertVector3(
                        preview[0][0].Position3(),
                        new Vector3(1.11f, 0.025f, 0.2f),
                        "Non-unit object scale preview position");
                    var scale = new Vector3(1.2f, 0.9f, 1.15f);
                    AssertVector3(
                        preview[0][0].Normal,
                        ScaleBasisPreservingMagnitude(initial[0][0].Normal, scale),
                        "Non-unit object scale preview normal");
                    AssertVector3(
                        preview[0][0].Tangent,
                        ScaleBasisPreservingMagnitude(initial[0][0].Tangent, scale),
                        "Non-unit object scale preview tangent");
                    AssertVector3(
                        preview[0][0].BiNormal,
                        ScaleBasisPreservingMagnitude(initial[0][0].BiNormal, scale),
                        "Non-unit object scale preview binormal");
                });
        }

        [Test]
        public void ModalRestore_ReplacesPriorOperationBeforeConfirmUndoAndRedo()
        {
            var mesh = CreateMesh();
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initialVertices = mesh.VertexArray.ToArray();

            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.3f, 0, 0),
                PivotType.ObjectCenter);
            AssertVector3(
                mesh.VertexArray[0].Position3(),
                new Vector3(1.3f, 0, 0.2f),
                "First modal preview");

            context.Wrapper.RestoreInitialPreviewState();
            AssertVertices(mesh.VertexArray, initialVertices, "Modal restore");
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0, 0.4f, 0),
                PivotType.ObjectCenter);
            var confirmedVertices = mesh.VertexArray.ToArray();
            AssertVector3(
                confirmedVertices[0].Position3(),
                new Vector3(1.0f, 0.4f, 0.2f),
                "Reapplied modal preview");

            context.Wrapper.CommitTransform(context.CommandExecutor);
            Assert.That(context.Wrapper.HasBackup, Is.False);
            context.CommandExecutor.Undo();
            AssertVertices(mesh.VertexArray, initialVertices, "Modal confirm Undo");
            context.CommandExecutor.Redo();
            AssertVertices(mesh.VertexArray, confirmedVertices, "Modal confirm Redo");
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
                    AssertVector3(
                        preview[0][0].Position3(),
                        new Vector3(1.15f, 0.01666667f, 0.165f),
                        "Face object-pivot preview vertex");
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
                    PivotType.WorldOrigin),
                (initial, preview) =>
                {
                    AssertVector3(
                        preview[0][1].Position3(),
                        new Vector3(-0.9154533f, 1.0497355f, -0.4f),
                        "World-origin edge rotation preview");
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
                    AssertVector3(
                        preview[0][0].Position3(),
                        new Vector3(0.4f, -0.2f, 0.1f),
                        "Full-weight falloff preview vertex");
                    AssertVector3(
                        preview[0][3].Position3(),
                        new Vector3(1.7f, -0.1f, 0.05f),
                        "Half-weight falloff preview vertex");
                    Assert.That(preview[0][4], Is.EqualTo(initial[0][4]));
                });
        }

        [Test]
        public void ObjectZeroScale_IsRejectedWithoutMutatingGeometry()
        {
            var mesh = CreateMesh(nonUnitBasisVertex: 0);
            var selection = new ObjectSelectionState();
            var context = CreateTransformContext(selection, mesh);
            var initial = mesh.VertexArray.ToArray();
            context.Wrapper.BeginTransform();

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
            context.Wrapper.BeginTransform();

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
            context.Wrapper.BeginTransform();

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
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.3f, 0.2f, -0.1f),
                PivotType.ObjectCenter);
            context.Wrapper.CommitTransform(context.CommandExecutor);
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
            context.Wrapper.BeginTransform();

            applyPreview();

            var previewVertices = Snapshot(meshes);
            assertPreview?.Invoke(initialVertices, previewVertices);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            context.CommandExecutor.Undo();
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                AssertVertices(meshes[meshIndex].VertexArray, initialVertices[meshIndex], $"Undo mesh {meshIndex}");

            context.CommandExecutor.Redo();
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                AssertVertices(meshes[meshIndex].VertexArray, previewVertices[meshIndex], $"Redo mesh {meshIndex}");
        }

        static void AssertRejectedScaleRoundTrip(
            TransformContext context,
            IReadOnlyList<MeshObject> meshes,
            string message)
        {
            var initialVertices = Snapshot(meshes);
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoScaleEvent(
                new Vector3(-0.9989f, 0, 0),
                PivotType.ObjectCenter);
            var previewVertices = Snapshot(meshes);
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            var undoVertices = Snapshot(meshes);
            context.CommandExecutor.Redo();
            var redoVertices = Snapshot(meshes);

            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                AssertVertices(
                    previewVertices[meshIndex],
                    initialVertices[meshIndex],
                    $"{message} preview mesh {meshIndex}");
                AssertVertices(
                    undoVertices[meshIndex],
                    initialVertices[meshIndex],
                    $"{message} Undo mesh {meshIndex}");
                AssertVertices(
                    redoVertices[meshIndex],
                    initialVertices[meshIndex],
                    $"{message} Redo mesh {meshIndex}");
            }
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

        static ISelectionState CreateEditSelection(
            GeometrySelectionMode selectionMode,
            MeshObject mesh)
        {
            var selectable = new TestSelectableNode { Geometry = mesh };
            return selectionMode switch
            {
                GeometrySelectionMode.Vertex => new VertexSelectionState(selectable, 0)
                {
                    SelectedVertices = new List<int> { 0 },
                    VertexWeights = new List<float> { 1, 0, 0, 0 }
                },
                GeometrySelectionMode.Face => new FaceSelectionState
                {
                    RenderObject = selectable,
                    SelectedFaces = new List<int> { 0 }
                },
                GeometrySelectionMode.Edge => new EdgeSelectionState
                {
                    RenderObject = selectable,
                    SelectedEdges = new HashSet<(int, int)> { (0, 1) }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(selectionMode))
            };
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
                    CreateVertex(new Vector3(1.5f, 0, 0)),
                    CreateVertex(new Vector3(2, 0, 0), nonUnitBasis: true)
                },
                IndexArray = new ushort[] { 0, 1, 2, 1, 4, 2 }
            };
            mesh.BuildBoundingBox();
            return mesh;
        }

        static MeshObject CreatePivotTenMesh(bool collapsedX)
        {
            var xCoordinates = collapsedX
                ? new[] { 10.0f, 10.0f, 10.0f, 10.0f }
                : new[] { 11.0f, 9.0f, 10.0f, 10.5f };
            var mesh = new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
            {
                VertexArray = new[]
                {
                    CreateVertex(new Vector3(xCoordinates[0], 0, 0)),
                    CreateVertex(new Vector3(xCoordinates[1], 1, 0)),
                    CreateVertex(new Vector3(xCoordinates[2], -1, 1)),
                    CreateVertex(new Vector3(xCoordinates[3], 0, -1))
                },
                IndexArray = new ushort[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.BuildBoundingBox();
            return mesh;
        }

        static MeshObject CreateLargeCoordinateMesh(int nonUnitBasisVertex = -1)
        {
            var mesh = new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
            {
                VertexArray = new[]
                {
                    CreateVertex(new Vector3(100.0f, 20.0f, 0.2f), nonUnitBasisVertex == 0),
                    CreateVertex(new Vector3(99.0f, -10.0f, -0.4f), nonUnitBasisVertex == 1),
                    CreateVertex(new Vector3(100.5f, 5.0f, 1.5f), nonUnitBasisVertex == 2),
                    CreateVertex(new Vector3(101.0f, -3.0f, -1.1f), nonUnitBasisVertex == 3)
                },
                IndexArray = new ushort[] { 0, 1, 2, 0, 2, 3 }
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

        static Vector3 ScaleBasisPreservingMagnitude(Vector3 basis, Vector3 scale)
        {
            var transformedDirection = new Vector3(
                basis.X / scale.X,
                basis.Y / scale.Y,
                basis.Z / scale.Z);
            return Vector3.Normalize(transformedDirection) * basis.Length();
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
