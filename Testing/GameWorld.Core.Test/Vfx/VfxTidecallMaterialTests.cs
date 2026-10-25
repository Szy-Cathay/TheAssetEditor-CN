#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxTidecallMaterialTests
{
    private const string Radial = @"vfx\materials\wh3_storm_dragon_water_splash_radial.xml.material";
    private const string Up = @"vfx\materials\wh3_storm_dragon_water_splash_up.xml.material";
    private readonly Dictionary<string, (PackFile File, string Pack)> _official = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _origins = new(StringComparer.OrdinalIgnoreCase);

    [OneTimeSetUp]
    public void LoadOnlyExplicitOfficialManifestPacks()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !Directory.Exists(data))
            Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA must point to the original game data directory.");
        var manifestPath = Path.Combine(data!, "manifest.txt");
        if (!File.Exists(manifestPath)) Assert.Ignore("Official manifest required; no Mod or arbitrary Pack fallback.");
        var manifest = File.ReadAllLines(manifestPath).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1 && parts[0].Trim().EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(parts => parts[0].Trim(), parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var loader = new PackFileContainerLoader(null!);
        var packs = new[] { "vfx.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" }.Select(name =>
        {
            Assert.That(manifest.ContainsKey(name), Is.True, "Required Pack must belong to the official manifest: " + name);
            var path = Path.Combine(data!, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]), "Official Pack size must match its manifest.");
            return loader.Load(path)!;
        }).OrderBy(pack => pack.Header.LoadOrder).ThenBy(pack => pack.Name).ToArray();
        foreach (var pack in packs)
            foreach (var item in pack.FileList) _official[Key(item.Key)] = (item.Value, pack.SystemFilePath);
        TestContext.Progress.WriteLine("Tidecall CPU original-source manifest SHA256=" + Hash(File.ReadAllBytes(manifestPath)));
    }

    [TestCase(Radial, 0f)]
    [TestCase(Up, .2f)]
    public void OriginalWaterMaterials_RetainAllAuthoredParametersAndLogicalTextures(string path, float displacement)
    {
        var bytes = Read(path);
        var beforeHash = Hash(bytes);
        var root = Parse(bytes);
        Assert.That(root.Element("shader")!.Value, Is.EqualTo("shaders/wh2_vfx_cst_tidecall.xml.shader"));
        var material = VfxTidecallMaterial.Read(root);
        var expected = root.Elements("params").Elements("param")
            .SelectMany(node => node.Element("value")!.Value.Split(','))
            .Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        var slots = new[] { "disintegrate_1_texture", "disintegrate_mask_texture", "displacement_texture", "dither_texture",
            "foam_a_texture", "foam_b_texture", "foam_mask", "noise_a_texture", "noise_b_texture", "normal", "wave_mask_texture", "y_edge_mask" };
        var expectedPaths = slots.Select(slot => root.Elements("textures").Elements("texture")
            .Single(node => node.Element("slot")!.Value == "t_xml_" + slot).Element("source")!.Value).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(expected, Has.Length.EqualTo(92), "The unmodified official document must actually contain the 368-byte source layout.");
            Assert.That(material.Parameters, Has.Length.EqualTo(104));
            Assert.That(material.Parameters.Take(92), Is.EqualTo(expected), "Keep every authored component in the source layout order.");
            Assert.That(material.Parameters[19], Is.EqualTo(displacement), "The two original splashes exercise zero and nonzero vertex displacement.");
            Assert.That(material.TexturePaths, Is.EqualTo(expectedPaths), "Aliasing backend bindings must not drop any authored logical slot.");
            Assert.That(material.TexturePaths, Has.Length.EqualTo(12));
            Assert.That(material.TexturePaths.Select(Key).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(Hash(bytes), Is.EqualTo(beforeHash));
        });
        foreach (var texture in material.TexturePaths)
            Assert.That(Read(texture).Length, Is.GreaterThan(128), "Each original logical binding must resolve to its actual DDS: " + texture);
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Material = path, OriginalSha256 = beforeHash,
            SourceParameters = expected, LogicalTextures = material.TexturePaths, Origins = _origins.Values,
            Scope = "CPU preservation of current official material parameters and original texture bindings; no GPU or native visual acceptance." }));
    }

    [TestCase("duplicate-parameter", "mesh_alpha", "Vfx.Preview.AmbiguousField")]
    [TestCase("invalid-number", "normal_pan", "Vfx.Preview.RangeUnsupported")]
    [TestCase("invalid-arity", "normal_pan", "Vfx.Preview.InvalidData")]
    [TestCase("missing-texture", "t_xml_wave_mask_texture", "Vfx.Preview.MissingResource")]
    public void InvalidOfficialDerivedCpuInputs_AreDiagnosedWithoutFallback(string change, string field, string reason)
    {
        var original = Read(Radial);
        var originalHash = Hash(original);
        var root = Parse(original);
        if (change == "missing-texture")
            root.Elements("textures").Elements("texture").Single(node => node.Element("slot")!.Value == field).Remove();
        else
        {
            var parameter = root.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == field);
            if (change == "duplicate-parameter") parameter.AddAfterSelf(new XElement(parameter));
            else parameter.Element("value")!.Value = change == "invalid-number" ? "invalid,0" : "1";
        }
        var exception = Assert.Throws<VfxMappingException>(() => VfxTidecallMaterial.Read(root));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Field, Is.EqualTo(field));
            Assert.That(exception.Reason, Is.EqualTo(reason));
            Assert.That(Hash(original), Is.EqualTo(originalHash), "Invalid-input CPU controls must not alter official source bytes.");
        });
    }

    private byte[] Read(string path)
    {
        if (!_official.TryGetValue(Key(path), out var entry)) throw new FileNotFoundException("Original official resource: " + path);
        var bytes = entry.File.DataSource.ReadData();
        _origins[Key(path)] = new { Path = path, SourcePack = entry.Pack, Bytes = bytes.Length, Sha256 = Hash(bytes) };
        return bytes;
    }

    private static XElement Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return XElement.Load(stream);
    }

    private static string Key(string path) => path.Replace('/', '\\').Trim().ToLowerInvariant();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
