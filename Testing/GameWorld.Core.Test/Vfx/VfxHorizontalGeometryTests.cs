#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxHorizontalGeometryTests
{
    private static readonly Vector3 CameraPosition = new(0, 6 + 30 * MathF.Sin(MathHelper.ToRadians(35)), -30 * MathF.Cos(MathHelper.ToRadians(35)));
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(CameraPosition, new(0, 6, 0), Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 540f / 980, 1, 12000) * Matrix.CreateScale(-1, 1, 1), CameraPosition, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    public void ActualPixels_MatchNativeFullResolutionGeometry(int index)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        using var fixture = JsonDocument.Parse(Resource("NativeHorizontalGeometry.json"));
        var data = fixture.RootElement;
        var control = data.GetProperty("Cases").EnumerateArray().Single(item => item.GetProperty("Index").GetInt32() == index);
        var position = Vector(control.GetProperty("Position"));
        var scale = Vector(control.GetProperty("Scale"));
        var snapshot = Snapshot(control.GetProperty("Axis").GetString()!, scale, control.GetProperty("Horizontal").GetBoolean(),
            control.GetProperty("Stretch").GetSingle(), control.GetProperty("Cap").GetSingle());
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot); preview.Seek(0, Matrix.CreateTranslation(position));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            Assert.That(preview.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            using var target = new RenderTarget2D(device, 540, 980, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.DarkSlateGray); device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Camera); device.SetRenderTarget(null);
            var pixels = new Color[540 * 980]; target.GetData(pixels);
            var points = pixels.Select((pixel, offset) => (pixel, offset)).Where(item => (item.pixel.R > 150 || item.pixel.B > 150) && item.pixel.G < 60)
                .Select(item => (X: item.offset % 540, Y: item.offset / 540)).ToArray();
            if (control.GetProperty("VisibleRoiFrames").GetInt32() == 0) Assert.That(points, Is.Empty);
            else
            {
                Assert.That(points, Is.Not.Empty);
                var width = points.Max(point => point.X) - points.Min(point => point.X) + 1;
                var height = points.Max(point => point.Y) - points.Min(point => point.Y) + 1;
                var tolerance = data.GetProperty("PixelTolerance").GetInt32();
                foreach (var expected in control.GetProperty("WidthRange").EnumerateArray()) Assert.That(width, Is.EqualTo(expected.GetInt32()).Within(tolerance));
                foreach (var expected in control.GetProperty("HeightRange").EnumerateArray()) Assert.That(height, Is.EqualTo(expected.GetInt32()).Within(tolerance));
            }
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
            var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, $"native-horizontal-{index:D2}.png")); target.SaveAsPng(stream, 540, 980);
            }
        }
        finally { graphics.Release(); }
    }

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(-1f)]
    public void ZeroAndVerticalVelocity_DoNotInventAFacingPlane(float speed)
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot("Y", Vector3.One, speed: speed));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void HorizontalWorldBasis_IsIndependentOfCameraPosition()
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot("X", Vector3.One));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, Matrix.Identity);
        var expected = simulation.Frame(Camera).Single().Transform;
        Assert.That(simulation.Frame(Camera with { CameraPosition = Vector3.Zero }).Single().Transform, Is.EqualTo(expected));
        Assert.That(simulation.Frame(Camera with { CameraPosition = new(30, 10, 20) }).Single().Transform, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyHorizontalOrientation_RemainsExplicitlyUnsupported()
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(Snapshot("X", Vector3.One).GameBytes));
        document.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "vs_translation_velocity_v2").SetAttributeValue("fragment", "vs_translation_velocity");
        var bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/legacy.xml", _ => Resource("NativeUvAsymmetric.dds")));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.OrientationUnsupported"), Is.True);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(0f)]
    [TestCase(45f)]
    [TestCase(-45f)]
    public void YawOnlyHorizontal_KeepsThePlaneLevelWhenVelocityRisesOrFalls(float elevation)
    {
        var snapshot = YawSnapshot(elevation);
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, Matrix.Identity);
        var transform = simulation.Frame(Camera).Single().Transform;
        Assert.Multiple(() =>
        {
            Assert.That(transform.Right.Y, Is.EqualTo(0).Within(.00001f));
            Assert.That(transform.Up.Y, Is.EqualTo(0).Within(.00001f));
            Assert.That(transform.Right.Length(), Is.EqualTo(1).Within(.00001f));
            Assert.That(transform.Up.Length(), Is.EqualTo(1).Within(.00001f));
            Assert.That(Vector3.Cross(transform.Right, transform.Up).Y, Is.EqualTo(-1).Within(.00001f));
            Assert.That(simulation.Frame(Camera with { CameraPosition = Vector3.Zero }).Single().Transform, Is.EqualTo(transform));
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
        });
    }

    [Test]
    public void YawOnlyHorizontal_UploadedGeometryPreservesTheLevelFootprint()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var target = new RenderTarget2D(device, 256, 256, false, SurfaceFormat.Color, DepthFormat.Depth24);
            Color[] Render(VfxPreviewSnapshot snapshot)
            {
                using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot);
                Assert.That(preview.EmitterCount, Is.EqualTo(1));
                preview.Seek(0, Matrix.CreateTranslation(0, 6, 0));
                device.SetRenderTarget(target); device.Clear(Color.DarkSlateGray); preview.Draw(Camera); device.SetRenderTarget(null);
                var pixels = new Color[256 * 256]; target.GetData(pixels); return pixels;
            }
            var level = Render(Snapshot("X", Vector3.One));
            Assert.That(level.Any(p => p.R > 150 && p.G < 60), Is.True);
            foreach (var elevation in new[] { -45f, 0f, 45f })
                Assert.That(Render(YawSnapshot(elevation)), Is.EqualTo(level), "Vertical velocity must not tilt or mirror this horizontal mode.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(false, -90f)]
    [TestCase(false, 90f)]
    [TestCase(true, -90f)]
    [TestCase(true, 90f)]
    public void WorldVelocityFacing_ReferenceYawDoesNotCollapseTheParticlePlane(bool yawOnly, float yaw)
    {
        var snapshot = yawOnly ? YawSnapshot(0) : Snapshot("X", Vector3.One);
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters.Single().SimulationSpace, Is.EqualTo(2));
        Assert.That(conversion.Emitters.Single().RelativeOrientation, Is.False);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.CreateRotationY(MathHelper.ToRadians(yaw)));
        var transform = simulation.Frame(Camera).Single().Transform;
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Cross(transform.Right, transform.Up).Length(), Is.EqualTo(1).Within(.00001f));
            Assert.That(transform.Right.Y, Is.Zero.Within(.00001f));
            Assert.That(transform.Up.Y, Is.Zero.Within(.00001f));
        });
    }

    [Test]
    public void WorldVelocityFacing_UploadedReferenceYawMatchesTheSameWorldVelocity()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var target = new RenderTarget2D(device, 256, 256, false, SurfaceFormat.Color, DepthFormat.Depth24);
            Color[] Render(VfxPreviewSnapshot snapshot, Matrix rotation)
            {
                using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot);
                Assert.That(preview.EmitterCount, Is.EqualTo(1));
                preview.Seek(0, rotation * Matrix.CreateTranslation(0, 6, 0));
                device.SetRenderTarget(target); device.Clear(Color.DarkSlateGray); preview.Draw(Camera); device.SetRenderTarget(null);
                var pixels = new Color[256 * 256]; target.GetData(pixels); return pixels;
            }
            var expected = Render(Snapshot("Z", Vector3.One), Matrix.Identity);
            Assert.That(expected.Any(p => p.R > 150 && p.G < 60), Is.True);
            var root = Matrix.CreateRotationY(-MathHelper.PiOver2);
            Assert.That(Render(Snapshot("X", Vector3.One), root), Is.EqualTo(expected));
            Assert.That(Render(YawSnapshot(0), root), Is.EqualTo(expected));
        }
        finally { graphics.Release(); }
    }

    private static VfxPreviewSnapshot YawSnapshot(float elevation)
    {
        var snapshot = Snapshot("X", Vector3.One);
        var document = VfxDocument.Read(snapshot.GameBytes);
        document.Root.Descendants("property").Single(p => (string?)p.Attribute("name") == "orientation_mode")
            .SetAttributeValue("value", "Particle only rotating around local Y (horizontal)");
        var rotation = Constant("x", 0).Split(':')[0] + "|" + Constant("y", 0).Split(':')[0] + "|"
            + Constant("z", elevation).Split(':')[0] + ":x,0;0,0|y,0;1,1|z,0;2,2";
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "EmitterOrientation"), rotation));
        return snapshot with { GameBytes = document.Write() };
    }

    private static VfxPreviewSnapshot Snapshot(string axis, Vector3 scale, bool horizontal = true, float stretch = 0, float cap = 1, float speed = 1)
    {
        var angle = axis == "Y" ? 0 : 90;
        // These geometry controls substitute a known birth velocity for native force axes.
        // SpawnSpread now correctly describes elevation with random azimuth, not a second Euler angle.
        var orientation = axis == "Z" ? $"<property name=\"EmitterOrientation\">{Constant("x", 0).Split(':')[0]}|{Constant("y", -90).Split(':')[0]}|{Constant("z", 0).Split(':')[0]}:x,0;0,0|y,0;1,1|z,0;2,2</property>" : "";
        var size = string.Join('|', new[] { ("x", scale.X), ("y", scale.Y), ("z", scale.Z) }.Select(item => Constant(item.Item1, item.Item2).Split(':')[0]))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var modifiers = horizontal ? $"""
            <modifier fragment="vs_translation_velocity_v2"><property name="orientation_mode" value="Horizontally-oriented"/>
            <property name="velocity_stretch_mul" value="{stretch.ToString(CultureInfo.InvariantCulture)}"/>
            <property name="velocity_stretch_cap" value="{cap.ToString(CultureInfo.InvariantCulture)}"/></modifier>
            """ : "<modifier fragment=\"vs_translation\"/>";
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="horizontal" type="continuous_mesh"><property name="InfiniteLife" value="true"/>
            <property name="EmissionRateCurve">{Constant("x", 2)}</property><property name="ParticleLifeTime">{Constant("x", .35f)}</property>
            <property name="ParticleScale">{size}</property><property name="ParticleInitialSpeed">{Constant("x", axis == "-X" ? -speed : speed)}</property>
            <property name="SpawnAngle" x="{angle}" y="{angle}"/>{orientation}
            </emitter></emitters><behaviour_chains><behaviour_chain id="horizontal"><quality value="0">{modifiers}
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/asymmetric.dds"/></modifier>
            <modifier fragment="vs_colour"><property name="curve_colour">{Constant("r", 1).Split(':')[0]}|{Constant("g", 0).Split(':')[0]}|{Constant("b", 1).Split(':')[0]}|{Constant("brightness", 1).Split(':')[0]}:r,0;0,0|g,0;1,1|b,0;2,2|brightness,0;3,3</property></modifier>
            </quality></behaviour_chain></behaviour_chains><effect_render_stacks><effect_render_stack id="horizontal"><property name="scene_view_type" value="FULLRES_DEFERRED_ALPHA"/></effect_render_stack></effect_render_stacks></vfx>
            """);
        return new(bytes, "vfx/horizontal.xml", _ => Resource("NativeUvAsymmetric.dds"));
    }
    private static string Constant(string axis, float value) => $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:{axis},0;0,0";
    private static Vector3 Vector(JsonElement values) { var array = values.EnumerateArray().Select(value => value.GetSingle()).ToArray(); return new(array[0], array[1], array[2]); }
    private static byte[] Resource(string suffix)
    {
        var assembly = typeof(VfxHorizontalGeometryTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
}
