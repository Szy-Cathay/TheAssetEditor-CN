using System.Reflection;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
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
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public partial class RenderEngineSelectionMaskOffscreenTests
{
    [Test]
    public void DenseStaticOverlay_PrioritizesSelectedVertexEdgesPastRenderLimit()
    {
        const int maxEdges = 50000;
        const int selectedVertex = 50001;
        var indices = Enumerable.Range(0, 50004)
            .Select(index => (ushort)index)
            .ToArray();

        var edges = KitbashSelectionOverlayComponent.BuildEdges(
            indices,
            maxEdges,
            [selectedVertex]);

        Assert.Multiple(() =>
        {
            Assert.That(
                edges,
                Does.Contain((selectedVertex, selectedVertex + 1)));
            Assert.That(
                edges,
                Does.Contain((selectedVertex, selectedVertex + 2)));
            Assert.That(edges, Has.Length.EqualTo(maxEdges));
        });
    }

    [TestCase(false, ViewportShadingMode.Wireframe)]
    [TestCase(true, ViewportShadingMode.Wireframe)]
    [TestCase(false, ViewportShadingMode.MaterialPreview)]
    [TestCase(true, ViewportShadingMode.MaterialPreview)]
    [TestCase(false, ViewportShadingMode.Solid)]
    public void VertexMode_DoesNotUseWholeObjectOutline(
        bool animated,
        ViewportShadingMode shadingMode)
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var eventHub = new Mock<IEventHub>();
        using var scopedResources = new ScopedResourceLibrary(
            resources,
            eventHub.Object,
            Mock.Of<IStandardDialogs>());
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            eventHub.Object,
            new GridComponent(
                camera,
                resources,
                deviceResolver.Object)
            {
                ShowGrid = false
            })
        {
            ShadingMode = shadingMode
        };
        renderEngine.Initialize();
        var selectionManager = new SelectionManager(eventHub.Object);
        using var selectionOverlay =
            new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                scopedResources,
                deviceResolver.Object);
        selectionOverlay.Initialize();
        var mesh = CreateMesh(device, animated);
        var rmvMaterial = new Mock<IRmvMaterial>();
        rmvMaterial.SetupGet(value => value.ModelName)
            .Returns("test");
        rmvMaterial.SetupGet(value => value.PivotPoint)
            .Returns(Vector3.Zero);
        var node = new Rmv2MeshNode(
            mesh,
            rmvMaterial.Object,
            new SelectionOutlineCapabilityMaterial(
                scopedResources),
            animated
                ? CreateAnimationPlayer()
                : new AnimationPlayer { IsEnabled = false });
        using var sceneTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24,
            4,
            RenderTargetUsage.DiscardContents);
        using var maskTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            4,
            RenderTargetUsage.DiscardContents);
        var requestField = typeof(RenderEngineComponent).GetField(
            "_selectionOutlineRequested",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(requestField, Is.Not.Null);
        using var outlineFilter = new OutlineFilter();
        outlineFilter.Load(
            device,
            resources,
            new QuadRenderer(device));
        var objectSelection =
            selectionManager.GetState<ObjectSelectionState>();
        objectSelection.ModifySelectionSingleObject(
            node,
            onlyRemove: false);
        node.Render(renderEngine, Matrix.Identity);
        selectionOverlay.Draw(new GameTime());
        var objectRequestField = typeof(RenderEngineComponent).GetField(
            "_selectionOutlineRequested",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(renderEngine.ShadingSettings.WireframeObjectSelection, Is.True);
        Assert.That(
            objectRequestField?.GetValue(renderEngine),
            Is.True,
            "Object mode must retain its normal whole-object outline.");
        device.SetRenderTargets(
            new RenderTargetBinding(sceneTarget),
            new RenderTargetBinding(maskTarget));
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);
        outlineFilter.Draw(maskTarget, size, size);
        var objectOutlinePixels = new Color[size * size];
        outlineFilter.GetOutlineTarget().GetData(
            objectOutlinePixels);
        Assert.That(
            objectOutlinePixels.Count(pixel => pixel.A > 0),
            Is.GreaterThan(0),
            "Object mode must retain its visible GPU outline.");
        renderEngine.Update(new GameTime());
        var enterVertexMode =
            new GameWorld.Core.Commands.Object
                .ObjectSelectionModeCommand(selectionManager);
        enterVertexMode.Configure(
            node,
            GeometrySelectionMode.Vertex);
        enterVertexMode.Execute();
        var selectionOutlineField = typeof(Rmv2MeshNode).GetField(
            "_selectionOutlineEnabled",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var previewOutlineField = typeof(Rmv2MeshNode).GetField(
            "_previewOutlineEnabled",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Multiple(() =>
        {
            AssertEditSelectionIsEmpty(
                selectionManager.GetState(),
                GeometrySelectionMode.Vertex);
            Assert.That(selectionOutlineField?.GetValue(node), Is.False);
            Assert.That(previewOutlineField?.GetValue(node), Is.False);
        });
        for (var frame = 0; frame < 2; frame++)
        {
            node.Render(renderEngine, Matrix.Identity);
            selectionOverlay.Draw(new GameTime());
            Assert.That(renderEngine.ShadingSettings.WireframeObjectSelection, Is.False);
            device.SetRenderTargets(
                new RenderTargetBinding(sceneTarget),
                new RenderTargetBinding(maskTarget));
            device.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            InvokeRender3DObjects(renderEngine);
            device.SetRenderTarget(null);

            var scenePixels = new Color[size * size];
            sceneTarget.GetData(scenePixels);
            var outlineRequested =
                (bool)requestField!.GetValue(renderEngine)!;
            var outlinePixels = new Color[size * size];
            if (outlineRequested)
            {
                outlineFilter.Draw(maskTarget, size, size);
                outlineFilter.GetOutlineTarget().GetData(
                    outlinePixels);
            }

            var internalOutlinePixels = 0;
            for (var y = 27; y <= 36; y++)
            {
                for (var x = 20; x <= 43; x++)
                {
                    if (outlinePixels[y * size + x].A > 0)
                        internalOutlinePixels++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    scenePixels.Count(IsOrange),
                    Is.EqualTo(0),
                    $"Frame {frame}: empty edit selection must not directly colour topology orange.");
                Assert.That(
                    outlinePixels.Count(pixel => pixel.A > 0),
                    Is.EqualTo(0),
                    $"Frame {frame}: vertex mode must not use a whole-object outline in {shadingMode}.");
                Assert.That(
                    internalOutlinePixels,
                    Is.EqualTo(0),
                    $"Frame {frame}: empty edit selection must not turn internal wireframe topology into an orange selection outline.");
                Assert.That(
                    previewOutlineField?.GetValue(node),
                    Is.False);
            });

            if (frame == 0)
                renderEngine.Update(new GameTime());
        }

        selectionManager.Dispose();
        mesh.Dispose();
    }

    [Test]
    public void PreviewOutline_RemainsEnabledWithoutObjectSelection()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            Mock.Of<IEventHub>(),
            new GridComponent(
                camera,
                resources,
                deviceResolver.Object));
        renderEngine.Initialize();
        var mesh = CreateMesh(device, animated: false);
        var material = new Mock<IRmvMaterial>();
        material.SetupGet(value => value.ModelName).Returns("preview");
        material.SetupGet(value => value.PivotPoint).Returns(Vector3.Zero);
        var node = new Rmv2MeshNode(
            mesh,
            material.Object,
            new PreviewOutlineCapabilityMaterial(),
            new AnimationPlayer { IsEnabled = false });

        node.SetPreviewOutline(true);
        node.SetSelectionOutline(false);
        node.Render(renderEngine, Matrix.Identity);

        var renderItem = GetRenderItems(
                renderEngine,
                RenderBuckedId.Normal)
            .Single(item => item is GeometryRenderItem);
        var maskField = typeof(GeometryRenderItem).GetField(
            "_selectionMaskEnabled",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var requestField = typeof(RenderEngineComponent).GetField(
            "_selectionOutlineRequested",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Multiple(() =>
        {
            Assert.That(maskField?.GetValue(renderItem), Is.True);
            Assert.That(requestField?.GetValue(renderEngine), Is.True);
        });

        renderEngine.Update(new GameTime());
        node.Render(renderEngine, Matrix.Identity);
        Assert.That(
            requestField?.GetValue(renderEngine),
            Is.True,
            "Preview outline must request the outline pass again after each render update.");

        node.SetSelectionOutline(true);
        node.SetPreviewOutline(false);
        Assert.That(
            maskField?.GetValue(renderItem),
            Is.True,
            "Turning off the preview outline must not clear Kitbash selection.");
        node.SetSelectionOutline(false);
        Assert.That(maskField?.GetValue(renderItem), Is.False);

        mesh.Dispose();
    }

    [Test]
    public void GridBeforeSelectedGeometry_DoesNotCreateInternalSelectionOutline()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>())
        {
            ViewMatrixOverride = Matrix.CreateLookAt(
                new Vector3(0, 10, 0),
                Vector3.Zero,
                Vector3.Forward),
            ProjectionMatrixOverride = Matrix.CreateOrthographic(
                100,
                100,
                0.1f,
                100)
        };
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        using var grid = new GridComponent(
            camera,
            resources,
            deviceResolver.Object)
        {
            ShowGrid = true
        };
        grid.Initialize();
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            Mock.Of<IEventHub>(),
            grid);
        renderEngine.Initialize();
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            new SelectionMaskRenderItem(
                game.Content.Load<Effect>(
                    "Shaders\\Pbr\\SpecGloss\\SpecGloss_main"),
                device));
        using var sceneTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        using var maskTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);
        using var outlineFilter = new OutlineFilter();
        outlineFilter.Load(
            device,
            resources,
            new QuadRenderer(device));
        var cameraPosition = new Vector3(0, 10, 0);
        var parameters = new CommonShaderParameters(
            camera.ViewMatrix,
            camera.ProjectionMatrix,
            cameraPosition,
            Vector3.Down,
            0,
            0,
            0,
            1,
            Vector3.One,
            []);

        device.SetRenderTargets(
            new RenderTargetBinding(sceneTarget),
            new RenderTargetBinding(maskTarget));
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        grid.RenderGrid(device, parameters);
        device.DepthStencilState = DepthStencilState.Default;
        device.BlendState = BlendState.Opaque;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        outlineFilter.Draw(maskTarget, size, size);
        var outlinePixels = new Color[size * size];
        outlineFilter.GetOutlineTarget().GetData(outlinePixels);

        Assert.That(
            CountPixels(
                outlinePixels,
                size,
                8,
                56,
                8,
                56,
                IsOrange),
            Is.EqualTo(0),
            "The ground grid must not cut orange lines into selected geometry.");
    }

    [Test]
    public void Render3DObjects_ActiveEditElementsUseThirdVisualLayer()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var eventHub = new Mock<IEventHub>();
        using var scopedResources = new ScopedResourceLibrary(
            resources,
            eventHub.Object,
            Mock.Of<IStandardDialogs>());
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            eventHub.Object,
            new GridComponent(
                camera,
                resources,
                deviceResolver.Object));
        renderEngine.Initialize();
        var selectionManager = new SelectionManager(eventHub.Object);
        using var selectionOverlay =
            new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                scopedResources,
                deviceResolver.Object);
        selectionOverlay.Initialize();
        var mesh = CreateMesh(device, animated: false);
        var material = new Mock<IRmvMaterial>();
        material.SetupGet(value => value.ModelName).Returns("test");
        material.SetupGet(value => value.PivotPoint)
            .Returns(Vector3.Zero);
        var node = new Rmv2MeshNode(
            mesh,
            material.Object,
            null!,
            new AnimationPlayer { IsEnabled = false });
        var selection = new EdgeSelectionState
        {
            RenderObject = node
        };
        selection.ModifySelection(
            [(0, 1), (2, 3)],
            onlyRemove: false);
        selectionManager.SetState(selection);
        selectionOverlay.Draw(new GameTime());
        using var renderTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        renderTarget.GetData(pixels);
        Assert.Multiple(() =>
        {
            Assert.That(
                pixels.Count(IsOrange),
                Is.GreaterThan(0));
            Assert.That(
                pixels.Count(pixel =>
                    pixel.R > 220 &&
                    pixel.G > 220 &&
                    pixel.B > 220 &&
                    pixel.A > 0),
                Is.GreaterThan(0));
            Assert.That(
                GetRenderItems(
                    renderEngine,
                    RenderBuckedId.Selection),
                Has.Exactly(2)
                    .TypeOf<AnimatedWireframeRenderItem>());
        });

        renderEngine.Update(new GameTime());
        var faceSelection = new FaceSelectionState
        {
            RenderObject = node
        };
        faceSelection.ModifySelection(
            [0, 3],
            onlyRemove: false);
        selectionManager.SetState(faceSelection);
        selectionOverlay.Draw(new GameTime());

        Assert.That(
            GetRenderItems(
                renderEngine,
                RenderBuckedId.Selection),
            Has.Exactly(2)
                .TypeOf<AnimatedSelectionRenderItem>());

        selectionManager.Dispose();
        mesh.Dispose();
    }

    [TestCase(GeometrySelectionMode.Vertex)]
    [TestCase(GeometrySelectionMode.Edge)]
    [TestCase(GeometrySelectionMode.Face)]
    public void Wireframe_NonEmptyEditSelectionKeepsSelectedOverlay(
        GeometrySelectionMode mode)
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var eventHub = new Mock<IEventHub>();
        using var scopedResources = new ScopedResourceLibrary(
            resources,
            eventHub.Object,
            Mock.Of<IStandardDialogs>());
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            eventHub.Object,
            new GridComponent(
                camera,
                resources,
                deviceResolver.Object)
            {
                ShowGrid = false
            })
        {
            ShadingMode = ViewportShadingMode.Wireframe
        };
        renderEngine.Initialize();
        var selectionManager = new SelectionManager(eventHub.Object);
        using var selectionOverlay =
            new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                scopedResources,
                deviceResolver.Object);
        selectionOverlay.Initialize();
        var mesh = CreateMesh(device, animated: false);
        var node = new Rmv2MeshNode(
            mesh,
            Mock.Of<IRmvMaterial>(),
            null!,
            new AnimationPlayer { IsEnabled = false });
        ISelectionState selectionState;
        switch (mode)
        {
            case GeometrySelectionMode.Vertex:
                var vertexSelection = new VertexSelectionState(
                    node,
                    0);
                vertexSelection.ModifySelection(
                    [0],
                    onlyRemove: false);
                vertexSelection.ActiveVertex = null;
                selectionState = vertexSelection;
                break;
            case GeometrySelectionMode.Edge:
                var edgeSelection = new EdgeSelectionState
                {
                    RenderObject = node
                };
                edgeSelection.ModifySelection(
                    [(0, 1)],
                    onlyRemove: false);
                edgeSelection.ActiveEdge = null;
                selectionState = edgeSelection;
                break;
            case GeometrySelectionMode.Face:
                var faceSelection = new FaceSelectionState
                {
                    RenderObject = node
                };
                faceSelection.ModifySelection(
                    [0],
                    onlyRemove: false);
                faceSelection.ActiveFace = null;
                selectionState = faceSelection;
                break;
            default:
                Assert.Fail($"Unexpected edit mode: {mode}");
                return;
        }

        selectionManager.SetState(selectionState);
        renderEngine.Update(new GameTime());
        selectionOverlay.Draw(new GameTime());
        using var renderTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        renderTarget.GetData(pixels);
        Assert.That(
            pixels.Count(IsSelectedOrange),
            Is.GreaterThan(0),
            $"Wireframe {mode} mode must keep the real selected-element overlay visible.");

        selectionManager.Dispose();
        mesh.Dispose();
    }

    [Test]
    public void AnimatedVertexSelection_OnlyHighlightsConnectedEdgeEnds()
    {
        const int size = 128;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var eventHub = new Mock<IEventHub>();
        using var scopedResources = new ScopedResourceLibrary(
            resources,
            eventHub.Object,
            Mock.Of<IStandardDialogs>());
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            eventHub.Object,
            new GridComponent(
                camera,
                resources,
                deviceResolver.Object)
            {
                ShowGrid = false
            });
        renderEngine.Initialize();
        var selectionManager = new SelectionManager(eventHub.Object);
        using var selectionOverlay =
            new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                scopedResources,
                deviceResolver.Object);
        selectionOverlay.Initialize();
        var mesh = CreateMesh(device, animated: true);
        var node = new Rmv2MeshNode(
            mesh,
            Mock.Of<IRmvMaterial>(),
            null!,
            CreateAnimationPlayer());
        var selection = new VertexSelectionState(node, 0);
        selection.ActiveVertex = null;

        selectionManager.SetState(selection);
        using var renderTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        var parameters = new CommonShaderParameters(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Zero,
            Vector3.Forward,
            0,
            0,
            0,
            1,
            Vector3.One,
            [],
            size,
            size);

        Color[] DrawCurrentWireframe()
        {
            renderEngine.Update(new GameTime());
            selectionOverlay.Draw(new GameTime());
            var wireframe = GetRenderItems(
                    renderEngine,
                    RenderBuckedId.Wireframe)
                .OfType<AnimatedWireframeRenderItem>()
                .Single();
            try
            {
                device.SetRenderTarget(renderTarget);
                device.Clear(
                    ClearOptions.Target | ClearOptions.DepthBuffer,
                    Color.Transparent,
                    1,
                    0);
                device.BlendState = BlendState.Opaque;
                device.DepthStencilState = DepthStencilState.Default;
                device.RasterizerState = RasterizerState.CullNone;
                wireframe.Draw(
                    device,
                    parameters,
                    RenderingTechnique.Normal);
            }
            finally
            {
                device.SetRenderTarget(null);
            }

            var framePixels = new Color[size * size];
            renderTarget.GetData(framePixels);
            return framePixels;
        }

        var defaultPixels = DrawCurrentWireframe();
        selection.ModifySelection([0], onlyRemove: false);
        var pixels = DrawCurrentWireframe();
        selectionManager.SetState(new EdgeSelectionState
        {
            RenderObject = node
        });
        var edgeModePixels = DrawCurrentWireframe();
        var connectedHighlight = CountPixels(
            pixels,
            size,
            8,
            46,
            44,
            84,
            IsSelectedOrange);
        var remoteTopologyHighlight = CountPixels(
            pixels,
            size,
            48,
            120,
            46,
            58,
            IsSelectedOrange);
        var farEndpointHighlight = CountPixels(
            pixels,
            size,
            98,
            120,
            70,
            84,
            IsSelectedOrange);

        Assert.Multiple(() =>
        {
            Assert.That(
                defaultPixels.Count(IsSelectedOrange),
                Is.EqualTo(0),
                "Kitbash vertex mode must keep an unselected wireframe neutral.");
            Assert.That(
                edgeModePixels.Count(IsSelectedOrange),
                Is.EqualTo(0),
                "Leaving vertex mode must clear the Kitbash vertex-edge gradient.");
            Assert.That(
                connectedHighlight,
                Is.GreaterThan(0),
                "The selected vertex must send a fading orange highlight into its connected edges.");
            Assert.That(
                remoteTopologyHighlight,
                Is.EqualTo(0),
                "Edges that are not connected to the selected vertex must keep the normal wire colour.");
            Assert.That(
                farEndpointHighlight,
                Is.EqualTo(0),
                "The highlight must fade before reaching an unselected endpoint.");
        });

        selectionManager.Dispose();
        mesh.Dispose();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Render3DObjects_SelectedEdgeRemainsOrangeOverWireframe(
        bool animated)
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            new Mock<IKeyboardComponent>().Object,
            new Mock<IMouseComponent>().Object);
        camera.Initialize();
        var resources = new ResourceLibrary(
            new Mock<IPackFileService>().Object);
        resources.Initialize(device, game.Content);
        var eventHub = new Mock<IEventHub>();
        using var scopedResources =
            new ScopedResourceLibrary(
                resources,
                eventHub.Object,
                new Mock<IStandardDialogs>().Object);
        var grid = new GridComponent(
            camera,
            resources,
            deviceResolver.Object)
        {
            ShowGrid = false
        };
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            eventHub.Object,
            grid);
        renderEngine.Initialize();
        var selectionManager = new SelectionManager(eventHub.Object);
        using var selectionOverlay =
            new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                scopedResources,
                deviceResolver.Object);
        selectionOverlay.Initialize();
        var mesh = CreateMesh(device, animated);
        var material = new Mock<IRmvMaterial>();
        material.SetupGet(value => value.ModelName).Returns("test");
        material.SetupGet(value => value.PivotPoint).Returns(Vector3.Zero);
        var animationPlayer = animated
            ? CreateAnimationPlayer()
            : new AnimationPlayer { IsEnabled = false };
        var node = new Rmv2MeshNode(
            mesh,
            material.Object,
            null!,
            animationPlayer);
        selectionManager.SetState(
            new EdgeSelectionState
            {
                RenderObject = node,
                SelectedEdges = [(0, 1)]
            });
        using var surface = new SolidMeshRenderItem(device);
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            surface);
        selectionOverlay.Draw(new GameTime());
        var selectionRenderItems = GetRenderItems(
            renderEngine,
            RenderBuckedId.Selection);
        Assert.That(
            selectionRenderItems,
            Has.Exactly(1)
                .TypeOf<AnimatedWireframeRenderItem>(),
            "Static and animated selected edges must use the same visible, depth-biased wireframe path.");
        using var renderTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        renderTarget.GetData(pixels);
        var orangePixels = pixels.Count(IsOrange);
        var widestOrangeColumn = Enumerable.Range(0, size)
            .Max(
                x => Enumerable.Range(0, size)
                    .Count(
                        y => IsOrange(pixels[y * size + x])));
        var initialOrangeRow = GetAverageOrangeRow(
            pixels,
            size);
        Assert.Multiple(() =>
        {
            Assert.That(
                orangePixels,
                Is.GreaterThan(0),
                "A selected edge must remain orange when it overlaps the black edit wireframe.");
            Assert.That(
                widestOrangeColumn,
                Is.InRange(1, 2),
                "A selected edge must stay readable without becoming a thick strip over the mesh.");
        });

        mesh.VertexArray[0].Position.Y += 0.6f;
        mesh.VertexArray[1].Position.Y += 0.6f;
        mesh.RebuildVertexBufferPartial(0, 1);
        renderEngine.Update(new GameTime());
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            surface);
        selectionOverlay.Draw(new GameTime());

        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);
        renderTarget.GetData(pixels);
        var movedOrangeRow = GetAverageOrangeRow(
            pixels,
            size);

        Assert.That(
            Math.Abs(movedOrangeRow - initialOrangeRow),
            Is.GreaterThan(10),
            "The orange selected edge must follow edited vertices instead of remaining at the original position.");

        selectionManager.Dispose();
        mesh.Dispose();
    }

    [Test]
    public void Render3DObjects_ForegroundOverlaysDoNotCutSelectionMask()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            new Mock<IKeyboardComponent>().Object,
            new Mock<IMouseComponent>().Object);
        camera.Initialize();
        var resources = new ResourceLibrary(
            new Mock<IPackFileService>().Object);
        resources.Initialize(device, game.Content);
        var grid = new GridComponent(
            camera,
            resources,
            deviceResolver.Object)
        {
            ShowGrid = false
        };
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            new Mock<IEventHub>().Object,
            grid);
        renderEngine.Initialize();
        renderEngine.AddRenderLines(
        [
            new VertexPositionColor(
                new Vector3(-0.8f, 0, 0.25f),
                Color.Black),
            new VertexPositionColor(
                new Vector3(0.8f, 0, 0.25f),
                Color.Black)
        ]);
        renderEngine.AddTranslucentPreviewTriangles(
        [
            new VertexPositionColor(
                new Vector3(-0.7f, -0.3f, 0.2f),
                new Color(64, 32, 16, 64)),
            new VertexPositionColor(
                new Vector3(0.7f, -0.3f, 0.2f),
                new Color(64, 32, 16, 64)),
            new VertexPositionColor(
                new Vector3(0, 0.3f, 0.2f),
                new Color(64, 32, 16, 64))
        ]);
        renderEngine.AddPreviewEdges(
        [
            new EdgeData
            {
                P0 = new Vector3(-0.8f, 0, 0.15f),
                P1 = new Vector3(0.8f, 0, 0.15f),
                C0 = Vector3.One,
                C1 = Vector3.One,
                Width = 2
            }
        ]);
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            new SelectionMaskRenderItem(
                game.Content.Load<Effect>(
                    "Shaders\\Pbr\\SpecGloss\\SpecGloss_main"),
                device));
        using var sceneTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        using var maskTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);

        device.SetRenderTargets(
            new RenderTargetBinding(sceneTarget),
            new RenderTargetBinding(maskTarget));
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        var maskPixels = new Color[size * size];
        maskTarget.GetData(maskPixels);
        var centralAlpha = byte.MaxValue;
        for (var y = 29; y <= 34; y++)
        {
            for (var x = 16; x <= 47; x++)
            {
                centralAlpha = Math.Min(
                    centralAlpha,
                    maskPixels[y * size + x].A);
            }
        }

        Assert.That(centralAlpha, Is.EqualTo(255));
    }

    private static void InvokeRender3DObjects(
        RenderEngineComponent renderEngine)
    {
        var method = typeof(RenderEngineComponent).GetMethod(
            "Render3DObjects",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method!.Invoke(
            renderEngine,
        [
            new CommonShaderParameters(
                Matrix.Identity,
                Matrix.Identity,
                Vector3.Backward,
                Vector3.Forward,
                0,
                0,
                0,
                1,
                Vector3.One,
                []),
            RenderingTechnique.Normal
        ]);
    }

    private static void AssertEditSelectionIsEmpty(
        ISelectionState state,
        GeometrySelectionMode mode)
    {
        Assert.That(state.Mode, Is.EqualTo(mode));
        switch (state)
        {
            case VertexSelectionState vertex:
                Assert.That(vertex.SelectedVertices, Is.Empty);
                Assert.That(vertex.VertexWeights, Has.All.Zero);
                Assert.That(vertex.ActiveVertex, Is.Null);
                break;
            case EdgeSelectionState edge:
                Assert.That(edge.SelectedEdges, Is.Empty);
                Assert.That(edge.ActiveEdge, Is.Null);
                break;
            case FaceSelectionState face:
                Assert.That(face.SelectedFaces, Is.Empty);
                Assert.That(face.ActiveFace, Is.Null);
                break;
            default:
                Assert.Fail(
                    $"Unexpected edit selection state: {state.GetType().Name}");
                break;
        }
    }

    private static IReadOnlyList<IRenderItem> GetRenderItems(
        RenderEngineComponent renderEngine,
        RenderBuckedId bucket)
    {
        var field = typeof(RenderEngineComponent).GetField(
            "_renderItems",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        var renderItems = field!.GetValue(renderEngine) as
            IReadOnlyDictionary<RenderBuckedId, List<IRenderItem>>;
        Assert.That(renderItems, Is.Not.Null);
        return renderItems![bucket];
    }

    private sealed class SelectionMaskRenderItem : IRenderItem
    {
        private readonly Effect _effect;
        private readonly Texture2D _diffuse;

        public SelectionMaskRenderItem(
            Effect effect,
            GraphicsDevice device)
        {
            _effect = effect;
            _diffuse = new Texture2D(device, 1, 1);
            _diffuse.SetData([Color.White]);
        }

        public bool SupportsTechnique(
            RenderingTechnique technique)
        {
            return technique == RenderingTechnique.Normal;
        }

        public void Draw(
            GraphicsDevice device,
            CommonShaderParameters parameters,
            RenderingTechnique renderingTechnique)
        {
            _effect.CurrentTechnique =
                _effect.Techniques["BasicColorDrawing"];
            _effect.Parameters["World"].SetValue(Matrix.Identity);
            _effect.Parameters["View"].SetValue(Matrix.Identity);
            _effect.Parameters["Projection"].SetValue(
                Matrix.Identity);
            _effect.Parameters["CameraPos"].SetValue(
                Vector3.Backward);
            _effect.Parameters["DirLightTransform"].SetValue(
                Matrix.Identity);
            _effect.Parameters["CapabilityFlag_ApplyAnimation"]
                .SetValue(false);
            _effect.Parameters["UseDiffuse"].SetValue(true);
            _effect.Parameters["DiffuseTexture"].SetValue(
                _diffuse);
            _effect.Parameters["UseSpecular"].SetValue(false);
            _effect.Parameters["UseGloss"].SetValue(false);
            _effect.Parameters["UseNormal"].SetValue(false);
            _effect.Parameters["UseAlpha"].SetValue(false);
            _effect.Parameters["UseMask"].SetValue(false);
            _effect.Parameters["CapabilityFlag_ApplyTinting"]
                .SetValue(false);
            _effect.Parameters["SelectionMaskEnabled"].SetValue(
                true);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    CreateFullScreenQuad(),
                    0,
                    2);
            }
        }

        private static VertexPositionNormalTextureCustom[]
            CreateFullScreenQuad()
        {
            return
            [
                CreateVertex(-1, -1),
                CreateVertex(-1, 1),
                CreateVertex(1, -1),
                CreateVertex(1, -1),
                CreateVertex(-1, 1),
                CreateVertex(1, 1)
            ];
        }

        private static VertexPositionNormalTextureCustom CreateVertex(
            float x,
            float y)
        {
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(x, y, 0.5f, 1),
                Normal = Vector3.UnitZ,
                Tangent = Vector3.UnitX,
                BiNormal = Vector3.UnitY,
                BlendWeights = Vector4.Zero,
                BlendIndices = Vector4.Zero
            };
        }
    }

    private sealed class PreviewOutlineCapabilityMaterial :
        CapabilityMaterial
    {
        public PreviewOutlineCapabilityMaterial()
            : base(
                CapabilityMaterialsEnum.SpecGlossPbr_Default,
                ShaderTypes.Pbr_SpecGloss,
                null!)
        {
        }

        protected override CapabilityMaterial CreateCloneInstance() =>
            new PreviewOutlineCapabilityMaterial();
    }

    private sealed class SelectionOutlineCapabilityMaterial :
        CapabilityMaterial
    {
        public SelectionOutlineCapabilityMaterial(
            IScopedResourceLibrary resourceLibrary)
            : base(
                CapabilityMaterialsEnum.SpecGlossPbr_Default,
                ShaderTypes.Pbr_SpecGloss,
                resourceLibrary)
        {
            Capabilities =
            [
                new CommonShaderParametersCapability(),
                new SpecGlossCapability(),
                new AnimationCapability(),
                new TintCapability()
            ];
            _renderingTechniqueMap[RenderingTechnique.Normal] =
                "BasicColorDrawing";
            _renderingTechniqueMap[RenderingTechnique.Solid] =
                "SolidDrawing";
        }

        protected override CapabilityMaterial CreateCloneInstance() =>
            new SelectionOutlineCapabilityMaterial(
                _resourceLibrary);
    }

    private sealed class SolidMeshRenderItem :
        IRenderItem,
        IDisposable
    {
        private readonly BasicEffect _effect;
        private readonly VertexPositionColor[] _vertices =
        [
            new(new Vector3(-0.8f, -0.2f, 0.5f), Color.Gray),
            new(new Vector3(-0.8f, 0.2f, 0.5f), Color.Gray),
            new(new Vector3(0.8f, -0.2f, 0.5f), Color.Gray),
            new(new Vector3(0.8f, -0.2f, 0.5f), Color.Gray),
            new(new Vector3(-0.8f, 0.2f, 0.5f), Color.Gray),
            new(new Vector3(0.8f, 0.2f, 0.5f), Color.Gray)
        ];

        public SolidMeshRenderItem(GraphicsDevice device)
        {
            _effect = new BasicEffect(device)
            {
                VertexColorEnabled = true,
                World = Matrix.Identity,
                View = Matrix.Identity,
                Projection = Matrix.Identity
            };
        }

        public bool SupportsTechnique(
            RenderingTechnique technique)
        {
            return technique == RenderingTechnique.Normal;
        }

        public void Draw(
            GraphicsDevice device,
            CommonShaderParameters parameters,
            RenderingTechnique renderingTechnique)
        {
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    2);
            }
        }

        public void Dispose()
        {
            _effect.Dispose();
        }
    }

    private static MeshObject CreateMesh(
        GraphicsDevice device,
        bool animated)
    {
        var mesh = new MeshObject(
            new GraphicsCardGeometry(device),
            "test")
        {
            VertexArray =
            [
                CreateVertex(-0.8f, -0.2f),
                CreateVertex(0.8f, -0.2f),
                CreateVertex(-0.8f, 0.2f),
                CreateVertex(0.8f, 0.2f)
            ],
            IndexArray = [0, 1, 2, 2, 1, 3]
        };
        mesh.ChangeVertexType(
            animated
                ? UiVertexFormat.Weighted
                : UiVertexFormat.Static,
            updateMesh: false);
        mesh.BuildBoundingBox();
        mesh.RebuildIndexBuffer();
        mesh.RebuildVertexBuffer();
        return mesh;
    }

    private static AnimationPlayer CreateAnimationPlayer()
    {
        var player = new AnimationPlayer();
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test_skeleton"
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Name = "root",
                    ParentId = -1
                }
            ]
        };
        var skeletonFrame = new AnimationFile.Frame();
        skeletonFrame.Transforms.Add(new RmvVector3(0, 0, 0));
        skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var skeletonPart = new AnimationFile.AnimationPart();
        skeletonPart.DynamicFrames.Add(skeletonFrame);
        skeletonFile.AnimationParts.Add(skeletonPart);
        var skeleton = new GameSkeleton(skeletonFile, player);
        var clip = new AnimationClip();
        clip.DynamicFrames.Add(
            new AnimationClip.KeyFrame
            {
                Position = [Vector3.Zero],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One]
            });
        clip.Duration = TimeSpan.FromSeconds(1);
        player.SetAnimation(clip, skeleton);
        player.IsEnabled = true;
        player.Pause();
        player.Refresh();
        return player;
    }

    private static VertexPositionNormalTextureCustom CreateVertex(
        float x,
        float y)
    {
        return new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(x, y, 0.5f, 1),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            BiNormal = Vector3.UnitY,
            BlendWeights = new Vector4(1, 0, 0, 0),
            BlendIndices = Vector4.Zero
        };
    }

    private static bool IsOrange(Color pixel)
    {
        return pixel.R > 200 &&
               pixel.G is > 70 and < 190 &&
               pixel.B < 40 &&
               pixel.A > 0;
    }

    private static bool IsSelectedOrange(Color pixel)
    {
        return pixel.A > 0 &&
               pixel.R > pixel.G * 1.5f &&
               pixel.G > pixel.B + 5;
    }

    private static int CountPixels(
        IReadOnlyList<Color> pixels,
        int width,
        int startX,
        int endX,
        int startY,
        int endY,
        Func<Color, bool> predicate)
    {
        var count = 0;
        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                if (predicate(pixels[y * width + x]))
                    count++;
            }
        }

        return count;
    }

    private static double GetAverageOrangeRow(
        IReadOnlyList<Color> pixels,
        int size)
    {
        var rowSum = 0;
        var count = 0;
        for (var index = 0; index < pixels.Count; index++)
        {
            if (pixels[index].A < 32 || !IsSelectedOrange(pixels[index]))
                continue;

            rowSum += index / size;
            count++;
        }

        Assert.That(
            count,
            Is.GreaterThan(0),
            "The selected edge must produce orange pixels.");
        return (double)rowSum / count;
    }
}
