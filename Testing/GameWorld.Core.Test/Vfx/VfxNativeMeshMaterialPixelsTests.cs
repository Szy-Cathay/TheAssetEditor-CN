#nullable enable
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable, Apartment(System.Threading.ApartmentState.STA)]
public class VfxNativeMeshMaterialPixelsTests
{
    private const int Width = 64;
    private const float Tolerance = .000035f;
    private static readonly Vector4 Background = new Color(40, 80, 120, 96).ToVector4();
    private static readonly Vector4 Vertex = new(128f / 255, 64f / 255, 192f / 255, 128f / 255);
    private static readonly Vector3 Tint = new(.8f, .6f, 1.2f);
    private static readonly byte[][] Samples = [[64,128,192,255], [80,160,40,200], [200,90,50,128], [100,150,200,64], [110,144,170,210], [128,190,30,210]];
    private static int Centre => Width / 2 * Width + Width / 2;

    [SetUp]
    public void RequireExplicitOffscreenOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering is opt-in; no editor window or desktop input.");
    }

    public static IEnumerable<TestCaseData> ColourCases => from mode in Enumerable.Range(0, 4)
        from alpha in new[] { 0f, .371f, 1.8f }
        select new TestCaseData(mode, alpha);

    [TestCaseSource(nameof(ColourCases))]
    public void OfficialColourGraphEquations_KeepHdrRgbAndExactNativeDestinationAlpha(int mode, float alpha)
    {
        WithDevice(device =>
        {
            var actual = Render(device, mode, alpha);
            Near(actual[Centre], Composite(Reference(mode, alpha), Background));
            Assert.That(actual.Count(p => Vector4.Distance(p, Background) > .0001f), Is.GreaterThan(500));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ColourCoverageAboveOne_RetainsTheOfficialFloatingPointBlendFactors(int mode)
    {
        WithDevice(device =>
        {
            var p = Parameters(mode);
            var total = (float)(.85 * 1.5 / Reference(mode, 1.8f).Alpha);
            p[mode == 0 ? 35 : mode == 3 ? 53 : 44] = total;
            var expected = Composite(Reference(mode, 1.8f, totalStrength: total), Background);
            Assert.That(expected.W, Is.GreaterThan(2));
            Near(Render(device, mode, 1.8f, parameters: p)[Centre], expected);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void NativeBlendScope_RestoresOrdinaryModelRgbAndAlphaBeforeAndAfterTheBatch(int mode)
    {
        WithDevice(device =>
        {
            var native = Composite(Reference(mode, .371f), Background);
            var before = Render(device, mode, .371f, ordinaryOrder: -1);
            var after = Render(device, mode, .371f, ordinaryOrder: 1);
            var alone = Render(device, mode, .371f, ordinaryOrder: 2)[Centre];
            // The builtin model multiplies the byte particle alpha by this mesh's byte vertex alpha.
            const double ordinaryAlpha = 102.0 / 255 * 128.0 / 255;
            var source = alone - Background * (float)(1 - ordinaryAlpha);
            var expectedAfter = source + native * (float)(1 - ordinaryAlpha);
            expectedAfter.W = Math.Max(native.W, (float)ordinaryAlpha);
            Near(after[Centre], expectedAfter);
            Near(before[Centre], Composite(Reference(mode, .371f), alone));
        });
    }

    [Test]
    public void FlatMaterial_HasNoTextureBindingAndWritesTintWithOpaqueAlpha()
    {
        WithDevice(device => Near(Render(device, -1, 0)[Centre], new(Tint, 1)));
    }

    [TestCase(0f)]
    [TestCase(.371f)]
    [TestCase(1.8f)]
    public void BasicAdditive_UsesNativeZeroAlphaAndPreservesBackgroundAlpha(float alpha)
    {
        WithDevice(device => Near(Render(device, -2, alpha)[Centre], Composite(Reference(-2, alpha), Background)));
    }

    [TestCase(.1f)]
    [TestCase(.371f)]
    [TestCase(1.8f)]
    public void RigidGlow_UsesSignedDitherCoverageWithoutDiscardingNegativeAlpha(float alpha)
    {
        WithDevice(device => Near(Render(device, -3, alpha)[Centre], Composite(Reference(-3, alpha), Background)));
    }

    [Test]
    public void RigidGlowDistortion_ZeroBlueWeightUsesNativeDenominatorOneAndPreservesAlpha()
    {
        WithDevice(device =>
        {
            var pixels = Render(device, -4, .371f, gradient: true);
            const int x = 32, y = 32;
            var shift = .02f * Samples[1][0] / 255 * Width;
            Near(pixels[y * Width + x], new((x + shift) / (Width - 1), (y + shift) / (Width - 1), .2f, Background.W));
            Assert.That(pixels[y * Width + x].X, Is.GreaterThan(x / (float)(Width - 1) + .005f));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ConsecutiveNativeBatches_CompositeInSourceOrderWithNonUnitBackgroundAlpha(int mode)
    {
        WithDevice(device =>
        {
            var expected = Composite(Reference(mode, .615f), Composite(Reference(mode, .371f), Background));
            Near(Render(device, mode, .371f, secondAlpha: .615f)[Centre], expected);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void GlobalMaskClock_UsesTheSpecificNativeGraphClock(int mode)
    {
        WithDevice(device =>
        {
            var p = Parameters(mode); var pan = mode == 0 ? 14 : mode == 3 ? 23 : 18; p[pan] = .37f;
            const float sourceTime = .73f, age = .17f;
            var clock = mode == 1 ? age : mode == 3 ? sourceTime + .75 * (age - sourceTime) : sourceTime;
            byte[] ramp = [0,0,0,255,64,0,0,255,192,0,0,255,255,0,0,255];
            var x = (.375 + clock * .37) * 4 - .5; var left = (int)Math.Floor(x); var fraction = x - left;
            var global = (ramp[((left % 4 + 4) % 4) * 4] * (1 - fraction) + ramp[(((left + 1) % 4 + 4) % 4) * 4] * fraction) / 255;
            Near(Render(device, mode, .371f, parameters: p, materialTime: sourceTime, age: age, globalRamp: ramp)[Centre],
                Composite(Reference(mode, .371f, global), Background));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void SoftDepth_UsesInverseProjectionWorldDistanceForOffAxisPerspectivePixels(int mode)
    {
        WithDevice(device =>
        {
            var p = Parameters(mode); p[mode == 0 ? 13 : 17] = 1;
            var view = Matrix.CreateLookAt(new(0,0,10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, .1f, 100);
            var vp = view * projection; var inverse = Matrix.Invert(vp);
            var foreground = Vector4.Transform(new Vector4(0,0,0,1), vp); var background = Vector4.Transform(new Vector4(0,0,-.4f,1), vp);
            var depth = background.Z / background.W;
            const int x = 48, y = 32;
            Vector3 Unproject(float z)
            {
                var world = Vector4.Transform(new Vector4((x + .5f) / Width * 2 - 1, 1 - (y + .5f) / Width * 2, z, 1), inverse);
                return new(world.X / world.W, world.Y / world.W, world.Z / world.W);
            }
            var distance = Vector3.Distance(Unproject(depth), Unproject(foreground.Z / foreground.W));
            Assert.That(distance, Is.GreaterThan(.44f), "This off-axis control distinguishes Euclidean world distance from the old 0.4 view-Z approximation.");
            Near(Render(device, mode, .371f, parameters: p, perspectiveDepth: depth)[y * Width + x],
                Composite(Reference(mode, .371f, depthFade: distance), Background));
        });
    }

    // Independent double equations transcribed from the official PS instructions, not production shader helpers.
    private static (double[] Rgb, double Alpha) Reference(int mode, double particleAlpha, double? globalSample = null, double depthFade = 1, double totalStrength = .85)
    {
        double[] Sample(int i) => Samples[i].Select(v => v / 255.0).ToArray();
        var a = Sample(0); var b = Sample(1); var c = Sample(2); var o = Sample(3);
        // Packing is float32 before floor: .371f*1000 rounds to 371, while promoting first gives 370.99999.
        var particle = MathF.Floor((float)particleAlpha * 1000f) * .001;
        if (mode == -3)
        {
            var weighted = particle * Math.Pow(Math.Abs(Vertex.W), 2.2);
            var coverage = (particle - a[0]) * weighted;
            return (Enumerable.Range(0, 3).Select(i => coverage * weighted * new[] { .8, 1.2, .6 }[i] * .9
                * new[] { (double)Tint.X, Tint.Y, Tint.Z }[i]).ToArray(), coverage);
        }
        if (mode == -2)
            return (Enumerable.Range(0, 3).Select(i => a[i] * b[i] * 5 * new[] { .8, 1.2, .6 }[i] * .9
                * particle * Vertex.W * new[] { (double)Tint.X, Tint.Y, Tint.Z }[i]).ToArray(), 0);
        var maskA = Dot(a, [.2,.3,.1,.4]) * .7;
        var maskB = Dot(b, [.1,.4,.2,.3]) * 1.1;
        var maskC = Dot(c, [.5,.2,.1,.2]) * 1.3;
        var global = globalSample ?? Samples[5][0] / 255.0;
        var vertex = new[] { (double)Vertex.X, Vertex.Y, Vertex.Z, Vertex.W }.Select(v => 1 + .6 * (v - 1)).ToArray();
        var scalar = global * totalStrength * (1 + .75 * (particle - 1)) * depthFade;
        var mask = maskA * maskB * (mode == 0 ? 1 : maskC) * global * totalStrength * depthFade;
        var alpha = Math.Max(mask * (1 + .75 * (particle - 1)) * (mode == 0 ? 1 : Math.Pow(Math.Abs(vertex[3]), mode == 1 ? 2 : 1.4)), 0);
        var colour = Enumerable.Range(0, 3).Select(i => o[i] * (1 + o[3] * .3) * new[] { .8, 1.2, .6 }[i]
            * .9 * new[] { (double)Tint.X, Tint.Y, Tint.Z }[i] * (mode == 0 ? 1 : vertex[i])).ToArray();
        if (mode == 3)
        {
            var mixed = Enumerable.Range(0, 3).Select(i =>
            {
                var mix = new[] { .1, .2, .3 }[i];
                return Math.Max((mix + (.2 + .2) * (maskA - mix)) * (mix + (b[1] + .2) * (maskB - mix))
                    * (mix + (c[1] + .2) * (maskC - mix)) * scalar * Math.Pow(Math.Abs(vertex[3]), 1.4), 0);
            }).ToArray();
            return (Enumerable.Range(0, 3).Select(i => mixed[i] * colour[i]).ToArray(), mixed[0]);
        }
        return (colour.Select(v => v * alpha * (mode == 0 ? mask : 1)).ToArray(), alpha);
    }

    private static double Dot(double[] sample, double[] channels) => sample.Zip(channels, (a, b) => a * b).Sum();
    private static Vector4 Composite((double[] Rgb, double Alpha) source, Vector4 destination) => new(
        (float)(source.Rgb[0] + destination.X * (1 - source.Alpha)),
        (float)(source.Rgb[1] + destination.Y * (1 - source.Alpha)),
        (float)(source.Rgb[2] + destination.Z * (1 - source.Alpha)),
        (float)(source.Alpha * source.Alpha + destination.W * (1 - source.Alpha)));

    private static float[] Parameters(int mode)
    {
        if (mode < 0)
        {
            if (mode == -1) return [0,0,0,0];
            if (mode == -2) return [.8f,1.2f,.6f,.9f];
            var glow = new float[16]; new[] { .8f,1.2f,.6f,.9f }.CopyTo(glow,0); return glow;
        }
        var p = new float[72]; p[68] = mode;
        void Put(int index, params float[] values) => values.CopyTo(p, index);
        Put(0,.2f,.3f,.1f,.4f); Put(4,.1f,.4f,.2f,.3f); Put(8,.5f,.2f,.1f,.2f);
        if (mode == 0)
        {
            Put(8,0,0,1.3f,1,1,0,0,0,1,1,.8f,1.2f,.6f,.9f,1,0,1,.7f,1.1f,0,0,0,0,1,1,1,1,.85f,.75f);
        }
        else if (mode == 3)
        {
            Put(12,0,0,1.3f,1,1,0,1,1,0,0,.2f,0,0,1,1,.75f,.8f,1.2f,.6f,.9f,.1f,.2f,.3f,1,0,1,.7f,1.1f,1.3f);
            Put(47,1,1,1,1,1,1,.85f,1.4f,.6f,.75f);
        }
        else
        {
            Put(12,0,0,1.3f,1,1,0,0,0,1,1,.8f,1.2f,.6f,.9f,1,0,1,.7f,1.1f,1.3f);
            Put(38,1,1,1,1,1,1,.85f);
            if (mode == 1) Put(45,.6f,.75f); else Put(45,1.4f,.6f,.75f);
        }
        return p;
    }

    private static Vector4[] Render(GraphicsDevice device, int mode, float alpha, int ordinaryOrder = 0, float? secondAlpha = null,
        float[]? parameters = null, float materialTime = 0, float age = 0, byte[]? globalRamp = null, float? perspectiveDepth = null, bool gradient = false)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        var textureCount = mode == -1 ? 0 : mode == -2 ? 2 : mode < -2 ? 3 : mode == 0 ? 4 : mode == 3 ? 6 : 5;
        var pixels = mode < -2 ? Samples : mode == 0 ? new[] { Samples[0], Samples[1], Samples[3], Samples[5] }
            : mode == 3 ? Samples : new[] { Samples[0], Samples[1], Samples[2], Samples[3], Samples[5] };
        foreach (var (sample, index) in pixels.Take(textureCount).Select((sample, index) => (sample, index)))
        {
            var data = globalRamp != null && index == textureCount - 1 ? globalRamp : sample;
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, data, data.Length, data.Length / 4, 1, 0));
        }
        if (textureCount == 0 && ordinaryOrder != 0)
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, Samples[0], 4, 1, 1, 0));
        parameters ??= Parameters(mode);
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, Enumerable.Range(0, textureCount).ToArray(), textureCount, parameters, parameters.Length));
        var vertices = new[] { new Vector3(-1,-1,0), new(1,-1,0), new(-1,1,0), new(1,1,0) }.Select(position => new EffekseerVertex
        { Position = position, Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(.375f,.625f), Colour = Vertex }).ToArray();
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0,1,2,2,1,3], 6));
        var native = EffekseerLayer.Create(); native.Kind = 1; native.ModelIndex = 0; native.MeshMaterialIndex = 0;
        native.TextureIndex = textureCount == 0 ? -1 : 0; native.OpaqueModel = mode == -1 ? 1 : 0;
        if (mode == -4) native.Distortion = 1;
        var ordinary = EffekseerLayer.Create(); ordinary.Kind = 1; ordinary.ModelIndex = 0; ordinary.TextureIndex = 0;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, ordinaryOrder == 0 ? [native] : [native, ordinary], ordinaryOrder == 0 ? 1 : 2));
        var particle = new EffekseerParticle { Transform = perspectiveDepth.HasValue ? Matrix.CreateScale(10,10,1) : Matrix.Identity,
            Colour = new(Tint, alpha), UV = new(0,0,1,1), ParticleSize = 1, MaterialTime = materialTime, Time = age };
        var plain = particle; plain.Layer = 1; plain.Colour = new(.5f,.75f,.25f,102f/255);
        EffekseerParticle[] particles = ordinaryOrder switch { -1 => [plain,particle], 1 => [particle,plain], 2 => [plain], _ => [particle] };
        if (secondAlpha.HasValue) { var second = particle; second.Colour.W = secondAlpha.Value; particles = [particle,second]; }
        using var target = new RenderTarget2D(device, Width, Width, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(Background), 1, 0);
        if (gradient)
        {
            using var background = new Texture2D(device, Width, Width, false, SurfaceFormat.Vector4);
            background.SetData(Enumerable.Range(0, Width * Width).Select(i => new Vector4(i % Width / (float)(Width - 1), i / Width / (float)(Width - 1), .2f, Background.W)).ToArray());
            using var batch = new SpriteBatch(device);
            batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(background, Vector2.Zero, Color.White); batch.End();
        }
        var view = Matrix.CreateLookAt(new(0,0,10), Vector3.Zero, Vector3.Up);
        var projection = perspectiveDepth.HasValue ? Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2,1,.1f,100) : Matrix.CreateOrthographic(2,2,.1f,100);
        using var depthData = perspectiveDepth.HasValue ? new SharpDX.DataStream(Width * Width * sizeof(float), true, true) : null;
        if (depthData != null) { depthData.WriteRange(Enumerable.Repeat(perspectiveDepth!.Value, Width * Width).ToArray()); depthData.Position = 0; }
        using var depthTexture = depthData == null ? null : new SharpDX.Direct3D11.Texture2D(direct, new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = Width, Height = Width, MipLevels = 1, ArraySize = 1, Format = SharpDX.DXGI.Format.R32_Float,
            SampleDescription = new(1,0), Usage = SharpDX.Direct3D11.ResourceUsage.Immutable, BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
        }, new SharpDX.DataRectangle(depthData.DataPointer, Width * sizeof(float)));
        using var depthView = depthTexture == null ? null : new SharpDX.Direct3D11.ShaderResourceView(direct, depthTexture);
        var lighting = default(EffekseerLighting);
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), particles, particles.Length, in lighting, depthView?.NativePointer ?? 0));
        device.SetRenderTarget(null); var result = new Vector4[Width*Width]; target.GetData(result); return result;
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static void WithDevice(Action<GraphicsDevice> action) { var graphics = new GraphicsDeviceServiceMock(); try { action(graphics.GraphicsDevice); } finally { graphics.Release(); } }
    private static void Near(Vector4 actual, Vector4 expected) => Assert.That(Vector4.Distance(actual, expected), Is.LessThanOrEqualTo(Tolerance), $"actual={actual}, expected={expected}");
}
