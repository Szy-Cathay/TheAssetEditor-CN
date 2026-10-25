#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSceneDepthProjectionTests
{
    private const string CoverageTexturePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels)? _coverageImage;
    private string? _coverageSourceHash;

    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
    }

    [Test]
    public void PerspectiveSoftEdges_PreserveEmptyBackgroundAndFadeOnlyAtActualContact(
        [Values(false, true)] bool customMaterial, [Values(0, 4)] int samples, [Values(false, true)] bool mirrored)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            var image = OfficialCoverageImage();
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, image.Pixels, image.Pixels.Length,
                image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
            var layer = EffekseerLayer.Create();
            layer.SoftDistance = 2; layer.MaterialMode = customMaterial ? 1 : 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var particle = new EffekseerParticle { Transform = Matrix.CreateScale(2), Colour = Vector4.One, UV = FixedOriginalTexel(image) };
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, .01f, 25000);
            if (mirrored) projection *= Matrix.CreateScale(-1, 1, 1);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
            var full = Draw(target, 1, "clear");
            var contact = Vector4.Transform(new Vector4(0, 0, -10.5f, 1), projection);
            var faded = Draw(target, contact.Z / contact.W, "contact");
            using var noDepthTarget = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.None);
            var withoutDepth = Draw(noDepthTarget, 1, "no-depth");
            Assert.Multiple(() =>
            {
                Assert.That(full.X, Is.GreaterThan(.1f), "The unchanged official texel must remain visible against cleared perspective depth.");
                Assert.That(faded.X / full.X, Is.EqualTo(.25f).Within(.025), "A surface half a unit behind the particle must give a quarter fade over two units.");
                Assert.That(withoutDepth.X / full.X, Is.EqualTo(1).Within(.025), "Unavailable depth must keep the existing visible fallback.");
                Assert.That(EffekseerNative.AecnVfxDepthAvailable(preview), Is.Zero);
            });
            WriteEvidence($"perspective-soft-{customMaterial}-{samples}-{mirrored}.json", new
            {
                CustomMaterial = customMaterial, Samples = samples, MirroredProjection = mirrored, Near = .01f, Far = 25000,
                Texture = CoverageTexturePath, OriginalDdsSha256 = _coverageSourceHash,
                Full = full.X, Contact = faded.X, WithoutDepth = withoutDepth.X,
                ContactRatio = faded.X / full.X, WithoutDepthRatio = withoutDepth.X / full.X,
            });

            Vector4 Draw(RenderTarget2D surface, float depth, string label)
            {
                device.SetRenderTarget(surface);
                device.Clear(ClearOptions.Target | (surface.DepthStencilFormat == DepthFormat.None ? 0 : ClearOptions.DepthBuffer), Color.Black, depth, 0);
                device.DepthStencilState = DepthStencilState.Default;
                EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1));
                device.SetRenderTarget(null);
                var pixels = new Vector4[64 * 64]; surface.GetData(pixels);
                return (pixels[32 * 64 + 31] + pixels[32 * 64 + 32]) / 2;
            }
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void CoveragePartialViewport_HorizontalContactUsesTheAbsoluteDepthPixel() =>
        CheckPartialCoverageDepth(vertical: false, origin: Point.Zero);

    [Test]
    public void CoveragePartialViewport_NonzeroOriginVerticalContactUsesTheAbsoluteDepthPixel() =>
        CheckPartialCoverageDepth(vertical: true, origin: new(73, 41));

    private void CheckPartialCoverageDepth(bool vertical, Point origin)
    {
        const int width = 256, height = 192;
        var image = OfficialCoverageImage();
        var uv = FixedOriginalTexel(image);
        var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), width / (float)height, .01f, 25000);
        var receiver = Vector4.Transform(new Vector4(0, 0, -10.5f, 1), projection);
        var receiverDepth = receiver.Z / receiver.W;
        var layer = EffekseerLayer.Create();
        layer.MaterialMode = 1; layer.SoftDistance = 2; layer.ZTest = 0;
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(16, 12, 1), Colour = Vector4.One, UV = uv };
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, image.Pixels, image.Pixels.Length,
                image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var referenceClear = Draw(width, height, Point.Zero, false, "reference-clear");
            var referenceContact = Draw(width, height, Point.Zero, true, "reference-contact");
            var partialClear = Draw(512, 384, origin, false, "partial-clear");
            var partialContact = Draw(512, 384, origin, true, "partial-contact");
            var contactIndices = Enumerable.Range(0, width * height).Where(index =>
                vertical ? index / width >= 8 && index / width < height / 2 - 8 : index % width >= 8 && index % width < width / 2 - 8).ToArray();
            var clearIndices = Enumerable.Range(0, width * height).Where(index =>
                vertical ? index / width >= height / 2 + 8 && index / width < height - 8 : index % width >= width / 2 + 8 && index % width < width - 8).ToArray();
            float Mean(Vector4[] pixels, int[] indices) => indices.Average(index => (pixels[index].X + pixels[index].Y + pixels[index].Z) / 3);
            float MaximumDifference(Vector4[] left, Vector4[] right) => left.Zip(right).Max(pair =>
                Math.Max(Math.Abs(pair.First.X - pair.Second.X), Math.Max(Math.Abs(pair.First.Y - pair.Second.Y), Math.Abs(pair.First.Z - pair.Second.Z))));
            var metrics = new
            {
                Vertical = vertical, Origin = new[] { origin.X, origin.Y }, Viewport = new[] { width, height }, FullTarget = new[] { 512, 384 },
                Texture = CoverageTexturePath, OriginalDdsSha256 = _coverageSourceHash, FixedOriginalTexelUv = new[] { uv.X, uv.Y },
                ReceiverDepth = receiverDepth, SoftDistance = layer.SoftDistance, layer.MaterialMode,
                ClearReferenceMean = Mean(referenceClear, contactIndices),
                ReferenceContactRatio = Mean(referenceContact, contactIndices) / Mean(referenceClear, contactIndices),
                PartialContactRatio = Mean(partialContact, contactIndices) / Mean(partialClear, contactIndices),
                PartialUncoveredRatio = Mean(partialContact, clearIndices) / Mean(partialClear, clearIndices),
                ClearViewportMaximumDifference = MaximumDifference(referenceClear, partialClear),
                ContactViewportMaximumDifference = MaximumDifference(referenceContact, partialContact),
                Diagnostic = "Official original diffuse and constant original texel; fixed quad, direct positive SoftDistance and untextured scene receiver only. Not a complete original-effect acceptance.",
            };
            WriteEvidence($"coverage-partial-{vertical}-{origin.X}-{origin.Y}-metrics.json", metrics);
            TestContext.Progress.WriteLine(JsonSerializer.Serialize(metrics));
            Assert.Multiple(() =>
            {
                Assert.That(metrics.ClearReferenceMean, Is.GreaterThan(.1f), "The official texel must produce visible colour before testing depth.");
                Assert.That(metrics.ReferenceContactRatio, Is.EqualTo(.25f).Within(.03f));
                Assert.That(metrics.ClearViewportMaximumDifference, Is.LessThan(.005f), "Cleared depth must preserve the same visible quad in both viewports.");
                Assert.That(metrics.PartialContactRatio, Is.EqualTo(.25f).Within(.03f), "Only the actual receiver half must soften the particle.");
                Assert.That(metrics.PartialUncoveredRatio, Is.EqualTo(1).Within(.005f), "The clear half must retain its colour instead of sampling unrelated full-RT depth.");
                Assert.That(metrics.ContactViewportMaximumDifference, Is.LessThan(.005f), "Viewport size and origin must not relocate or interpolate the captured receiver boundary.");
            });

            Vector4[] Draw(int targetWidth, int targetHeight, Point viewportOrigin, bool contact, string label)
            {
                using var target = new RenderTarget2D(device, targetWidth, targetHeight, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target);
                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                device.Viewport = new(viewportOrigin.X, viewportOrigin.Y, width, height);
                device.ScissorRectangle = new(viewportOrigin.X, viewportOrigin.Y, width, height);
                if (contact) DrawReceiver();
                device.DepthStencilState = DepthStencilState.None;
                EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1));
                device.SetRenderTarget(null);
                var full = new Vector4[targetWidth * targetHeight]; target.GetData(full);
                var crop = new Vector4[width * height];
                for (var y = 0; y < height; y++) Array.Copy(full, (viewportOrigin.Y + y) * targetWidth + viewportOrigin.X, crop, y * width, width);
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    using var png = new Texture2D(device, width, height);
                    png.SetData(crop.Select(pixel => new Color(pixel)).ToArray());
                    using var stream = File.Create(Path.Combine(output, $"coverage-partial-{vertical}-{origin.X}-{origin.Y}-{label}.png"));
                    png.SaveAsPng(stream, width, height);
                }
                return crop;
            }

            void DrawReceiver()
            {
                var right = vertical ? 1 : 0;
                var bottom = vertical ? 0 : -1;
                VertexPositionColor[] vertices = [new(new(-1, bottom, receiverDepth), Color.White), new(new(right, bottom, receiverDepth), Color.White), new(new(-1, 1, receiverDepth), Color.White),
                    new(new(-1, 1, receiverDepth), Color.White), new(new(right, bottom, receiverDepth), Color.White), new(new(right, 1, receiverDepth), Color.White)];
                using var effect = new BasicEffect(device) { VertexColorEnabled = true, World = Matrix.Identity, View = Matrix.Identity, Projection = Matrix.Identity };
                using var noColour = new BlendState { ColorWriteChannels = ColorWriteChannels.None };
                device.BlendState = noColour; device.DepthStencilState = DepthStencilState.Default; device.RasterizerState = RasterizerState.CullNone;
                foreach (var pass in effect.CurrentTechnique.Passes) { pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2); }
                device.BlendState = BlendState.Opaque;
            }
        }
        finally { graphics.Release(); }
    }

    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) OfficialCoverageImage()
    {
        if (_coverageImage == null)
        {
            var official = new VfxOfficialDistortionPixelTests();
            official.LoadCurrentOfficialResourcesOnly();
            var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var bytes = (byte[]?)read.Invoke(official, [CoverageTexturePath]) ?? throw new FileNotFoundException(CoverageTexturePath);
            _coverageSourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
            _coverageImage = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))decode.Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
        }
        return _coverageImage.Value;
    }

    private static Vector4 FixedOriginalTexel((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) image)
    {
        var sample = Enumerable.Range(0, image.Width * image.Height)
            .Where(index => image.Pixels[index * 4 + 3] >= 192)
            .OrderByDescending(index => image.Pixels[index * 4] + image.Pixels[index * 4 + 1] + image.Pixels[index * 4 + 2])
            .First();
        return new((sample % image.Width + .5f) / image.Width, (sample / image.Width + .5f) / image.Height, 0, 0);
    }

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    private static void WriteEvidence(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
