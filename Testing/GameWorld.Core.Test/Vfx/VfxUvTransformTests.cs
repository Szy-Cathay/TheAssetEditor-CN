#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxUvTransformTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(4, 4, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    public static IEnumerable<TestCaseData> NativeCases()
    {
        var cases = ReadFixture().GetProperty("Cases");
        for (var index = 0; index < cases.GetArrayLength(); index++)
            foreach (var premultiplied in new[] { false, true })
                foreach (var offsetPivot in new[] { false, true })
                    yield return new TestCaseData(index, premultiplied, offsetPivot)
                        .SetName($"NativeUv_{cases[index].GetProperty("Name")}_premul{premultiplied}_offset{offsetPivot}");
    }

    [TestCaseSource(nameof(NativeCases))]
    public void ActualPreviewCorners_MatchFrozenNativeOrientationWithoutRotatingTheRectangle(int index, bool premultiplied, bool offsetPivot)
    {
        RequireGpu();
        var fixture = ReadFixture();
        var sample = fixture.GetProperty("Cases")[index];
        var snapshot = Snapshot(sample.GetProperty("Orientation").GetString()!, sample.GetProperty("FlipU").GetSingle(),
            sample.GetProperty("FlipV").GetSingle(), premultiplied, offsetPivot);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); preview.Reload(snapshot);
            var image = Render(graphics.GraphicsDevice, preview, .1f, $"native-uv-{index}-{premultiplied}-{offsetPivot}.png");
            var bounds = Bounds(image);
            Assert.That(bounds.Width, Is.EqualTo(51).Within(2), "UV rotation must keep the authored rectangular width.");
            Assert.That(bounds.Height, Is.EqualTo(38).Within(2), "UV rotation must keep the authored rectangular height.");
            var actual = Corners(image, bounds);
            Assert.That(actual, Is.EqualTo(sample.GetProperty("ExpectedCorners").GetString()),
                $"Frozen Terry raw {fixture.GetProperty("RawSha256")}, {sample.GetProperty("NativeCohorts")} independent births.");
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
            WriteEvidence($"native-uv-{index}-{premultiplied}-{offsetPivot}.json", new { Index = index, ActualCorners = actual,
                NativeExpectedCorners = sample.GetProperty("ExpectedCorners").GetString(), bounds.Width, bounds.Height,
                ActualProductPixels = true, OriginalGameBytesUnchanged = true });
        }
        finally { graphics.Release(); }
    }

    private static Color[] Render(GraphicsDevice device, EffekseerVfxPreview preview, float time, string name)
    {
        using var target = new RenderTarget2D(device, 256, 256, false, SurfaceFormat.Color, DepthFormat.Depth24);
        preview.Seek(time, Matrix.Identity);
        device.SetRenderTarget(target); device.Clear(new Color(20, 20, 20)); device.DepthStencilState = DepthStencilState.Default;
        preview.Draw(Camera); device.SetRenderTarget(null);
        var pixels = new Color[256 * 256]; target.GetData(pixels);
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var stream = File.Create(Path.Combine(output, name)); target.SaveAsPng(stream, 256, 256);
        }
        return pixels;
    }

    [TestCase(-.1f, 0, "untouched")]
    [TestCase(0, 1.1f, "untouched")]
    [TestCase(0, 0, "unknown")]
    public void InvalidUvControls_AreDiagnosedWithoutChangingGameBytes(float u, float v, string orientation)
    {
        var snapshot = Snapshot(orientation, u, v);
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(item => item.ReasonKey is "Vfx.Preview.RangeUnsupported" or "Vfx.Preview.UvUnsupported"), Is.True);
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    [Test]
    public void RandomUvCandidates_VaryBothAxesAndRemainDeterministicAcrossSeek()
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot("untouched", .5f, .5f));
        var simulation = new TotalWarVfxSimulation(conversion);
        var states = new HashSet<(bool, bool)>();
        simulation.Seek(.1f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single().UV;
        for (var birth = 0; birth < 64; birth++)
        {
            simulation.Seek(birth * .5f + .1f, Matrix.Identity);
            var newest = simulation.Frame(Camera).MinBy(particle => particle.Time);
            states.Add((newest.UV.Z < 0, newest.UV.W < 0));
        }
        Assert.That(states, Is.EquivalentTo(new[] { (false, false), (true, false), (false, true), (true, true) }),
            "The native half/half control rules out one shared flip decision for both axes.");
        simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().UV, Is.EqualTo(first));
        Assert.That(conversion.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.UvTransformCandidate"), Is.True);
    }

    [Test]
    public void FlippedAtlas_UsesTheSameAdjacentCellsForSpritesAndModels(
        [Values(0, 1)] int kind, [Values(false, true)] bool premultiplied,
        [Values(false, true)] bool flipU, [Values(false, true)] bool flipV)
    {
        RequireGpu();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            var texture = new byte[16 * 8 * 4];
            for (var y = 0; y < 8; y++) for (var x = 0; x < 16; x++)
            {
                var offset = (y * 16 + x) * 4;
                texture[offset] = (byte)(x < 4 || x >= 12 ? 255 : 0);
                texture[offset + 1] = (byte)(y < 4 ? 255 : 0);
                texture[offset + 2] = (byte)(x < 8 ? 0 : 255);
                texture[offset + 3] = 255;
            }
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, texture, texture.Length, 16, 8, 0));
            if (kind == 1)
            {
                var vertices = new[] { Vertex(-.5f, -.5f, 0, 1), Vertex(.5f, -.5f, 1, 1), Vertex(-.5f, .5f, 0, 0), Vertex(.5f, .5f, 1, 0) };
                EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, vertices, 4, [0, 1, 2, 2, 1, 3], 6));
            }
            var layer = EffekseerLayer.Create(); layer.Kind = kind; layer.ModelIndex = kind == 1 ? 0 : -1;
            layer.MaterialMode = premultiplied ? 1 : 0; layer.AtlasColumns = 2; layer.AtlasRows = 1; layer.AtlasLoop = 1; layer.AtlasBlend = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            var baseline = Draw(false, false);
            var actual = Draw(flipU, flipV);
            foreach (var y in new[] { 24, 40 }) foreach (var x in new[] { 24, 40 })
                Assert.That(Vector4.Distance(actual[y * 64 + x], baseline[(flipV ? 63 - y : y) * 64 + (flipU ? 63 - x : x)]), Is.LessThan(.025),
                    "Flipping cell-local UVs must not reverse the adjacent-frame offset or sample another atlas cell.");

            Vector4[] Draw(bool horizontal, bool vertical)
            {
                var particle = new EffekseerParticle { Transform = Matrix.CreateScale(2), Colour = Vector4.One,
                    UV = new(horizontal ? .5f : 0, vertical ? 1 : 0, horizontal ? -.5f : .5f, vertical ? -1 : 1), Flipbook = .5f };
                device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
                EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(Camera.View), Values(Matrix.CreateOrthographic(4, 4, .1f, 50)), [particle], 1));
                device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels); return pixels;
            }
        }
        finally { graphics.Release(); }
    }

    private static EffekseerVertex Vertex(float x, float y, float u, float v) => new()
    { Position = new(x, y, 0), Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, Binormal = Vector3.UnitY, UV = new(u, v) };
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    private static (int X, int Y, int Width, int Height) Bounds(Color[] image)
    {
        var selected = Enumerable.Range(0, image.Length).Where(i => image[i].R > 100 || image[i].B > 100).ToArray();
        Assert.That(selected.Length, Is.GreaterThan(100), "The controlled particle must produce visible pixels.");
        var left = selected.Min(i => i % 256); var right = selected.Max(i => i % 256);
        var top = selected.Min(i => i / 256); var bottom = selected.Max(i => i / 256);
        return (left, top, right - left + 1, bottom - top + 1);
    }

    private static string Corners(Color[] image, (int X, int Y, int Width, int Height) bounds)
    {
        var result = new StringBuilder();
        foreach (var point in new[] { (.25f, .25f), (.75f, .25f), (.25f, .75f), (.75f, .75f) })
        {
            var colour = image[(bounds.Y + (int)(bounds.Height * point.Item2)) * 256 + bounds.X + (int)(bounds.Width * point.Item1)];
            result.Append(colour.R > 100 ? colour.B > 100 ? 'M' : 'R' : colour.B > 100 ? 'B' : 'K');
        }
        return result.ToString();
    }

    private static VfxPreviewSnapshot Snapshot(string orientation, float flipU, float flipV, bool premultiplied = false, bool offsetPivot = false)
    {
        var texture = Resource("NativeUvAsymmetric.dds");
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="control" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{Scalar(2)}</property>
            <property name="ParticleLifeTime">{Scalar(1)}</property><property name="ParticleScale">{Vector(.8f, .6f, 1)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="control"><quality value="0">
            <modifier fragment="vs_translation"><property name="orientation_mode" value="Camera-facing"/>
            <property name="pivot_x" value="{(offsetPivot ? ".25" : ".5")}"/><property name="pivot_y" value="{(offsetPivot ? ".75" : ".5")}"/></modifier>
            <modifier fragment="vs_flip_uv"><property name="flip_u" value="{Number(flipU)}"/><property name="flip_v" value="{Number(flipV)}"/>
            <property name="uv_orientation" value="{orientation}"/></modifier>
            <modifier fragment="vs_colour"><property name="curve_colour">r_min,0,0;0,1,linear,linear,0,0,0,0,1|g_min,0,0;0,0,linear,linear,0,0,0,0,1|b_min,0,0;0,1,linear,linear,0,0,0,0,1|brightness_min,0,0;0,1,linear,linear,0,0,0,0,1:r,0;0,0|g,0;1,1|b,0;2,2|brightness,0;3,3</property></modifier>
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/control.dds"/>
            <property name="pre_multiplied_alpha" value="{premultiplied.ToString().ToLowerInvariant()}"/></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        return new(bytes, "vfx/control.xml", _ => texture);
    }

    private static string Scalar(float value) => $"x_min,0,0;0,{Number(value)},linear,linear,0,0,0,0,1:x,0;0,0";
    private static string Vector(float x, float y, float z) => $"x_min,0,0;0,{Number(x)},linear,linear,0,0,0,0,1|y_min,0,0;0,{Number(y)},linear,linear,0,0,0,0,1|z_min,0,0;0,{Number(z)},linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
    private static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonElement ReadFixture() { using var document = JsonDocument.Parse(Resource("NativeUvTransforms.json")); return document.RootElement.Clone(); }
    private static byte[] Resource(string suffix)
    {
        var assembly = typeof(VfxUvTransformTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    private static void RequireGpu()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit offscreen rendering opt-in is required.");
    }
    private static void WriteEvidence(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
