using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxMeshSoftDepthViewportTests
{
    private const int Width = 128, Height = 96;
    private static readonly Color Background = new(26, 51, 77);

    public static IEnumerable<TestCaseData> MaterialsAndViewports()
    {
        foreach (var (name, count, textures) in new[]
        {
            ("ScrollingMesh", 49, 6), ("OneShotMesh", 54, 6), ("BasicMesh", 55, 6),
            ("AnimatedUvSprite", 36, 6), ("BasicSprite", 20, 4), ("PremultiplyAnimated", 32, 6),
            ("SolidMesh", 56, 6), ("PrelitSprite", 24, 5), ("SoulPanning", 28, 4),
        })
        foreach (var (targetWidth, targetHeight, x, y) in new[] { (128, 96, 0, 0), (256, 192, 0, 0), (256, 192, 65, 35) })
            yield return new TestCaseData(count, textures, targetWidth, targetHeight, x, y)
                .SetName($"{name}_SoftDepth_Target{targetWidth}x{targetHeight}_Origin{x}_{y}");
    }

    [SetUp]
    public void RequireExplicitGpuOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Specialized material depth coordinates require explicit offscreen GPU opt-in.");
    }

    [TestCaseSource(nameof(MaterialsAndViewports))]
    public void MaterialDepth_UsesAbsoluteTargetCoordinates(int parameterCount, int textureCount,
        int targetWidth, int targetHeight, int viewportX, int viewportY)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device, parameterCount, textureCount);
            var local = new Viewport(0, 0, Width, Height);
            var expected = Render(device, preview, Width, Height, local, true);
            var withoutReceiver = Render(device, preview, Width, Height, local, false);
            var actual = Render(device, preview, targetWidth, targetHeight,
                new Viewport(viewportX, viewportY, Width, Height), true);
            var respondingPixels = 0;
            var compared = 0;
            var maximumDifference = 0f;
            for (var y = 2; y < Height - 2; y++)
            for (var x = 2; x < Width - 2; x++)
            {
                var reference = expected[y * Width + x];
                if (Vector3.Distance(Rgb(reference), Rgb(withoutReceiver[y * Width + x])) > .01f)
                    respondingPixels++;
                // Exclude only raster and hard-discard transition edges.
                if (Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx =>
                    Vector3.Distance(Rgb(reference), Rgb(expected[(y + dy) * Width + x + dx])) > .06f))) continue;
                var observed = actual[(viewportY + y) * targetWidth + viewportX + x];
                maximumDifference = Math.Max(maximumDifference, Vector3.Distance(Rgb(reference), Rgb(observed)));
                compared++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(respondingPixels, Is.GreaterThan(30), "The full-target control must actually respond to scene depth.");
                Assert.That(compared, Is.GreaterThan(500), "A substantial interior must be compared, including softened pixels.");
                Assert.That(maximumDifference, Is.LessThan(.003f),
                    "Changing target extent or viewport origin must preserve material pixels at the same local location.");
            });
        }
        finally { graphics.Release(); }
    }

    private static EffekseerHandle Preview(GraphicsDevice device, int count, int textureCount)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        Assert.That(handle.IsInvalid, Is.False);
        // DIAG constant maps and payloads select the supported material branches without editing any game asset.
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [255, 255, 255, 255], 4, 1, 1, 0));
        var parameters = Enumerable.Repeat(1f, count).ToArray();
        var unusedChannels = count switch
        {
            49 => new[] { 1, 2, 3, 5, 6, 7, 9, 10, 11 },
            54 => [24, 25, 26, 33, 34, 35, 42, 43, 44],
            55 => [9, 10, 11, 18, 19, 20, 27, 28, 29],
            _ => [],
        };
        foreach (var index in unusedChannels) parameters[index] = 0;
        if (count == 32) parameters[25] = 0;
        if (count == 24) parameters[17] = 0;
        if (count == 28) { parameters[2] = 0; parameters[4] = 0; }
        if (count == 56)
        {
            foreach (var index in new[] { 21, 22, 23, 29, 30, 31, 38, 39, 40 }) parameters[index] = 0;
            parameters[19] = 2;
        }
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle, new int[textureCount], textureCount, parameters, count));
        EffekseerVertex[] vertices =
        [
            new() { Position = new(-1, -1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(0, 1), Colour = Vector4.One },
            new() { Position = new(1, -1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(1, 1), Colour = Vector4.One },
            new() { Position = new(-1, 1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(0, 0), Colour = Vector4.One },
            new() { Position = new(1, 1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(1, 0), Colour = Vector4.One },
        ];
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, vertices, vertices.Length, [0, 1, 2, 2, 1, 3], 6));
        var layer = EffekseerLayer.Create(); layer.Kind = count is 20 or 24 or 36 ? 0 : 1;
        layer.ModelIndex = 0; layer.MeshMaterialIndex = 0;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
        return handle;
    }

    private static Vector4[] Render(GraphicsDevice device, EffekseerHandle handle, int width, int height, Viewport viewport, bool receiver)
    {
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Vector4,
            DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
        var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreateOrthographic(4, 3, .1f, 50) * Matrix.CreateScale(-1, 1, 1);
        device.SetRenderTarget(target);
        try
        {
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Background, 1, 0);
            device.Viewport = viewport;
            device.DepthStencilState = DepthStencilState.Default; device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullNone;
            if (receiver)
            {
                using var effect = new BasicEffect(device) { View = view, Projection = projection, VertexColorEnabled = true };
                var plane = new[]
                {
                    new VertexPositionColor(new(-20, -20, 8.5f), Background), new VertexPositionColor(new(20, -20, -9.5f), Background),
                    new VertexPositionColor(new(-20, 20, 8.5f), Background), new VertexPositionColor(new(20, 20, -9.5f), Background),
                };
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply(); device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, plane, 0, 4, new short[] { 0, 1, 2, 2, 1, 3 }, 0, 2);
                }
            }
            var particle = new EffekseerParticle { Transform = Matrix.Identity, Colour = Vector4.One, UV = new(0, 0, 1, 1), Time = 1, ParticleSize = 1 };
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view), Values(projection), [particle], 1));
        }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[width * height]; target.GetData(pixels); return pixels;
    }

    private static Vector3 Rgb(Vector4 value) => new(value.X, value.Y, value.Z);
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
