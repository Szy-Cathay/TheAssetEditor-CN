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
public class VfxStaticAtlasCellTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(4, 4, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void StaticAtlasLinearPixels_RetainTheOriginalPureColour(int cell)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        using var fixture = JsonDocument.Parse(Resource("NativeStaticAtlasCells.json"));
        var colour = fixture.RootElement.GetProperty("Cases")[cell].GetProperty("StableCohortColours").EnumerateObject().Single().Name;
        var expected = colour switch
        {
            "Blue" => new Vector4(0, 0, 1, 1),
            "Red" => new Vector4(1, 0, 0, 1),
            _ => throw new InvalidDataException("Unexpected native atlas control colour: " + colour),
        };
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(Resource("NativeStaticAtlas.dds"))),
            Is.EqualTo(fixture.RootElement.GetProperty("TextureSha256").GetString()));
        var snapshot = Snapshot(cell, false);
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot)); simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().UV, Is.EqualTo(new Vector4(cell % 2 == 0 ? .75f : .875f, .125f, .125f, .125f)),
            "The outer cell and wrapped inner cell must reach the renderer unchanged.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot); preview.Seek(.1f, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Camera); device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels);
            TestContext.Progress.WriteLine($"Static atlas cell={cell}, source={colour}, linear centre={pixels[32 * 64 + 32]}");
            for (var y = 31; y <= 33; y++)
                for (var x = 31; x <= 33; x++)
                    Assert.That(pixels[y * 64 + x], Is.EqualTo(expected), $"Source radiance at {x},{y} must remain exact before display mapping.");
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void StaticAtlasPixels_MatchNativeWrappedCells(int cell)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        using var fixture = JsonDocument.Parse(Resource("NativeStaticAtlasCells.json"));
        var expected = fixture.RootElement.GetProperty("Cases")[cell].GetProperty("StableCohortColours").EnumerateObject().Single().Name;
        // Shipped hdr_to_screen + default LUT map unit red to 239 and unit blue to 251.
        var expectedDisplay = expected switch
        {
            "Blue" => new Color(0, 0, 251, 255),
            "Red" => new Color(239, 0, 0, 255),
            _ => throw new InvalidDataException("Unexpected native atlas control colour: " + expected),
        };
        var snapshot = Snapshot(cell, false);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot); preview.Seek(.1f, Matrix.Identity);
            Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Camera); device.SetRenderTarget(null); var pixels = new Color[4096]; target.GetData(pixels);
            var centre = pixels[32 * 64 + 32];
            TestContext.Progress.WriteLine($"Static atlas cell={cell}, source={expected}, display centre={centre}");
            for (var y = 31; y <= 33; y++)
                for (var x = 31; x <= 33; x++)
                    Assert.That(pixels[y * 64 + x], Is.EqualTo(expectedDisplay),
                        $"All display channels at {x},{y} must match the native transfer, independently of the linear-colour check.");
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(99)]
    public void RandomAtlas_AddsTheBaseCellAndWrapsWithoutChangingBirthTiming(int baseCell)
    {
        var baseline = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(Snapshot(0, true)));
        var candidate = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(Snapshot(baseCell, true)));
        var states = new HashSet<int>();
        for (var birth = 0; birth < 64; birth++)
        {
            var time = birth * .5f + .1f;
            baseline.Seek(time, Matrix.Identity); candidate.Seek(time, Matrix.Identity);
            var original = baseline.Frame(Camera).MinBy(p => p.Time);
            var actual = candidate.Frame(Camera).MinBy(p => p.Time);
            var first = (int)MathF.Round((original.UV.X - .75f) * 8);
            var next = (int)MathF.Round((actual.UV.X - .75f) * 8);
            Assert.That(next, Is.EqualTo((first + baseCell) % 2));
            Assert.That(actual.Time, Is.EqualTo(original.Time));
            Assert.That(actual.Transform, Is.EqualTo(original.Transform));
            states.Add(next);
        }
        Assert.That(states, Is.EquivalentTo(new[] { 0, 1 }));
    }

    [Test]
    public void StaticBaseOffset_DoesNotChangeAnimatedRandomSelection()
    {
        var baseline = Animated(0);
        var candidate = Animated(1);
        for (var birth = 0; birth < 32; birth++)
        {
            var time = birth * .5f + .1f;
            baseline.Seek(time, Matrix.Identity); candidate.Seek(time, Matrix.Identity);
            Assert.That(candidate.Frame(Camera), Is.EqualTo(baseline.Frame(Camera)));
        }

        static TotalWarVfxSimulation Animated(int cell)
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(Snapshot(cell, true).GameBytes));
            var modifier = document.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "vs_cell_texture");
            modifier.SetAttributeValue("fragment", "vs_animated_texture");
            modifier.Add(new XElement("property", new XAttribute("name", "no_cells_xy"), new XAttribute("value", "float2(2,1)")));
            var conversion = TotalWarVfxConverter.Convert(new(Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)),
                "vfx/animated.xml", _ => Resource("NativeStaticAtlas.dds")));
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            return new(conversion);
        }
    }

    [TestCase(-2)]
    [TestCase(-1)]
    [TestCase(2.5f)]
    public void UnconfirmedStaticCellValues_KeepExplicitDiagnostics(float cell)
    {
        var snapshot = Snapshot(cell, true);
        var result = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d => d.Field == "cell" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    private static VfxPreviewSnapshot Snapshot(float cell, bool random)
    {
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="atlas" type="continuous_mesh"><property name="InfiniteLife" value="true"/>
            <property name="EmissionRateCurve">x,0,0;0,2,linear,linear,0,0,0,0,1:x,0;0,0</property>
            <property name="ParticleLifeTime">x,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0</property>
            <property name="ParticleScale">x_min,0,0;0,1,linear,linear,0,0,0,0,1|y_min,0,0;0,1,linear,linear,0,0,0,0,1|z_min,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2</property>
            </emitter></emitters>
            <behaviour_chains><behaviour_chain id="atlas"><quality value="0"><modifier fragment="vs_translation"/>
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/atlas.dds"/></modifier>
            <modifier fragment="vs_cell_texture"><property name="atlas_number_of_cells_x" value="4"/><property name="atlas_number_of_cells_y" value="8"/>
            <property name="atlas_cell" value="7"/><property name="number_of_cells_x" value="2"/><property name="number_of_cells_y" value="1"/>
            <property name="cell" value="{cell.ToString(CultureInfo.InvariantCulture)}"/><property name="randomize_cell" value="{random.ToString().ToLowerInvariant()}"/>
            </modifier></quality></behaviour_chain></behaviour_chains></vfx>
            """);
        return new(bytes, "vfx/atlas.xml", _ => Resource("NativeStaticAtlas.dds"));
    }
    private static byte[] Resource(string suffix)
    {
        var assembly = typeof(VfxStaticAtlasCellTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
}
