using Editors.KitbasherEditor.Components;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

public partial class RenderEngineSelectionMaskOffscreenTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void EditElements_DenseSelectedVerticesRemainVisibleWhenZoomedOut(bool animated)
    {
        using var scene = new ElementOverlayScene(animated, dense: true);
        var selection = new VertexSelectionState(scene.Node, 0);
        selection.SetSelection(Enumerable.Range(0, scene.Mesh.VertexCount()));
        selection.ActiveVertex = null;
        scene.Prepare(selection);
        var vertices = scene.Items.OfType<VertexRenderItem>().Single();
        var pixels = scene.DrawItem(vertices, Matrix.CreateScale(0.02f));
        Assert.That(pixels.Count(IsSelectedOrange), Is.GreaterThan(0),
            "Zooming out must not make an existing vertex selection invisible.");
        Assert.That(selection.SelectedVertices, Has.Count.EqualTo(81));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void EditElements_FaceSelectionHasOrangeEdgesAndWhiteActiveBorder(bool animated, bool xray)
    {
        using var scene = new ElementOverlayScene(animated, xray: xray);
        var faces = new FaceSelectionState { RenderObject = scene.Node };
        faces.ModifySelection([0, 3], onlyRemove: false);
        var pixels = scene.Draw(faces);
        Assert.Multiple(() =>
        {
            Assert.That(pixels.Count(IsOrange), Is.GreaterThan(0),
                "Selected face edges need a clear orange boundary, not only translucent fill.");
            Assert.That(pixels.Count(pixel => pixel.R > 220 && pixel.G > 220 && pixel.B > 220),
                Is.GreaterThan(0), "The active face needs an opaque white border.");
            Assert.That(faces.SelectedFaces, Is.EqualTo(new[] { 0, 3 }));
            Assert.That(faces.ActiveFace, Is.EqualTo(3));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void EditElements_CompleteFaceFillMatchesAcrossSelectionModes(bool animated, bool xray)
    {
        using var scene = new ElementOverlayScene(animated, xray: xray);
        var faces = new FaceSelectionState { RenderObject = scene.Node, SelectedFaces = [0] };
        var facePixels = scene.Draw(faces);
        var vertices = new VertexSelectionState(scene.Node, 0);
        vertices.SetSelection([0, 1, 2]);
        vertices.ActiveVertex = null;
        var vertexPixels = scene.Draw(vertices);
        var edges = new EdgeSelectionState
        {
            RenderObject = scene.Node,
            SelectedEdges = [(0, 1), (0, 2), (1, 2)]
        };
        var edgePixels = scene.Draw(edges);
        var interior = ElementOverlayScene.Size / 2 * ElementOverlayScene.Size + 38;
        Assert.Multiple(() =>
        {
            Assert.That(facePixels[interior].R, Is.GreaterThan(facePixels[interior].B + 20));
            Assert.That(vertexPixels[interior], Is.EqualTo(facePixels[interior]),
                "Selecting all three vertices must show the same face fill as face mode.");
            Assert.That(edgePixels[interior], Is.EqualTo(facePixels[interior]),
                "Selecting all three edges must show the same face fill as face mode.");
            Assert.That(vertices.SelectedVertices, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(edges.SelectedEdges, Has.Count.EqualTo(3));
        });
    }

    [TestCase(GeometrySelectionMode.Vertex)]
    [TestCase(GeometrySelectionMode.Edge)]
    public void EditElements_IncompleteSelectionDoesNotFillFace(GeometrySelectionMode mode)
    {
        using var scene = new ElementOverlayScene(animated: false);
        ISelectionState selection;
        if (mode == GeometrySelectionMode.Vertex)
        {
            var vertices = new VertexSelectionState(scene.Node, 0);
            vertices.SetSelection([0, 1]);
            selection = vertices;
        }
        else
        {
            selection = new EdgeSelectionState
            {
                RenderObject = scene.Node,
                SelectedEdges = [(0, 1), (0, 2)]
            };
        }
        var pixels = scene.Draw(selection);
        Assert.That(pixels[ElementOverlayScene.Size / 2 * ElementOverlayScene.Size + 38],
            Is.EqualTo(Color.Gray), "An incomplete triangle must not look like a selected face.");
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void EditElements_ActiveVertexStaysWhiteAboveCompletedFaceFill(bool animated, bool xray)
    {
        using var scene = new ElementOverlayScene(animated, xray: xray);
        var vertices = new VertexSelectionState(scene.Node, 0);
        vertices.SetSelection([0, 1, 2]);
        var pixels = scene.Draw(vertices);
        Assert.That(pixels.Count(pixel => pixel.R > 220 && pixel.G > 220 && pixel.B > 220),
            Is.GreaterThan(0), "The derived face fill must not cover or tint the active vertex.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EditElements_WireframeKeepsFaceCentersWithoutXray(bool animated)
    {
        using var scene = new ElementOverlayScene(animated);
        scene.Renderer.ShadingMode = ViewportShadingMode.Wireframe;
        var faces = new FaceSelectionState { RenderObject = scene.Node };
        scene.Prepare(faces);
        var centers = scene.Items.OfType<VertexRenderItem>().SingleOrDefault();
        Assert.That(centers, Is.Not.Null, "Wireframe face centers must not depend on X-Ray.");
        var pixels = scene.DrawItem(centers!, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(pixels.Count(pixel => pixel.A > 0), Is.GreaterThan(0));
            Assert.That(faces.SelectedFaces, Is.Empty);
            Assert.That(scene.Settings.IsXRay, Is.False);
        });
    }

    [Test]
    public void EditElements_FaceFillRefreshesWhenTopologyChangesWithoutSelectionChange()
    {
        using var scene = new ElementOverlayScene(animated: false);
        var vertices = new VertexSelectionState(scene.Node, 0);
        vertices.SetSelection([0, 1, 2]);
        var interior = ElementOverlayScene.Size / 2 * ElementOverlayScene.Size + 38;
        var before = scene.Draw(vertices);
        Assert.That(before[interior].R, Is.GreaterThan(before[interior].B + 20));
        scene.Mesh.IndexArray = [0, 1, 3, 0, 3, 2];
        scene.Mesh.RebuildIndexBuffer();
        var after = scene.Draw();
        Assert.That(after[interior], Is.EqualTo(Color.Gray));
        Assert.That(vertices.SelectedVertices, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    private sealed class ElementOverlayScene : IDisposable
    {
        public const int Size = 128;
        private readonly WpfGameMock _game = new();
        private readonly ScopedResourceLibrary _scoped;
        private readonly SelectionManager _selection;
        private readonly KitbashSelectionOverlayComponent _overlay;
        private readonly SolidMeshRenderItem _surface;
        private readonly RenderTarget2D _target;
        public GraphicsDevice Device => _game.GraphicsDevice;
        public MeshObject Mesh { get; }
        public Rmv2MeshNode Node { get; }
        public RenderEngineComponent Renderer { get; }
        public KitbashSelectionSettings Settings { get; } = new();
        public IEnumerable<IRenderItem> Items => Enum.GetValues<RenderBuckedId>()
            .SelectMany(bucket => GetRenderItems(Renderer, bucket));

        public ElementOverlayScene(bool animated, bool dense = false, bool xray = false)
        {
            var resolver = Mock.Of<IDeviceResolver>(value => value.Device == Device);
            var camera = new ArcBallCamera(resolver, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
            camera.Initialize();
            var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
            resources.Initialize(Device, _game.Content);
            var events = Mock.Of<IEventHub>();
            _scoped = new ScopedResourceLibrary(resources, events, Mock.Of<IStandardDialogs>());
            Renderer = new RenderEngineComponent(_game, resources, camera, resolver,
                new ApplicationSettingsService(), new SceneRenderParametersStore(), events,
                new GridComponent(camera, resources, resolver) { ShowGrid = false });
            Renderer.Initialize();
            _target = new RenderTarget2D(Device, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24);
            Device.SetRenderTarget(_target);
            Renderer.Draw(new GameTime());
            Device.SetRenderTarget(null);
            _selection = new SelectionManager(events);
            Settings.IsXRay = xray;
            _overlay = new KitbashSelectionOverlayComponent(_selection, Renderer, _scoped, resolver, Settings);
            _overlay.Initialize();
            Mesh = CreateMesh(Device, animated);
            if (dense)
            {
                Mesh.VertexArray = Enumerable.Range(0, 81)
                    .Select(index => CreateVertex((index % 9 - 4) * 0.2f, (index / 9 - 4) * 0.2f))
                    .ToArray();
                Mesh.IndexArray = [0, 8, 72, 72, 8, 80];
                Mesh.BuildBoundingBox();
                Mesh.RebuildIndexBuffer();
                Mesh.RebuildVertexBuffer();
            }
            var material = new Mock<IRmvMaterial>();
            material.SetupGet(value => value.ModelName).Returns("element-overlay-test");
            material.SetupGet(value => value.PivotPoint).Returns(Vector3.Zero);
            Node = new Rmv2MeshNode(Mesh, material.Object, null!,
                animated ? CreateAnimationPlayer() : new AnimationPlayer { IsEnabled = false });
            _surface = new SolidMeshRenderItem(Device);
        }

        public void Prepare(ISelectionState? selection = null)
        {
            Renderer.Update(new GameTime());
            if (selection != null) _selection.SetState(selection);
            Renderer.AddRenderItem(RenderBuckedId.Normal, _surface);
            _overlay.Draw(new GameTime());
        }

        public Color[] Draw(ISelectionState? selection = null)
        {
            Prepare(selection);
            BeginDraw();
            InvokeRender3DObjects(Renderer);
            return ReadPixels();
        }

        public Color[] DrawItem(IRenderItem item, Matrix projection)
        {
            BeginDraw();
            item.Draw(Device, new CommonShaderParameters(Matrix.Identity, projection,
                Vector3.Backward, Vector3.Forward, 0, 0, 0, 1, Vector3.One, [], Size, Size),
                RenderingTechnique.Normal);
            return ReadPixels();
        }

        private void BeginDraw()
        {
            Device.SetRenderTarget(_target);
            Device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            Device.BlendState = BlendState.Opaque;
            Device.DepthStencilState = DepthStencilState.Default;
            Device.RasterizerState = RasterizerState.CullNone;
        }

        private Color[] ReadPixels()
        {
            Device.SetRenderTarget(null);
            var pixels = new Color[Size * Size];
            _target.GetData(pixels);
            return pixels;
        }

        public void Dispose()
        {
            Device.SetRenderTarget(null);
            _overlay.Dispose();
            _selection.Dispose();
            _target.Dispose();
            _surface.Dispose();
            Mesh.Dispose();
            Renderer.Dispose();
            _scoped.Dispose();
        }
    }
}
