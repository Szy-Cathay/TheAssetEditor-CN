#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSimpleVelocityLightingTests
{
    [OneTimeSetUp]
    public void RecordLoadedCore()
    {
        var path = typeof(TotalWarVfxConverter).Assembly.Location;
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { CoreModule = path, CoreSha256 = hash }));
        var expected = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_CORE_SHA256");
        if (!string.IsNullOrWhiteSpace(expected)) Assert.That(hash, Is.EqualTo(expected).IgnoreCase);
    }

    [TestCase("vs_translation_velocity")]
    [TestCase("vs_translation_velocity_v2")]
    public void DIAG_SimpleVelocityLighting_PreservesTheNativeAmbientModeWithoutANormalMap(string fragment)
    {
        var source = Source(1, true, fragment);
        var converted = ConvertSource(source);
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var emitter = converted.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Alignment, Is.EqualTo(1));
            Assert.That(emitter.Render.LightingMode, Is.EqualTo(1),
                "Native simple-lighting PS uses ambient top and emissive, independently of the velocity sprite's normal basis.");
            Assert.That(emitter.Render.NormalIndex, Is.EqualTo(-1));
            Assert.That(emitter.Render.Emissive, Is.EqualTo(.7f), "Retain the authored value; do not replace it with a fitted gain.");
            Assert.That(emitter.CoveredColourMultiplier, Is.EqualTo(1));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(0, true, 1)]
    [TestCase(3, true, 1)]
    [TestCase(0, false, 2)]
    [TestCase(3, false, 2)]
    [TestCase(1, false, 0)]
    public void DIAG_LightingScope_KeepsExistingBillboardsAndRejectsUncalibratedVelocityNormals(int alignment, bool simple, int expectedMode)
    {
        var converted = ConvertSource(Source(alignment, simple, "vs_translation_velocity_v2"));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var emitter = converted.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Alignment, Is.EqualTo(alignment));
            Assert.That(emitter.Render.LightingMode, Is.EqualTo(expectedMode));
            Assert.That(emitter.Render.NormalIndex, Is.EqualTo(-1));
        });
        if (alignment == 1 && !simple)
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ps_lighting" && d.ReasonKey == "Vfx.Preview.LightingCandidate"), Is.True);
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void CompleteOfficialBlowpipe_AllOriginalQualityLevels_MapBothSimpleVelocityLayers(string quality)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("The original game manifest is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(row => row.Length > 1)
            .ToDictionary(row => row[0], row => long.Parse(row[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack" })
        {
            var path = Path.Combine(data!, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]), "Only official manifest packs may resolve these original samples.");
            foreach (var pair in new PackFileContainerLoader(null!).Load(path)!.FileList) files[pair.Key.Replace('/', '\\')] = pair.Value;
        }
        const string rootPath = @"vfx\wh2_dlc17_weapon_trail_blowpipe_hero.xml";
        var source = files[rootPath].DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo("dc9b7b8d10bbd9b9f76a66ea5ff45bd0a6fbc6f2911caffef831fdef2323b5f3"));
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? Resolve(string path)
        {
            var key = path.Replace('/', '\\');
            if (!files.TryGetValue(key, out var file)) return null;
            var bytes = file.DataSource.ReadData(); resolved[key] = Convert.ToHexStringLower(SHA256.HashData(bytes)); return bytes;
        }
        var converted = TotalWarVfxConverter.Convert(new(source, rootPath, Resolve, quality));
        Assert.That(converted.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "_rifle_100ms", "_arrow_glowing_large" }));
        foreach (var emitter in converted.Emitters)
        {
            Assert.Multiple(() =>
            {
                Assert.That(emitter.Alignment, Is.EqualTo(1));
                Assert.That(emitter.Render.LightingMode, Is.EqualTo(1), $"{emitter.Name} q{quality} must retain the original simple-lighting material.");
                Assert.That(emitter.Render.NormalIndex, Is.EqualTo(-1));
                Assert.That(emitter.Render.Emissive, Is.EqualTo(.7f));
                Assert.That(emitter.CoveredColourMultiplier, Is.EqualTo(1));
            });
        }
        Assert.Multiple(() =>
        {
            Assert.That(resolved[@"vfx\textures\generic_cells_b_diffuse.dds"], Is.EqualTo("0f9f9d442fc80bd338c9826d25df2d0fef4820918c9a68d5fb32d6650cc989ce"));
            Assert.That(files[rootPath].DataSource.ReadData(), Is.EqualTo(source));
        });
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { OfficialRoot = rootPath, Quality = quality, Resolved = resolved }));
    }

    private static VfxConversion ConvertSource(byte[] source)
        => TotalWarVfxConverter.Convert(new(source, "DIAG/simple-velocity-lighting.xml", _ => VfxMaterialCompositingTests.Texture(128, 255)));

    private static byte[] Source(int alignment, bool simple, string fragment)
    {
        var document = VfxDocument.Read(VfxSimpleEmissiveTests.Source(.7f, simple, new Vector3(.3f), pass: "HALFRES_DEFERRED_ALPHA", premultiplied: true));
        if (alignment != 0)
        {
            var quality = document.Root.Descendants("quality").Single();
            quality.Elements("modifier").Where(node => (string?)node.Attribute("fragment") == "vs_translation").Remove();
            quality.Add(new XElement("modifier", new XAttribute("fragment", fragment),
                new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", alignment == 1 ? "Camera-facing velocity-aligned" : "Camera-aligned with stretching factor")),
                new XElement("property", new XAttribute("name", "velocity_stretch_mul"), VfxLibraryVisibilityTests.Constant("x", 0)),
                new XElement("property", new XAttribute("name", "velocity_stretch_cap"), new XAttribute("value", 1000))));
        }
        return document.Write();
    }
}
