using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxNativeSmokeLightingTests
{
    private static JsonElement Fixture()
    {
        var assembly = typeof(VfxNativeSmokeLightingTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("NativeSmokeLighting.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    public static IEnumerable<TestCaseData> NativeCases() => Fixture().GetProperty("Cases").EnumerateArray()
        .Select(item => new TestCaseData(item.Clone()).SetName($"NativeSmokeNormal_{item.GetProperty("Row")}_{item.GetProperty("Column")}"));

    [TestCaseSource(nameof(NativeCases))]
    public void AuthoredNormalAndVelocityBasis_MatchCapturedNativeRadiance(JsonElement item)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit offscreen rendering opt-in is required.");
        var row = item.GetProperty("Row").GetInt32();
        var column = item.GetProperty("Column").GetInt32();
        var source = Convert.FromBase64String(item.GetProperty("SourceBase64").GetString()!);
        var originalHash = SHA256.HashData(source);
        byte[] Resolve(string path)
        {
            if (path.EndsWith("white.dds", StringComparison.Ordinal)) return VfxMaterialCompositingTests.Texture(255, 255);
            var normal = VfxMaterialCompositingTests.Texture(0, row == 1 ? (byte)218 : (byte)128);
            for (var offset = 128; offset < normal.Length; offset += 4) normal[offset + 1] = row == 2 ? (byte)218 : (byte)128;
            return normal;
        }
        var snapshot = new VfxPreviewSnapshot(source, "vfx/native-smoke.xml", Resolve, "1");
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters.Single().Render.LightingMode, Is.EqualTo(2), "The authored lighting must reach the shared material.");
        Assert.That(conversion.Emitters.Single().Render.NormalIndex, Is.GreaterThanOrEqualTo(0));
        var eye = new Vector3(0, 10, 22);
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, new(0, 10, 0), Vector3.Up),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 360f / 654, 1, 12000) * Matrix.CreateScale(-1, 1, 1),
            eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [],
            GameWeather: GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(Fixture().GetProperty("EnvironmentXml").GetString()!), "controlled-native.environment"));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(3, Matrix.Identity);
        var backward = Vector(item.GetProperty("Backward"));
        // Native velocity-facing normals depend on the eye-to-particle direction. Match that recorded basis,
        // independently of the native live recording's age and the editor's deterministic emission clock.
        var position = column >= 2 ? eye + backward * (-22 / backward.Z) : new Vector3((column - 1.5f) * 2.3f, 6 + row * 4, 0);
        var attachment = Matrix.CreateTranslation(position - simulation.Frame(camera).Single().Transform.Translation);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(snapshot); preview.Seek(3, attachment);
            using var target = new RenderTarget2D(device, 360, 654, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(camera); device.SetRenderTarget(null);
            var pixels = new Vector4[360 * 654]; target.GetData(pixels);
            var sample = new Viewport(0, 0, 360, 654).Project(position, camera.Projection, camera.View, Matrix.Identity);
            var actual = pixels[(int)sample.Y * 360 + (int)sample.X];
            var expected = Vector(item.GetProperty("Radiance"));
            Assert.Multiple(() =>
            {
                // Native attributes are half precision; the editor retains full precision source curves.
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(.000005f));
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.000005f));
                Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(.000005f));
                Assert.That(SHA256.HashData(source), Is.EqualTo(originalHash));
            });
        }
        finally { graphics.Release(); }
    }

    private static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
}
