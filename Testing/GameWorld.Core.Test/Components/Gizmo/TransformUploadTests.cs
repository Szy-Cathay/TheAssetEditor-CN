using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Test.TestUtility;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.Core.Events;
using Test.TestingUtility.Shared;

namespace Testing.GameWorld.Core.Components.Gizmo;

[Apartment(ApartmentState.STA)]
public class TransformUploadTests
{
    private static IEnumerable<TestCaseData> ModalSelectionModeCases
    {
        get
        {
            foreach (var transformMode in new[]
                     {
                         GizmoMode.Translate,
                         GizmoMode.Rotate,
                         GizmoMode.NonUniformScale
                     })
            {
                foreach (var selectionMode in new[]
                         {
                             GeometrySelectionMode.Object,
                             GeometrySelectionMode.Vertex,
                             GeometrySelectionMode.Face,
                             GeometrySelectionMode.Edge
                         })
                {
                    yield return new TestCaseData(transformMode, selectionMode)
                        .SetName(
                            $"Modal_{selectionMode}_{transformMode}_UploadsFinalStateOnce");
                }
            }
        }
    }

    [TestCaseSource(nameof(ModalSelectionModeCases))]
    public void ModalFrame_UploadsFinalStateOnce(
        GizmoMode transformMode,
        GeometrySelectionMode selectionMode)
    {
        using var context = CreateModalContext(selectionMode);
        var baseline = context.Mesh.VertexArray.ToArray();
        context.Component.Gizmo.StartModalTransform(transformMode);

        context.SetMousePosition(GetModalMousePosition(transformMode, 20.0f));
        context.Component.Update(new GameTime());
        Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(baseline));
        context.Graphics.ResetRebuildCounts();

        context.SetMousePosition(GetModalMousePosition(transformMode, 40.0f));
        context.Component.Update(new GameTime());

        if (selectionMode == GeometrySelectionMode.Object)
        {
            Assert.Multiple(() =>
            {
                Assert.That(context.Graphics.VertexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(context.Graphics.PartialUploads, Is.Empty);
            });
        }
        else
        {
            AssertEditUpload(
                context.Graphics,
                GetExpectedEditUpload(selectionMode));
        }
        AssertGpuMatchesCpu(new[] { context.Graphics }, context.Mesh);
    }

