#nullable enable
using System.Globalization;
using System.Security.Cryptography;
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
public class VfxOuterAtlasZeroTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(4, 4, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    public static IEnumerable<TestCaseData> NativeCases()
    {
        using var fixture = JsonDocument.Parse(Resource("NativeOuterAtlasZero.json"));
        foreach (var item in fixture.RootElement.GetProperty("Cases").EnumerateArray())
            foreach (var animated in new[] { false, true })
                yield return new TestCaseData(item.Clone(), animated).SetName("OuterAtlasNative_"
                    + string.Join('_', item.GetProperty("Raw").EnumerateArray().Select(value => value.GetInt32()))
                    + (animated ? "_Animated" : "_Static"));
    }

    [TestCaseSource(nameof(NativeCases))]
    public void ZeroOuterDimensions_UseTheNativePerAxisIdentityAndKeepInnerCells(JsonElement item, bool animated)
    {
        var raw = item.GetProperty("Raw");
        var bytes = Source(raw[0].GetInt32(), raw[1].GetInt32(), raw[2].GetInt32(), animated);
        var result = Convert(bytes);
        var emitter = result.Emitters.Single();
        var expected = item.GetProperty("FloatBits").EnumerateArray().Select(value => BitConverter.UInt32BitsToSingle(value.GetUInt32())).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(1f / emitter.Atlas.OuterColumns, Is.EqualTo(expected[0]));
            Assert.That(1f / emitter.Atlas.OuterRows, Is.EqualTo(expected[1]));
            Assert.That(emitter.Render.AtlasColumns, Is.EqualTo(2));
            Assert.That(emitter.Render.AtlasRows, Is.EqualTo(2));
            Assert.That(emitter.Render.AtlasBlend, Is.EqualTo(animated ? 1 : 0));
        });
        var simulation = new TotalWarVfxSimulation(result); simulation.Seek(.375f, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        var x = .5f;
        var y = animated ? 0 : .5f;
        Assert.Multiple(() =>
        {
            Assert.That(particle.UV, Is.EqualTo(new Vector4(expected[2] + x * expected[0],
                expected[3] + y * expected[1], .5f * expected[0], .5f * expected[1])));
            Assert.That(particle.Flipbook, Is.EqualTo(animated ? 1.5f : 3f));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void ZeroOuterDimensions_KeepTheDeferredDecalLayerAndItsAnimatedUv()
    {
        var bytes = Source(0, 0, 0, animated: true, decal: true);
        var result = Convert(bytes);
        Assert.That(result.Emitters.Single().Kind, Is.EqualTo(2), "The frost-trail failure is a decal, not an ordinary sprite.");
        var simulation = new TotalWarVfxSimulation(result); simulation.Seek(.375f, Matrix.Identity);
        var particle = simulation.Frame(Camera with { VfxGroundHeight = 0 }).Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.UV, Is.EqualTo(new Vector4(.5f, 0, .5f, .5f)));
            Assert.That(particle.Flipbook, Is.EqualTo(1.5f));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase("atlas_number_of_cells_x", -1)]
    [TestCase("atlas_number_of_cells_y", -1)]
    [TestCase("atlas_number_of_cells_x", .5f)]
    [TestCase("atlas_number_of_cells_y", .5f)]
    [TestCase("atlas_number_of_cells_x", 257)]
    [TestCase("number_of_cells_x", 0)]
    [TestCase("number_of_cells_y", 0)]
    [TestCase("atlas_cell", 1)]
    public void OuterZeroSentinel_DoesNotOpenUnprovedValuesOrZeroInnerDimensions(string field, float value)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(Source(0, 0, 0, animated: false)));
        document.Descendants("property").Single(node => (string?)node.Attribute("name") == field)
            .SetAttributeValue("value", value.ToString(CultureInfo.InvariantCulture));
        var bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/outer-atlas-control.xml", _ => Resource("NativeStaticAtlas.dds")));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Is.Empty);
            Assert.That(result.Diagnostics.Any(d => d.Field == field && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase(0, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(1, 1)]
    public void ZeroOuterDimensions_PreserveBothTextureSamplesAndTheirFractionalBlend(int outerX, int outerY)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        using var fixture = JsonDocument.Parse(Resource("NativeStaticAtlasCells.json"));
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(Resource("NativeStaticAtlas.dds"))),
            Is.EqualTo(fixture.RootElement.GetProperty("TextureSha256").GetString()));
        var bytes = Source(outerX, outerY, 0, animated: true, columns: 8, rows: 8);
        var time = 14.5f / 64;
        var simulation = new TotalWarVfxSimulation(Convert(bytes)); simulation.Seek(time, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.UV, Is.EqualTo(new Vector4(.75f, .125f, .125f, .125f)));
            Assert.That(particle.Flipbook, Is.EqualTo(14.5f));
        });
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(bytes, "vfx/outer-atlas-control.xml", _ => Resource("NativeStaticAtlas.dds")));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            preview.Seek(time, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Camera); device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels);
            // The retained native atlas control has blue and red in these two adjacent cells.
            for (var y = 31; y <= 33; y++)
                for (var x = 31; x <= 33; x++)
                    Assert.That(Vector4.Distance(pixels[y * 64 + x], new(.5f, 0, .5f, 1)), Is.LessThan(.002f),
                        $"Both independently known native atlas colours must contribute equally at {x},{y}.");
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        }
        finally { graphics.Release(); }
    }

    private static VfxConversion Convert(byte[] bytes)
    {
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/outer-atlas-control.xml", _ => Resource("NativeStaticAtlas.dds")));
        Assert.That(result.Emitters, Has.Count.EqualTo(1), string.Join(';', result.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return result;
    }

    private static byte[] Source(int outerX, int outerY, int outerCell, bool animated, int columns = 2, int rows = 2, bool decal = false)
    {
        var atlas = new XElement("modifier", new XAttribute("fragment", animated ? "vs_animated_texture" : "vs_cell_texture"),
            Property("atlas_number_of_cells_x", outerX), Property("atlas_number_of_cells_y", outerY), Property("atlas_cell", outerCell));
        if (animated)
            atlas.Add(Property("no_cells_xy", FormattableString.Invariant($"float2({columns},{rows})")),
                Property("no_loops", "float2(1,1)"), Property("time_mode", "using_particle_lifetime"), Property("cell_blend", "true"));
        else
            atlas.Add(Property("number_of_cells_x", columns), Property("number_of_cells_y", rows), Property("cell", 3));
        var emitter = new XElement("emitter", new XAttribute("id", "atlas"), new XAttribute("type", "continuous_mesh"),
            Property("InfiniteLife", "true"), new XElement("property", new XAttribute("name", "EmissionRateCurve"), Constant("x", .1f)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Constant("x", 1)),
            new XElement("property", new XAttribute("name", "ParticleScale"), Constant("xyz", 2)));
        var root = new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "atlas"),
                new XElement("quality", new XAttribute("value", 0), new XElement("modifier", new XAttribute("fragment", "vs_translation")),
                    new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                        new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "vfx/atlas.dds"))), atlas))));
        if (decal)
        {
            emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL"),
                Property("ParticleRelativeSpawnDirection", "RELATIVE_TO_TERRAIN_NORMAL"), Property("ParticlesSpawnOnTerrain", "true"));
            foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
                emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", -90), new XAttribute("y", 0), new XAttribute("z", 0)));
            root.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "atlas"),
                Property("scene_view_type", "DEFERRED_DECAL"))));
        }
        return Encoding.UTF8.GetBytes(new XDocument(root).ToString(SaveOptions.DisableFormatting));
    }

    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));

    private static string Constant(string axes, float value) => string.Join('|', axes.Select(axis =>
        FormattableString.Invariant($"{axis}_min,0,0;0,{value},linear,linear,0,0,0,0,1"))) + ":"
        + string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));

    private static byte[] Resource(string suffix)
    {
        var assembly = typeof(VfxOuterAtlasZeroTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
}
