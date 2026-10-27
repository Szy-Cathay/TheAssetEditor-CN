using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxProjectedDecalViewportTests
{
    private const int Width = 128, Height = 96;

    [SetUp]
    public void RequireExplicitGpuOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Projected decal coordinates require explicit offscreen GPU opt-in.");
    }

    [TestCase(128, 96, 0, 0)]
    [TestCase(256, 192, 0, 0)]
    [TestCase(256, 192, 64, 32)]
    public void ActualReceiverDepth_UsesAbsoluteTargetCoordinates(
        int targetWidth, int targetHeight, int viewportX, int viewportY)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var handle = Preview(device);
            var camera = Camera(true);
            var localViewport = new Viewport(0, 0, Width, Height);
            var actualViewport = new Viewport(viewportX, viewportY, Width, Height);
            // DIAG: the same known plane is either analytic or actual opaque depth.
            var expected = Render(device, handle, Width, Height, localViewport, camera, false, true);
            var actual = Render(device, handle, targetWidth, targetHeight, actualViewport, camera, true, false);
            var compared = 0;
            var maximumDifference = 0f;
            for (var y = 1; y < Height - 1; y++)
            for (var x = 1; x < Width - 1; x++)
            {
                // Exclude raster and colour-transition edges, not receiver-depth errors.
                var reference = expected[y * Width + x];
                if (reference.X + reference.Z < .9f) continue;
                if (Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx =>
                    Vector3.Distance(Rgb(reference), Rgb(expected[(y + dy) * Width + x + dx])) > .001f))) continue;
                var observed = actual[(y + viewportY) * targetWidth + x + viewportX];
                maximumDifference = Math.Max(maximumDifference,
                    Vector3.Distance(Rgb(reference), Rgb(observed)));
                compared++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(compared, Is.GreaterThan(100), "The analytic receiver must provide a substantial interior control.");
                Assert.That(maximumDifference, Is.LessThan(.002f),
                    "Changing the depth target extent or viewport origin must preserve projected receiver pixels.");
            });
        }
        finally { graphics.Release(); }
    }

    private static EffekseerHandle Preview(GraphicsDevice device)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        Assert.That(handle.IsInvalid, Is.False);
        // DIAG texture: opaque red on the left and blue on the right, constant along V.
        var rgba = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            rgba[(y * 8 + x) * 4 + (x < 4 ? 0 : 2)] = 255;
            rgba[(y * 8 + x) * 4 + 3] = 255;
        }
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, rgba, rgba.Length, 8, 8, 0));
        var model = VfxDecalVolume.Create(new(.5f));
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, model.Vertices, model.Vertices.Length, model.Indices, model.Indices.Length));
        var layer = EffekseerLayer.Create();
        layer.Kind = 2; layer.ModelIndex = 0;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
        return handle;
    }

    private static (Matrix View, Matrix Projection) Camera(bool mirrored)
    {
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), Width / (float)Height, .1f, 50);
        if (mirrored) projection *= Matrix.CreateScale(-1, 1, 1);
        return (Matrix.CreateLookAt(new(0, 4, 6), Vector3.Zero, Vector3.Up), projection);
    }

    private static Vector4[] Render(GraphicsDevice device, EffekseerHandle handle, int width, int height,
        Viewport viewport, (Matrix View, Matrix Projection) camera, bool actualGround, bool analyticGround)
    {
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Vector4,
            DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        try
        {
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            device.Viewport = viewport;
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullNone;
            if (actualGround)
            {
                using var effect = new BasicEffect(device)
                {
                    View = camera.View, Projection = camera.Projection, VertexColorEnabled = true,
                };
                var vertices = new[]
                {
                    new VertexPositionColor(new(-20, 0, -20), Color.Black),
                    new VertexPositionColor(new(-20, 0, 20), Color.Black),
                    new VertexPositionColor(new(20, 0, -20), Color.Black),
                    new VertexPositionColor(new(20, 0, 20), Color.Black),
                };
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4,
                        new short[] { 0, 1, 2, 1, 3, 2 }, 0, 2);
                }
            }
            var lighting = new EffekseerLighting { Parameters = new(0, 1, analyticGround ? 1 : 0, 0) };
            var particle = new EffekseerParticle
            {
                Transform = Matrix.CreateScale(2) * Matrix.CreateRotationX(MathHelper.PiOver2),
                Colour = Vector4.One, UV = new(0, 0, 1, 1),
            };
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(camera.View), Values(camera.Projection), [particle], 1, in lighting));
        }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[width * height];
        target.GetData(pixels);
        return pixels;
    }

    private static Vector3 Rgb(Vector4 value) => new(value.X, value.Y, value.Z);

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
