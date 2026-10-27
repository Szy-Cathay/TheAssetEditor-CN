#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxOrdinaryAppearanceTableTests
{
    public static IEnumerable<TestCaseData> NativeRows()
    {
        foreach (var row in Fixture().GetProperty("Rows").EnumerateArray())
            yield return new TestCaseData(row.Clone()).SetName($"Native128Half_{row.GetProperty("Layer").GetString()}_{row.GetProperty("SourceField").GetString()}_{row.GetProperty("NativeRow").GetInt32()}");
    }

    [TestCaseSource(nameof(NativeRows))]
    public void ConvertedTableMatchesEveryOriginalNativeHalfTexel(JsonElement row)
    {
        var definition = ConvertLayer(row.GetProperty("Layer").GetString()!);
        var field = row.GetProperty("SourceField").GetString();
        var table = Table(definition, field!);
        Assert.That(table, Is.Not.Null, "Only the explicitly calibrated ordinary sprite path opts in; direct curve APIs remain analytic.");
        var rawRow = row.GetProperty("NativeRow").GetInt32();
        var upper = (rawRow & 1) != 0;
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        for (var index = 0; index < 128; index++)
        {
            var x = table!.SampleBoundAt("x", index, upper);
            var values = field == "ParticleScale" ? new[] { x, table.SampleBoundAt("y", index, upper), table.SampleBoundAt("z", index, upper), 1f } : new[] { x, x, x, x };
            foreach (var value in values) writer.Write(BitConverter.HalfToUInt16Bits((Half)value));
        }
        writer.Flush();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(memory.ToArray())), Is.EqualTo(row.GetProperty("NativeSha256").GetString()));
    }

    [Test]
    public void NativeHardwareGoldenAndPortableSamplingRemainExplicitlyDistinct()
    {
        var definition = ConvertLayer("_arrow_glowing_large");
        var sample = Fixture().GetProperty("ActualSample");
        var table = Table(definition, "curve_alpha");
        Assert.That(table, Is.Not.Null);
        var phase = sample.GetProperty("Phase").GetSingle();
        var portable = table!.Sample("x", phase, 0);
        Assert.That(portable, Is.EqualTo(sample.GetProperty("PortableLinear").GetSingle()));
        Assert.That(Table(definition, "ParticleInitialAlpha")!.Sample("x", 1, 0), Is.EqualTo(sample.GetProperty("InitialAtBirthPhaseOne").GetSingle()));
        var hardware = sample.GetProperty("NativeHardwareSample").GetSingle();
        Assert.That(hardware, Is.EqualTo(.7158203125f));
        Assert.That(hardware - portable, Is.EqualTo(.00024408102035522461f), "Do not hide the real hardware-filter precision residual by adding a guessed phase or global half rounding.");
        Assert.That(definition.Alpha.Sample("alpha", phase, 0), Is.EqualTo(.7052647f).Within(.0000001f), "The existing direct curve API keeps its original analytic contract.");
    }

    [TestCase("unmarked")]
    [TestCase("decal")]
    [TestCase("distortion")]
    public void UncalibratedShaderFamiliesKeepTheExistingAnalyticFields(string control)
    {
        var definition = ConvertLayer("_arrow_glowing_large", control);
        Assert.That(Table(definition, "ParticleScale"), Is.Null);
        Assert.That(Table(definition, "curve_alpha"), Is.Null);
        Assert.That(Table(definition, "ParticleInitialAlpha"), Is.Null);
    }

    [TestCase("offset")]
    [TestCase("bezier")]
    [TestCase("different-bounds")]
    public void UncalibratedAlphaCombinationsRetainTheAnalyticFallback(string control)
    {
        var definition = ConvertLayer("_arrow_glowing_large", control);
        Assert.That(Table(definition, "curve_alpha"), Is.Null);
        Assert.That(definition.Alpha.Sample("alpha", .25, .5f), Is.GreaterThan(0));
    }

    private static VfxSpriteNativeScaleCurve? Table(VfxPreviewEmitter definition, string field)
    {
        var name = field switch { "ParticleScale" => "SpriteScaleTable", "ParticleInitialAlpha" => "SpriteInitialAlphaTable", _ => "SpriteAlphaTable" };
        return (VfxSpriteNativeScaleCurve?)typeof(VfxPreviewEmitter).GetProperty(name)?.GetValue(definition);
    }

    private static VfxPreviewEmitter ConvertLayer(string name, string control = "")
    {
        var rows = Fixture().GetProperty("Rows").EnumerateArray().Where(row => row.GetProperty("Layer").GetString() == name).ToArray();
        string Curve(string field) => rows.First(row => row.GetProperty("SourceField").GetString() == field).GetProperty("SourceCurve").GetString()!;
        var pass = control switch { "unmarked" => null, "decal" => "DEFERRED_DECAL", "distortion" => "DISTORTION", _ => "FULLRES_DEFERRED_ALPHA" };
        var document = VfxDocument.Read(VfxSimpleEmissiveTests.Source(.7f, true, Vector3.One, 0, pass, premultiplied: true));
        var emitter = document.Root.Descendants("emitter").Single();
        foreach (var field in new[] { "ParticleScale", "ParticleInitialAlpha" })
        {
            emitter.Elements("property").Where(node => (string?)node.Attribute("name") == field).Remove();
            emitter.Add(new XElement("property", new XAttribute("name", field), Curve(field)));
        }
        var alpha = document.Root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "vs_vertex_alpha");
        alpha.Elements("property").Where(node => (string?)node.Attribute("name") == "curve_alpha").Remove();
        var text = Curve("curve_alpha");
        if (control == "bezier")
        {
            const string points = ";0,0,bezier,bezier,0,0,.25,0,0;1,1,bezier,bezier,-.25,0,0,0,0";
            text = "alpha_min,0,1" + points + "|alpha_max,0,1" + points + ":alpha,0;0,1";
        }
        if (control == "different-bounds")
        {
            var channels = text.Split(':');
            var max = channels[0].Split('|'); max[1] = max[1].Replace("0.148509,1.0", "0.148509,0.5", StringComparison.Ordinal);
            text = string.Join('|', max) + ":alpha,1;0,1";
        }
        alpha.Add(new XElement("property", new XAttribute("name", "curve_alpha"), text));
        if (control == "offset") alpha.Add(new XElement("property", new XAttribute("name", "alpha_START_offset"), new XAttribute("value", "float2(0.1,0.1)")));
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "DIAG/native-ordinary-tables.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return conversion.Emitters[0];
    }

    private static JsonElement Fixture()
    {
        var assembly = typeof(VfxOrdinaryAppearanceTableTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeOrdinaryAppearanceTables.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
