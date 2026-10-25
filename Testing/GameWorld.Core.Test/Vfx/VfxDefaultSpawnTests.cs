#nullable enable
using System.Text;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxDefaultSpawnTests
{
    private static readonly Vector3 CameraPosition = new(10 * MathF.Cos(MathHelper.ToRadians(24)) * MathF.Sin(MathHelper.ToRadians(26)),
        10 * MathF.Sin(MathHelper.ToRadians(24)), 10 * MathF.Cos(MathHelper.ToRadians(24)) * MathF.Cos(MathHelper.ToRadians(26)));
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(CameraPosition, Vector3.Zero, Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 540f / 980, .01f, 25000) * Matrix.CreateScale(-1, 1, 1), CameraPosition, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(-10f)]
    [TestCase(-1f)]
    [TestCase(1f)]
    public void DefaultVerticalSpawn_PreservesTheHorizontalPlaneAtBirthAndLater(float speed)
    {
        var snapshot = Snapshot(speed);
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
        foreach (var time in new[] { 0f, .1f, .3f })
        {
            simulation.Seek(time, Matrix.Identity);
            var transform = simulation.Frame(Camera).Single().Transform;
            Assert.That(Vector3.Normalize(transform.Up).Y, Is.EqualTo(MathF.Sign(speed)).Within(.000001));
            Assert.That(Vector3.Normalize(transform.Right).Y, Is.Zero.Within(.000001));
            Assert.That(Vector3.Dot(Vector3.Normalize(transform.Right), Vector3.Normalize(transform.Up)), Is.Zero.Within(.000001));
            Assert.That(transform.Right.Length(), Is.GreaterThan(.9));
        }
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    [Test]
    public void NearVerticalExplicitVelocity_KeepsTheWorldBasisInsteadOfUsingTheCamera()
    {
        var document = VfxDocument.Read(Snapshot(1).GameBytes);
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "SpawnAngle"), new System.Xml.Linq.XAttribute("x", ".00001"), new System.Xml.Linq.XAttribute("y", ".00001")));
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(document.Write(), "vfx/near-vertical.xml", _ => Texture())));
        simulation.Seek(.1f, Matrix.Identity);
        var expected = simulation.Frame(Camera).Single().Transform;
        Assert.That(simulation.Frame(Camera with { CameraPosition = new(30, 10, 20) }).Single().Transform, Is.EqualTo(expected));
        Assert.That(Vector3.Normalize(expected.Right).Y, Is.Zero.Within(.000001));
    }

    [Test]
    public void DefaultHorizontalSpawn_SeekAndReloadRemainDeterministic()
    {
        var snapshot = Snapshot(-10);
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(.3f, Matrix.Identity);
        var expected = direct.Frame(Camera);
        Assert.That(expected, Is.Not.Empty);
        var incremental = new TotalWarVfxSimulation(conversion); incremental.Advance(.1f, Matrix.Identity); incremental.Advance(.2f, Matrix.Identity);
        Assert.That(incremental.Frame(Camera), Is.EqualTo(expected));
        direct.Seek(.1f, Matrix.Identity); direct.Seek(.3f, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(expected));
        var reloaded = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot)); reloaded.Seek(.3f, Matrix.Identity);
        Assert.That(reloaded.Frame(Camera), Is.EqualTo(expected));
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void OriginalHorizontalLayer_RendersWithItsOwnTextureAndUnchangedFields()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit offscreen rendering opt-in is required.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var definitions = loader.Load(Path.Combine(data!, "vfx_desc.pack"))!;
        var textures = loader.Load(Path.Combine(data!, "vfx.pack"))!;
        const string path = @"vfx\wh3_main_nurgle_rancid_visitations_entity.xml";
        var source = definitions.FileList[path].DataSource.ReadData();
        byte[]? Resolve(string key) => textures.FileList.GetValueOrDefault(key.Replace('/', '\\').ToLowerInvariant())?.DataSource.ReadData();
        Assert.That(TotalWarVfxConverter.Convert(new(source, path, Resolve)).Emitters, Has.Count.EqualTo(5));
        var document = VfxDocument.Read(source);
        foreach (var layer in document.Layers.Where(layer => layer.Kind == "Emitter" && layer.Name != "debuff_lines").ToArray()) document.RemoveLayer(layer).Apply();
        var snapshot = new VfxPreviewSnapshot(document.Write(), path, Resolve);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot);
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
            foreach (var time in new[] { 3f, 4f, 6f })
            {
                simulation.Seek(time, Matrix.Identity); preview.Seek(time, Matrix.Identity);
                Assert.That(simulation.Frame(Camera), Is.Not.Empty, $"Original horizontal layer at {time}s");
                using var target = new RenderTarget2D(device, 540, 980, false, SurfaceFormat.Color, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(Color.DarkSlateGray); device.DepthStencilState = DepthStencilState.Default;
                preview.Draw(Camera); device.SetRenderTarget(null);
                var pixels = new Color[540 * 980]; target.GetData(pixels);
                Assert.That(pixels.Count(pixel => pixel != Color.DarkSlateGray), Is.GreaterThan(0));
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    using var stream = File.Create(Path.Combine(output, $"original-horizontal-t{time}.png")); target.SaveAsPng(stream, 540, 980);
                }
            }
        }
        finally { graphics.Release(); }
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        Assert.That(definitions.FileList[path].DataSource.ReadData(), Is.EqualTo(source));
    }

    private static VfxPreviewSnapshot Snapshot(float speed)
    {
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="default" type="continuous_mesh"><property name="InfiniteLife" value="true"/>
            <property name="EmissionRateCurve">{Scalar(1)}</property><property name="ParticleLifeTime">{Scalar(.4f)}</property>
            <property name="ParticleScale">{UnitScale()}</property>
            <property name="ParticleInitialSpeed">{Scalar(speed)}</property></emitter></emitters><behaviour_chains><behaviour_chain id="default"><quality value="0">
            <modifier fragment="vs_translation_velocity_v2"><property name="orientation_mode" value="Horizontally-oriented"/><property name="velocity_stretch_cap" value="1"/></modifier>
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/marker.dds"/></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        return new(bytes, "vfx/default.xml", _ => Texture());
    }
    private static string Scalar(float value) => $"x_min,0,0;0,{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:x,0;0,0";
    private static string UnitScale() => string.Join('|', new[] { "x", "y", "z" }.Select(axis => $"{axis}_min,0,0;0,1,linear,linear,0,0,0,0,1")) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] Texture()
    {
        var assembly = typeof(VfxDefaultSpawnTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeUvAsymmetric.dds", StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
}