    [TestCase(GeometrySelectionMode.Object)]
    [TestCase(GeometrySelectionMode.Vertex)]
    public void ModalReturnToOrigin_UploadsPreviousPreviewToBaselineOnce(
        GeometrySelectionMode selectionMode)
    {
        using var context = CreateModalContext(selectionMode);
        var baseline = context.Mesh.VertexArray.ToArray();
        context.Component.Gizmo.StartModalTransform(GizmoMode.Translate);
        context.SetMousePosition(
            GetModalMousePosition(GizmoMode.Translate, 40.0f));
        context.Component.Update(new GameTime());
        context.Graphics.ResetRebuildCounts();

        context.SetMousePosition(
            GetModalMousePosition(GizmoMode.Translate, 0.0f));
        context.Component.Update(new GameTime());

        Assert.That(context.Mesh.VertexArray, Is.EqualTo(baseline));
        if (selectionMode == GeometrySelectionMode.Object)
        {
            Assert.Multiple(() =>
            {
                Assert.That(context.Graphics.VertexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(context.Graphics.PartialUploads, Is.Empty);
            });
        }
        else
        {
            AssertEditUpload(
                context.Graphics,
                GetExpectedEditUpload(selectionMode));
        }
        AssertGpuMatchesCpu(new[] { context.Graphics }, context.Mesh);
    }

    [Test]
    public void ModalRotatePlaneConstraint_RestoresPreviousPreview()
    {
        using var context = CreateModalContext(GeometrySelectionMode.Vertex);
        var baseline = context.Mesh.VertexArray.ToArray();
        context.Component.Gizmo.StartModalTransform(GizmoMode.Rotate);
        context.SetMousePosition(
            GetModalMousePosition(GizmoMode.Rotate, 20.0f));
        context.Component.Update(new GameTime());
        context.Component.Gizmo.ActiveAxis = GizmoAxis.YZ;
        context.Graphics.ResetRebuildCounts();

        context.SetMousePosition(
            GetModalMousePosition(GizmoMode.Rotate, 40.0f));
        context.Component.Update(new GameTime());

        Assert.That(context.Mesh.VertexArray, Is.EqualTo(baseline));
        AssertEditUpload(context.Graphics, new PartialUpload(1, 6));
        AssertGpuMatchesCpu(new[] { context.Graphics }, context.Mesh);
    }

    [Test]
    public void DirectObjectPreview_UploadsEachMeshFullOnce()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var firstMesh = CreateMesh(factory, 0.0f);
        var secondMesh = CreateMesh(factory, 10.0f);
        var selection = new ObjectSelectionState();
        selection.ModifySelection(
            new[]
            {
                new TestSelectableNode { Geometry = firstMesh },
                new TestSelectableNode { Geometry = secondMesh }
            },
            onlyRemove: false);
        using var context = CreateContext(selection, firstMesh, secondMesh);

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        foreach (var meshContext in factory.CreatedContexts)
        {
            Assert.Multiple(() =>
            {
                Assert.That(meshContext.VertexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(meshContext.PartialUploads, Is.Empty);
            });
        }
        AssertGpuMatchesCpu(factory.CreatedContexts, firstMesh, secondMesh);
    }

    [Test]
    public void DirectVertexPreview_ConsumesResultRange()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 0.5f));
        using var context = CreateContext(selection, mesh);

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void DirectFacePreview_ConsumesResultRange()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var selection = new FaceSelectionState
        {
            RenderObject = new TestSelectableNode { Geometry = mesh },
            SelectedFaces = new List<int> { 0 }
        };
        using var context = CreateContext(selection, mesh);

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void DirectEdgePreview_ConsumesResultRange()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var selection = new EdgeSelectionState
        {
            RenderObject = new TestSelectableNode { Geometry = mesh },
            SelectedEdges = new HashSet<(int, int)> { (2, 7) }
        };
        using var context = CreateContext(selection, mesh);

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(2, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void DirectAllZeroWeights_DoesNotUpload()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var selection = CreateVertexSelection(mesh, (1, 0.0f));
        using var context = CreateContext(selection, mesh);

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        Assert.Multiple(() =>
        {
            Assert.That(factory.CreatedContexts[0].VertexBufferRebuildCount, Is.Zero);
            Assert.That(factory.CreatedContexts[0].PartialUploads, Is.Empty);
        });
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void GestureTarget_IsFrozenWhenLiveVertexWeightsChange()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var initialVertices = mesh.VertexArray.ToArray();
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(1.0f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        factory.CreatedContexts[0].ResetRebuildCounts();

        selection.VertexWeights[1] = 0.0f;
        selection.VertexWeights[2] = 1.0f;
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        Assert.Multiple(() =>
        {
            Assert.That(
                mesh.GetVertexById(1),
                Is.EqualTo(initialVertices[1].Position3() + new Vector3(1.5f, 0.0f, 0.0f)));
            Assert.That(mesh.GetVertexById(2), Is.EqualTo(initialVertices[2].Position3()));
            Assert.That(
                mesh.GetVertexById(6),
                Is.EqualTo(initialVertices[6].Position3() + new Vector3(1.5f, 0.0f, 0.0f)));
        });
        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);

        var displayedPreview = mesh.VertexArray.ToArray();
        context.Wrapper.CommitTransform(context.CommandExecutor);
        context.CommandExecutor.Undo();
        Assert.That(mesh.VertexArray, Is.EqualTo(initialVertices));
        context.CommandExecutor.Redo();
        Assert.That(mesh.VertexArray, Is.EqualTo(displayedPreview));
    }

    [Test]
    public void EditStandaloneRestore_UploadsDisplayedRangeAndKeepsBackup()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var baseline = mesh.VertexArray.ToArray();
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);
        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        factory.CreatedContexts[0].ResetRebuildCounts();

        context.Wrapper.RestoreInitialPreviewState();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.VertexArray, Is.EqualTo(baseline));
            Assert.That(context.Wrapper.HasBackup, Is.True);
        });
        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void EditRejectedReplacement_UploadsBaselineRangeAndClearsPreview()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var baseline = mesh.VertexArray.ToArray();
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);
        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        factory.CreatedContexts[0].ResetRebuildCounts();

        context.Wrapper.ReplaceInitialPreview(
            ModalPreviewReplacement.Scale(
                new Vector3(-1.00001f, 0.0f, 0.0f),
                PivotType.ObjectCenter));

        Assert.Multiple(() =>
        {
            Assert.That(mesh.VertexArray, Is.EqualTo(baseline));
            Assert.That(context.Wrapper.HasBackup, Is.True);
        });
        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);

        factory.CreatedContexts[0].ResetRebuildCounts();
        context.Wrapper.ReplaceInitialPreview(
            ModalPreviewReplacement.RestoreOnly(PivotType.ObjectCenter));
        Assert.Multiple(() =>
        {
            Assert.That(factory.CreatedContexts[0].VertexBufferRebuildCount, Is.Zero);
            Assert.That(factory.CreatedContexts[0].PartialUploads, Is.Empty);
        });
    }

    [Test]
    public void RejectedDirectEvent_LeavesDisplayedPreviewAndDoesNotUpload()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);
        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        var displayedPreview = mesh.VertexArray.ToArray();
        factory.CreatedContexts[0].ResetRebuildCounts();

        context.Wrapper.GizmoScaleEvent(
            new Vector3(-1.00001f, 0.0f, 0.0f),
            PivotType.ObjectCenter);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.VertexArray, Is.EqualTo(displayedPreview));
            Assert.That(factory.CreatedContexts[0].VertexBufferRebuildCount, Is.Zero);
            Assert.That(factory.CreatedContexts[0].PartialUploads, Is.Empty);
        });
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void FalloffChangeDuringGesture_AppliesOnlyToNextGesture()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var selection = new EdgeSelectionState
        {
            RenderObject = new TestSelectableNode { Geometry = mesh },
            SelectedEdges = new HashSet<(int, int)> { (2, 7) }
        };
        using var context = CreateContext(selection, mesh);
        context.Wrapper.SetFalloffDistance(1.5f);
        context.Wrapper.BeginTransform();
        context.Wrapper.SetFalloffDistance(0.0f);

        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 7));
        context.Wrapper.CancelTransform();
        factory.CreatedContexts[0].ResetRebuildCounts();

        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);

        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(2, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void PreviewThenCommit_DoesNotUploadAndReleasesFinalBounds()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var node = new TestSelectableNode { Geometry = mesh };
        var selection = new ObjectSelectionState();
        selection.ModifySelectionSingleObject(node, onlyRemove: false);
        using var context = CreateContext(selection, mesh);
        var initialBounds = mesh.BoundingBox;
        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        factory.CreatedContexts[0].ResetRebuildCounts();

        context.Wrapper.CommitTransform(context.CommandExecutor);

        Assert.Multiple(() =>
        {
            Assert.That(factory.CreatedContexts[0].VertexBufferRebuildCount, Is.Zero);
            Assert.That(factory.CreatedContexts[0].PartialUploads, Is.Empty);
            Assert.That(mesh.DeferBoundingBoxRebuild, Is.False);
            Assert.That(mesh.BoundingBox.Min.X, Is.EqualTo(initialBounds.Min.X + 0.5f));
            Assert.That(mesh.BoundingBox.Max.X, Is.EqualTo(initialBounds.Max.X + 0.5f));
            Assert.That(context.Wrapper.HasBackup, Is.False);
        });
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void PreviewThenCancel_UploadsBaselineRangeAndReleasesBaselineBounds()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var baseline = mesh.VertexArray.ToArray();
        var initialBounds = mesh.BoundingBox;
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);
        context.Wrapper.BeginTransform();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.5f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        factory.CreatedContexts[0].ResetRebuildCounts();

        context.Wrapper.CancelTransform();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.VertexArray, Is.EqualTo(baseline));
            Assert.That(mesh.BoundingBox, Is.EqualTo(initialBounds));
            Assert.That(mesh.DeferBoundingBoxRebuild, Is.False);
            Assert.That(context.Wrapper.HasBackup, Is.False);
        });
        AssertEditUpload(factory.CreatedContexts[0], new PartialUpload(1, 6));
        AssertGpuMatchesCpu(factory.CreatedContexts, mesh);
    }

    [Test]
    public void PreviewUploadFailure_RecoversBaselineAndPreservesPrimaryError()
    {
        var graphics = new FailingGraphicsCardGeometry();
        var mesh = CreateMesh(graphics);
        var baseline = mesh.VertexArray.ToArray();
        graphics.ResetRebuildCounts();
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);
        context.Wrapper.BeginTransform();
        var expectedException =
            new InvalidOperationException("preview upload failed");
        graphics.NextPartialUploadException = expectedException;

        var actualException = Assert.Throws<InvalidOperationException>(
            () => context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.5f, 0.0f, 0.0f),
                PivotType.WorldOrigin));

        Assert.Multiple(() =>
        {
            Assert.That(actualException, Is.SameAs(expectedException));
            Assert.That(mesh.VertexArray, Is.EqualTo(baseline));
            Assert.That(graphics.UploadedVertexArray, Is.EqualTo(baseline));
            Assert.That(graphics.VertexBufferRebuildCount, Is.EqualTo(1));
            Assert.That(graphics.IndexBufferRebuildCount, Is.EqualTo(1));
            Assert.That(context.Wrapper.HasBackup, Is.True);
            Assert.That(mesh.DeferBoundingBoxRebuild, Is.True);
        });

        graphics.ResetRebuildCounts();
        context.Wrapper.GizmoTranslateEvent(
            new Vector3(0.25f, 0.0f, 0.0f),
            PivotType.WorldOrigin);
        Assert.That(graphics.PartialUploads, Is.EqualTo(
            new[] { new PartialUpload(1, 6) }));
        Assert.That(graphics.UploadedVertexArray, Is.EqualTo(mesh.VertexArray));
    }

    [Test]
    public void RecoveryUploadFailure_FaultsCommitAndCancelRetriesBaseline()
    {
        var graphics = new FailingGraphicsCardGeometry();
        var mesh = CreateMesh(graphics);
        var baseline = mesh.VertexArray.ToArray();
        graphics.ResetRebuildCounts();
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        using var context = CreateContext(selection, mesh);
        context.Wrapper.BeginTransform();
        var primaryException =
            new InvalidOperationException("preview upload failed");
        graphics.NextPartialUploadException = primaryException;
        graphics.FullUploadFailuresRemaining = 1;
        graphics.FullUploadException =
            new InvalidOperationException("recovery upload failed");

        var actualException = Assert.Throws<InvalidOperationException>(
            () => context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.5f, 0.0f, 0.0f),
                PivotType.WorldOrigin));
        var commitException = Assert.Throws<InvalidOperationException>(
            () => context.Wrapper.CommitTransform(context.CommandExecutor));

        Assert.Multiple(() =>
        {
            Assert.That(actualException, Is.SameAs(primaryException));
            Assert.That(commitException?.Message, Does.Contain("not synchronized"));
            Assert.That(context.Wrapper.HasBackup, Is.True);
            Assert.That(mesh.DeferBoundingBoxRebuild, Is.True);
        });

        graphics.ResetRebuildCounts();
        context.Wrapper.CancelTransform();

        Assert.Multiple(() =>
        {
            Assert.That(graphics.VertexBufferRebuildCount, Is.EqualTo(1));
            Assert.That(graphics.UploadedVertexArray, Is.EqualTo(baseline));
            Assert.That(mesh.VertexArray, Is.EqualTo(baseline));
            Assert.That(context.Wrapper.HasBackup, Is.False);
            Assert.That(mesh.DeferBoundingBoxRebuild, Is.False);
        });
    }

    [Test]
    public void ModalUploadFailure_AbortsInteractionAndPreservesPrimaryError()
    {
        var graphics = new FailingGraphicsCardGeometry();
        var mesh = CreateMesh(graphics);
        var baseline = mesh.VertexArray.ToArray();
        graphics.ResetRebuildCounts();
        var selection = CreateVertexSelection(mesh, (1, 1.0f), (6, 1.0f));
        var harness = CreateModalHarness(selection);
        using var context = new FailingModalContext(
            harness.Component,
            mesh,
            graphics,
            harness.Mouse,
            harness.SetMousePosition);
        var wrapper =
            (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
        context.Component.Gizmo.StartModalTransform(GizmoMode.Translate);
        context.SetMousePosition(
            GetModalMousePosition(GizmoMode.Translate, 20.0f));
        context.Component.Update(new GameTime());
        graphics.ResetRebuildCounts();
        var expectedException =
            new InvalidOperationException("modal preview upload failed");
        graphics.NextPartialUploadException = expectedException;

        context.SetMousePosition(
            GetModalMousePosition(GizmoMode.Translate, 40.0f));
        var actualException = Assert.Throws<InvalidOperationException>(
            () => context.Component.Update(new GameTime()));

        Assert.Multiple(() =>
        {
            Assert.That(actualException, Is.SameAs(expectedException));
            Assert.That(mesh.VertexArray, Is.EqualTo(baseline));
            Assert.That(graphics.UploadedVertexArray, Is.EqualTo(baseline));
            Assert.That(wrapper.HasBackup, Is.False);
            Assert.That(mesh.DeferBoundingBoxRebuild, Is.False);
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
            Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        });
        context.Mouse.Verify(component => component.ClearStates(), Times.Once);
    }

    private static VertexSelectionState CreateVertexSelection(
        MeshObject mesh,
        params (int Index, float Weight)[] weightedVertices)
    {
        var selection = new VertexSelectionState(
            new TestSelectableNode { Geometry = mesh },
            vertexSelectionFallof: 0)
        {
            SelectedVertices = weightedVertices.Select(item => item.Index).ToList(),
            VertexWeights = Enumerable.Repeat(0.0f, mesh.VertexCount()).ToList()
        };
        foreach (var (index, weight) in weightedVertices)
            selection.VertexWeights[index] = weight;
        return selection;
    }

    private static ModalContext CreateModalContext(
        GeometrySelectionMode selectionMode)
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var mesh = CreateMesh(factory);
        var node = new TestSelectableNode { Geometry = mesh };
        var selection = CreateModalSelection(selectionMode, mesh, node);
        var harness = CreateModalHarness(selection);
        return new ModalContext(
            harness.Component,
            mesh,
            factory.CreatedContexts[0],
            harness.SetMousePosition);
    }

    private static ModalHarness CreateModalHarness(
        ISelectionState selection)
    {
        var eventHub = new TestEventHub();
        var selectionManager = new SelectionManager(
            eventHub,
            null,
            null,
            null);
        var commandExecutor = new CommandExecutor(eventHub);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
            .Returns(() => new TransformVertexCommand(selectionManager));
        var commandFactory = new CommandFactory(
            serviceProvider.Object,
            commandExecutor);

        var mousePosition = new Vector2(960.0f, 540.0f);
        var mouse = new Mock<IMouseComponent>();
        mouse.SetupProperty(component => component.MouseOwner);
        mouse.Setup(component => component.Position())
            .Returns(() => mousePosition);
        mouse.Setup(component => component.GetScreenSize())
            .Returns(new Vector2(1920.0f, 1080.0f));
        var keyboard = new Mock<IKeyboardComponent>();
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(new WpfGameMock().GraphicsDevice);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            keyboard.Object,
            mouse.Object);
        camera.Initialize();
        var component = new GizmoComponent(
            eventHub,
            keyboard.Object,
            mouse.Object,
            camera,
            commandExecutor,
            null,
            deviceResolver.Object,
            commandFactory,
            selectionManager);
        component.Initialize();
        selectionManager.SetState(selection);

        Assert.That(component.Gizmo.Selection, Has.Count.EqualTo(1));
        return new ModalHarness(
            component,
            mouse,
            value => mousePosition = value);
    }

    private static ISelectionState CreateModalSelection(
        GeometrySelectionMode selectionMode,
        MeshObject mesh,
        TestSelectableNode node)
    {
        switch (selectionMode)
        {
            case GeometrySelectionMode.Object:
                var objectSelection = new ObjectSelectionState();
                objectSelection.ModifySelectionSingleObject(
                    node,
                    onlyRemove: false);
                return objectSelection;
            case GeometrySelectionMode.Vertex:
                return CreateVertexSelection(mesh, (1, 1.0f), (6, 0.5f));
            case GeometrySelectionMode.Face:
                return new FaceSelectionState
                {
                    RenderObject = node,
                    SelectedFaces = new List<int> { 0 }
                };
            case GeometrySelectionMode.Edge:
                return new EdgeSelectionState
                {
                    RenderObject = node,
                    SelectedEdges = new HashSet<(int, int)> { (2, 7) }
                };
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(selectionMode),
                    selectionMode,
                    null);
        }
    }

    private static Vector2 GetModalMousePosition(
        GizmoMode transformMode,
        float displacement)
    {
        return transformMode == GizmoMode.NonUniformScale
            ? new Vector2(960.0f, 540.0f + displacement)
            : new Vector2(960.0f + displacement, 540.0f);
    }

    private static PartialUpload GetExpectedEditUpload(
        GeometrySelectionMode selectionMode)
    {
        return selectionMode == GeometrySelectionMode.Edge
            ? new PartialUpload(2, 6)
            : new PartialUpload(1, 6);
    }

    private static DirectContext CreateContext(
        ISelectionState selection,
        params MeshObject[] meshes)
    {
        var eventHub = new Mock<IEventHub>();
        var selectionManager = new SelectionManager(eventHub.Object, null, null, null);
        selectionManager.SetState(selection);
        var commandExecutor = new CommandExecutor(eventHub.Object);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
            .Returns(() => new TransformVertexCommand(selectionManager));
        var commandFactory = new CommandFactory(serviceProvider.Object, commandExecutor);
        return new DirectContext(
            new TransformGizmoWrapper(commandFactory, meshes.ToList(), selection),
            commandExecutor);
    }

    private static MeshObject CreateMesh(
        TestGeometryGraphicsContextFactory factory,
        float xOffset = 0.0f)
    {
        var graphics = (TestGraphicsCardGeometry)factory.Create();
        var mesh = CreateMesh(graphics, xOffset);
        Assert.That(mesh.GetGeometryContext(), Is.SameAs(graphics));
        graphics.ResetRebuildCounts();
        return mesh;
    }

    private static MeshObject CreateMesh(
        IGraphicsCardGeometry graphics,
        float xOffset = 0.0f)
    {
        var mesh = new MeshObject(graphics, string.Empty)
        {
            VertexArray = Enumerable.Range(0, 8)
                .Select(index => CreateVertex(new Vector3(xOffset + index - 3.0f, index % 2, 0.0f)))
                .ToArray(),
            IndexArray = new ushort[]
            {
                1, 4, 6,
                0, 2, 7,
                1, 6, 7
            }
        };
        mesh.RebuildVertexBuffer();
        mesh.RebuildIndexBuffer();
        return mesh;
    }

    private static VertexPositionNormalTextureCustom CreateVertex(Vector3 position)
    {
        return new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(position, 1.0f),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            BiNormal = Vector3.UnitY
        };
    }

    private static void AssertEditUpload(
        TestGraphicsCardGeometry graphics,
        PartialUpload expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(graphics.VertexBufferRebuildCount, Is.Zero);
            Assert.That(graphics.PartialUploads, Is.EqualTo(new[] { expected }));
        });
    }

    private static void AssertGpuMatchesCpu(
        IReadOnlyList<TestGraphicsCardGeometry> graphics,
        params MeshObject[] meshes)
    {
        Assert.That(graphics, Has.Count.EqualTo(meshes.Length));
        for (var i = 0; i < meshes.Length; i++)
            Assert.That(graphics[i].UploadedVertexArray, Is.EqualTo(meshes[i].VertexArray));
    }

    private sealed class DirectContext : IDisposable
    {
        public TransformGizmoWrapper Wrapper { get; }
        public CommandExecutor CommandExecutor { get; }

        public DirectContext(
            TransformGizmoWrapper wrapper,
            CommandExecutor commandExecutor)
        {
            Wrapper = wrapper;
            CommandExecutor = commandExecutor;
        }

        public void Dispose()
        {
            if (Wrapper.IsTransformActive)
                Wrapper.CancelTransform();
            Wrapper.Dispose();
        }
    }

    private sealed record ModalContext(
        GizmoComponent Component,
        MeshObject Mesh,
        TestGraphicsCardGeometry Graphics,
        Action<Vector2> SetMousePosition) : IDisposable
    {
        public void Dispose() => Component.Dispose();
    }

    private sealed record ModalHarness(
        GizmoComponent Component,
        Mock<IMouseComponent> Mouse,
        Action<Vector2> SetMousePosition);

    private sealed record FailingModalContext(
        GizmoComponent Component,
        MeshObject Mesh,
        FailingGraphicsCardGeometry Graphics,
        Mock<IMouseComponent> Mouse,
        Action<Vector2> SetMousePosition) : IDisposable
    {
        public void Dispose() => Component.Dispose();
    }

    private sealed class TestEventHub : IEventHub
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        public void PublishGlobalEvent<T>(T value) => Publish(value);

        public void Publish<T>(T value)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
                return;

            foreach (var subscriber in subscribers)
                ((Action<T>)subscriber)(value);
        }

        public void Register<T>(object owner, Action<T> action)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
            {
                subscribers = new List<Delegate>();
                _subscribers.Add(typeof(T), subscribers);
            }

            subscribers.Add(action);
        }

        public void UnRegister(object owner)
        {
        }
    }

    private sealed class FailingGraphicsCardGeometry : IGraphicsCardGeometry
    {
        private readonly List<PartialUpload> _partialUploads = new();

        public IndexBuffer IndexBuffer => null!;
        public VertexBuffer VertexBuffer => null!;
        public int IndexBufferRebuildCount { get; private set; }
        public int VertexBufferRebuildCount { get; private set; }
        public VertexPositionNormalTextureCustom[] UploadedVertexArray { get; private set; }
        public IReadOnlyList<PartialUpload> PartialUploads => _partialUploads;
        public Exception NextPartialUploadException { get; set; }
        public Exception FullUploadException { get; set; }
        public int FullUploadFailuresRemaining { get; set; }

        public void RebuildIndexBuffer(ushort[] indexList)
        {
            IndexBufferRebuildCount++;
        }

        public void RebuildVertexBuffer(
            VertexPositionNormalTextureCustom[] vertexArray,
            VertexDeclaration vertexDeclaration)
        {
            VertexBufferRebuildCount++;
            if (FullUploadFailuresRemaining > 0)
            {
                FullUploadFailuresRemaining--;
                throw FullUploadException ??
                      new InvalidOperationException("Full upload failed.");
            }

            UploadedVertexArray = vertexArray.ToArray();
        }

        public void RebuildVertexBufferPartial(
            VertexPositionNormalTextureCustom[] vertexArray,
            int startIndex,
            int count,
            VertexDeclaration vertexDeclaration,
            int vertexStride)
        {
            _partialUploads.Add(new PartialUpload(startIndex, count));
            if (NextPartialUploadException != null)
            {
                var exception = NextPartialUploadException;
                NextPartialUploadException = null;
                throw exception;
            }

            UploadedVertexArray ??= vertexArray.ToArray();
            Array.Copy(
                vertexArray,
                startIndex,
                UploadedVertexArray,
                startIndex,
                count);
        }

        public void ResetRebuildCounts()
        {
            IndexBufferRebuildCount = 0;
            VertexBufferRebuildCount = 0;
            _partialUploads.Clear();
        }

        public IGraphicsCardGeometry Clone() => this;

        public void Dispose()
        {
        }
    }

    private sealed class TestSelectableNode : SceneNode, ISelectable, ITransformable
    {
        public MeshObject Geometry { get; set; } = null!;
        public bool IsSelectable { get; set; } = true;
        public Vector3 Position { get; set; }
        public Vector3 Scale { get; set; } = Vector3.One;
        public Quaternion Orientation { get; set; } = Quaternion.Identity;

        public Vector3 GetObjectCentre() => Geometry.MeshCenter;
        public override ISceneNode CreateCopyInstance() => new TestSelectableNode();
    }
}
