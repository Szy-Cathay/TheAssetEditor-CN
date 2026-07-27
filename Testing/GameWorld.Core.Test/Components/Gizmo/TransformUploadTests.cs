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

namespace Testing.GameWorld.Core.Components.Gizmo;

public class TransformUploadTests
{
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
            new TransformGizmoWrapper(commandFactory, meshes.ToList(), selection));
    }

    private static MeshObject CreateMesh(
        TestGeometryGraphicsContextFactory factory,
        float xOffset = 0.0f)
    {
        var graphics = (TestGraphicsCardGeometry)factory.Create();
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
        Assert.That(mesh.GetGeometryContext(), Is.SameAs(graphics));
        graphics.ResetRebuildCounts();
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

        public DirectContext(TransformGizmoWrapper wrapper)
        {
            Wrapper = wrapper;
        }

        public void Dispose()
        {
            if (Wrapper.IsTransformActive)
                Wrapper.CancelTransform();
            Wrapper.Dispose();
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
