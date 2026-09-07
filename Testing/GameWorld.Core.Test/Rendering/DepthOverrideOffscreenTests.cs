using GameWorld.Core.Animation;
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
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class DepthOverrideOffscreenTests
{
    [TestCase("vertex", false)]
    [TestCase("vertex", true)]
    [TestCase("edge", false)]
    [TestCase("wire", false)]
    [TestCase("wire", true)]
    [TestCase("selected-wire", true)]
    [TestCase("face", false)]
    [TestCase("face", true)]
    public void ThroughOverlayDrawsOccludedPixelsAndRestoresDepthState(string kind, bool animated)
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var resources = new Mock<IScopedResourceLibrary>();
        resources.Setup(r => r.GetStaticEffect(ShaderTypes.EdgeQuad)).Returns(game.Content.Load<Effect>("Shaders\\EdgeQuadShader"));
        resources.Setup(r => r.GetStaticEffect(ShaderTypes.VertexPoint)).Returns(game.Content.Load<Effect>("Shaders\\VertexPointShader"));
        resources.Setup(r => r.GetStaticEffect(ShaderTypes.AnimatedSelection)).Returns(game.Content.Load<Effect>("Shaders\\AnimatedSelection"));
        var resolver = new Mock<IDeviceResolver>();
        resolver.SetupGet(r => r.Device).Returns(device);
        using var mesh = new MeshObject(new GraphicsCardGeometry(device), "depth")
        {
            VertexArray = new[] { new Vector3(-0.6f, -0.6f, 0.8f), new Vector3(0.6f, -0.6f, 0.8f), new Vector3(0, 0.6f, 0.8f) }
                .Select(p => new VertexPositionNormalTextureCustom
                {
                    Position = new Vector4(p, 1), Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, BiNormal = Vector3.UnitY,
                    BlendWeights = new Vector4(1, 0, 0, 0), BlendIndices = Vector4.Zero
                }).ToArray(),
            IndexArray = [0, 1, 2]
        };
        mesh.ChangeVertexType(Shared.GameFormats.RigidModel.UiVertexFormat.Weighted, updateMesh: false);
        mesh.BuildBoundingBox();
        mesh.RebuildIndexBuffer();
        mesh.RebuildVertexBuffer();
        var pose = MeshPoseSnapshot.Create(mesh, Matrix.Identity, [Matrix.Identity], animated);
        using var vertices = new VertexInstanceMesh(resolver.Object, resources.Object);
        using var edges = new EdgeQuadInstanceMesh(resolver.Object, resources.Object);
        using var wire = new AnimatedWireframeRenderItem(pose, resources.Object, Vector4.One, 50_000);
        wire.IsSelectionOverlay = kind == "selected-wire";
        var node = new Rmv2MeshNode(mesh, Mock.Of<Shared.GameFormats.RigidModel.MaterialHeaders.IRmvMaterial>(), null!, null!);
        var selection = new VertexSelectionState(node, 0);
        selection.SetSelection([0, 1, 2]);
        IRenderItem item = kind switch
        {
            "vertex" => new VertexRenderItem { VertexRenderer = vertices, Node = node, Pose = pose, SelectedVertices = selection },
            "edge" => new EdgeQuadRenderItem { EdgeQuadRenderer = edges, Edges = [new EdgeData
                { P0 = mesh.GetVertexById(0), P1 = mesh.GetVertexById(1), C0 = Vector3.One, C1 = Vector3.One, Width = 2 }] },
            "wire" or "selected-wire" => wire,
            _ => new AnimatedSelectionRenderItem(pose, resources.Object, Vector4.One, [0])
        };
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
        var parameters = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, Vector3.Zero, Vector3.Forward,
            0, 0, 0, 1, Vector3.One, []);
        var alphaTotal = 0;
        var normal = Draw(item);
        var through = Draw(new DepthOverrideRenderItem(item, DepthStencilState.None));
        Assert.That(normal, Is.Zero, "The geometry is behind an opaque depth layer.");
        Assert.That(through, Is.GreaterThan(2), "X-Ray must draw real back-layer pixels.");
        var opaqueAlpha = alphaTotal;
        Draw(new OcclusionOverlayRenderItem(item));
        Assert.That(alphaTotal, Is.InRange(1, opaqueAlpha * 0.75), "Occluded detail must remain visible without becoming an opaque stack.");
        var visible = Draw(new OcclusionOverlayRenderItem(item), 1);
        Assert.That(visible, Is.GreaterThan(2));
        Assert.That(alphaTotal, Is.EqualTo(opaqueAlpha), "Front-layer detail must retain its original strength.");

        int Draw(IRenderItem renderItem, float depth = 0.1f)
        {
            try
            {
                device.SetRenderTarget(target);
                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, depth, 0);
                device.DepthStencilState = DepthStencilState.Default;
                device.RasterizerState = RasterizerState.CullNone;
                device.BlendState = BlendState.Opaque;
                renderItem.Draw(device, parameters, RenderingTechnique.Normal);
                Assert.That(device.DepthStencilState, Is.SameAs(DepthStencilState.Default));
            }
            finally { device.SetRenderTarget(null); }
            var pixels = new Color[64 * 64];
            target.GetData(pixels);
            alphaTotal = pixels.Sum(pixel => (int)pixel.A);
            return pixels.Count(pixel => pixel.A > 0);
        }
    }
}
