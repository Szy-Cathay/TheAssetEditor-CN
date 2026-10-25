#nullable enable
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxProjectedDecalTests
{
    [SetUp]
    public void RequireExplicitGpuOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Projected decal pixels require explicit offscreen GPU opt-in.");
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, 4)]
    public void Projection_RequiresAReceiverAndDoesNotApplyCoverageTwice(bool perspective, int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var handle = Preview(device);
            var absent = Render(device, handle, perspective, samples, 0, null, null);
            var flat = Render(device, handle, perspective, samples, 0, 0, null);
            var elevated = Render(device, handle, perspective, samples, 3, 0, null);
            Assert.Multiple(() =>
            {
                Assert.That(absent.Max(p => p.X), Is.LessThan(.001), "Clear background is not an opaque receiver.");
                Assert.That(flat[32 * 64 + 32].X, Is.EqualTo(.5).Within(.02), "Only one volume face may contribute coverage.");
                Assert.That(flat.Count(p => p.X > .1), Is.GreaterThan(100));
                Assert.That(elevated.Max(p => p.X), Is.LessThan(.001), "The native elevated control does not touch the ground.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, 4)]
    public void ActualSceneDepth_MatchesTheFlatReceiverAndBlocksSurfacesOutsideTheVolume(bool perspective, int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var handle = Preview(device);
            var flat = Render(device, handle, perspective, samples, 0, 0, null);
            var surface = Render(device, handle, perspective, samples, 0, null, 0);
            var occluded = Render(device, handle, perspective, samples, 0, 0, 3);
            Assert.Multiple(() =>
            {
                Assert.That(surface[32 * 64 + 32].X, Is.EqualTo(.5).Within(.02));
                Assert.That(flat.Zip(surface).Count(p => MathF.Abs(p.First.X - p.Second.X) > .03), Is.LessThan(50),
                    "Actual depth and the explicitly supplied plane must project the same footprint, apart from raster edges.");
                Assert.That(occluded.Max(p => p.X), Is.LessThan(.001), "A supplied preview plane must never shine through an opaque surface.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EditorProjection_KeepsTheReceiverVisibleWhenTheCameraEntersTheVolume(bool cameraInside)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var handle = Preview(device);
            var flat = Render(device, handle, true, 4, 0, 0, null, true, cameraInside);
            var surface = Render(device, handle, true, 4, 0, null, 0, true, cameraInside);
            Assert.Multiple(() =>
            {
                Assert.That(flat[32 * 64 + 32].X, Is.EqualTo(.5).Within(.02));
                Assert.That(surface[32 * 64 + 32].X, Is.EqualTo(.5).Within(.02));
            });
        }
        finally { graphics.Release(); }
    }

    private static EffekseerHandle Preview(GraphicsDevice device)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        Assert.That(handle.IsInvalid, Is.False);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [255, 255, 255, 255], 4, 1, 1, 0));
        var model = VfxDecalVolume.Create(new(.5f));
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, model.Vertices, model.Vertices.Length, model.Indices, model.Indices.Length));
        var layer = EffekseerLayer.Create(); layer.Kind = 2; layer.ModelIndex = 0;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
        return handle;
    }

    private static Vector4[] Render(GraphicsDevice device, EffekseerHandle handle, bool perspective, int samples,
        float particleHeight, float? previewGround, float? opaqueGround, bool editorProjection = false, bool cameraInside = false)
    {
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        var editorCamera = new Vector3(4.0047197f, 4.0673666f, 8.210892f);
        var view = Matrix.CreateLookAt(editorProjection ? editorCamera * (cameraInside ? 1 : 3) : new(0, 5, 6), Vector3.Zero, Vector3.Up);
        var projection = editorProjection ? Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, .01f, 25000) * Matrix.CreateScale(-1, 1, 1)
            : perspective ? Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, .1f, 50)
            : Matrix.CreateOrthographic(6, 6, .1f, 50);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        device.DepthStencilState = DepthStencilState.Default;
        device.BlendState = BlendState.Opaque;
        device.RasterizerState = RasterizerState.CullNone;
        if (opaqueGround is { } height)
        {
            using var effect = new BasicEffect(device) { View = view, Projection = projection, VertexColorEnabled = true };
            var vertices = new[] { new VertexPositionColor(new(-20, height, -20), Color.Black), new VertexPositionColor(new(-20, height, 20), Color.Black),
                new VertexPositionColor(new(20, height, -20), Color.Black), new VertexPositionColor(new(20, height, 20), Color.Black) };
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply(); device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4, new short[] { 0, 1, 2, 1, 3, 2 }, 0, 2);
            }
        }
        var lighting = new EffekseerLighting { Parameters = new(0, 1, previewGround.HasValue ? 1 : 0, previewGround ?? 0) };
        var particle = new EffekseerParticle { Transform = (editorProjection ? Matrix.CreateScale(32, 32, 10) : Matrix.CreateScale(2)) * Matrix.CreateRotationX(MathHelper.PiOver2)
            * Matrix.CreateTranslation(0, particleHeight, 0), Colour = new(1, 1, 1, .5f), UV = new(0, 0, 1, 1) };
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view), Values(projection), [particle], 1, in lighting));
        device.SetRenderTarget(null);
        var pixels = new Vector4[4096]; target.GetData(pixels);
        return pixels;
    }

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
