#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxWsModelMaterialSelectionTests
{
    [Test]
    public void UniqueUnindexedMaterial_WithOneLodAndPartUsesTheWrapperMaterial()
    {
        var (plan, reads) = Diagnostic("<material>vfx/character.xml.material</material>");
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Has.Count.EqualTo(1));
            Assert.That(reads, Does.Contain("vfx/character.xml.material"));
            Assert.That(plan.TexturePaths, Does.Contain("vfx/shared.dds"));
            Assert.That(plan.TexturePaths, Does.Not.Contain("vfx/texture-0.dds"), "The RMV's embedded texture is not the wrapper's selected material.");
            Assert.That(plan.Emitters.Single().Render.OpaqueModel, Is.EqualTo(1));
        });
    }

    [TestCase(1, 1, 2)]
    [TestCase(2, 1, 1)]
    [TestCase(1, 2, 1)]
    public void UnindexedMaterial_RejectsAmbiguousAssignments(int parts, int lods, int materials)
    {
        var entries = string.Concat(Enumerable.Range(0, materials).Select(i => $"<material>vfx/ambiguous-{i}.xml.material</material>"));
        var (plan, reads) = Diagnostic(entries, parts, lods);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Is.Empty);
            Assert.That(plan.Diagnostics.Any(d => d.Field == "material" && d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
            Assert.That(reads.Any(p => p.Contains("ambiguous-", StringComparison.Ordinal)), Is.False,
                "Do not guess a wildcard material for multiple parts, LODs or candidate entries.");
        });
    }

    [Test]
    public void ExactMaterialIndex_TakesPriorityOverAnUnindexedEntry()
    {
        var (plan, reads) = Diagnostic("<material>vfx/unused.xml.material</material><material lod_index=\"0\" part_index=\"0\">vfx/character.xml.material</material>");
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Has.Count.EqualTo(1));
            Assert.That(reads, Does.Contain("vfx/character.xml.material"));
            Assert.That(reads, Does.Not.Contain("vfx/unused.xml.material"));
            Assert.That(plan.TexturePaths, Does.Contain("vfx/shared.dds"));
        });
    }

    [TestCase("")]
    [TestCase(" lod_index=\"0\" part_index=\"0\"")]
    public void SelectedMaterial_MissingItsRealTextureStillRejectsTheLayer(string indexes)
    {
        var (plan, _) = Diagnostic($"<material{indexes}>vfx/character.xml.material</material>", missingTexture: true);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Is.Empty);
            Assert.That(plan.Diagnostics.Any(d => d.Field == @"vfx\shared.dds" && d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
            Assert.That(plan.Textures, Is.Empty, "Do not substitute a white texture or fall back to the embedded RMV material.");
        });
    }

    [Test]
    public void DuplicateExactMaterialIndexes_AreDiagnosedWithoutGuessing()
    {
        var entry = "<material lod_index=\"0\" part_index=\"0\">vfx/character.xml.material</material>";
        var (plan, _) = Diagnostic(entry + entry);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Emitters, Is.Empty);
            Assert.That(plan.Diagnostics.Any(d => d.Field == "material" && d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
        });
    }

    [Test]
    public void CompleteOfficialSlashCircle_UsesItsOriginalWrapperAndSixRealMaterialSlots()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Official resources require explicit VFX opt-in.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !Directory.Exists(data)) Assert.Ignore("Original game data is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(l => l.Split('\t')).Where(p => p.Length > 1)
            .ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var loader = new PackFileContainerLoader(null!);
        var packs = new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" }.Select(name =>
        {
            var path = Path.Combine(data!, name);
            Assert.That(manifest.TryGetValue(name, out var size), Is.True);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(size));
            return loader.Load(path)!;
        }).OrderBy(p => p.Header.LoadOrder).ThenBy(p => p.Name).ToArray();
        var index = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        foreach (var file in pack.FileList) index[file.Key.Replace('/', '\\')] = file.Value;
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? Read(string path)
        {
            var key = path.Replace('/', '\\');
            if (!index.TryGetValue(key, out var file)) return null;
            if (!originals.TryGetValue(key, out var bytes)) originals[key] = bytes = file.DataSource.ReadData();
            return bytes;
        }
        const string root = @"vfx\wh_main_ability_melee_slash_circle.xml";
        const string wrapper = @"vfx\models\wh_spell_targeting_central_c.wsmodel";
        var source = Read(root) ?? throw new FileNotFoundException(root);
        var wrapperBytes = Read(wrapper) ?? throw new FileNotFoundException(wrapper);
        // DIAG supplies explicit zero indexes only; original material, geometry, textures and effect remain intact.
        var diagnostic = XElement.Parse(Encoding.UTF8.GetString(wrapperBytes));
        var assignment = diagnostic.Element("materials")!.Elements("material").Single();
        Assert.That(assignment.Attributes(), Is.Empty);
        assignment.SetAttributeValue("lod_index", "0"); assignment.SetAttributeValue("part_index", "0");
        var diagnosticBytes = Encoding.UTF8.GetBytes(diagnostic.ToString(SaveOptions.DisableFormatting));
        var control = TotalWarVfxConverter.Convert(new(source, root, path => path.Replace('/', '\\').Equals(wrapper, StringComparison.OrdinalIgnoreCase) ? diagnosticBytes : Read(path), "1"));
        Assert.That(control.Emitters.Any(e => e.Name == "3D_ring"), Is.True,
            "The explicit-index diagnostic must cross the material and vertex-colour guards before testing the original wrapper. " + string.Join(';', control.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        var originalHashes = originals.ToDictionary(pair => pair.Key, pair => Convert.ToHexString(SHA256.HashData(pair.Value)), StringComparer.OrdinalIgnoreCase);
        var original = TotalWarVfxConverter.Convert(new(source, root, Read, "1"));
        Assert.That(original.Emitters.Any(e => e.Name == "3D_ring"), Is.True, "The complete unmodified official wrapper must select its real material.");
        var ring = original.Emitters.Single(e => e.Name == "3D_ring");
        Assert.Multiple(() =>
        {
            Assert.That(ring.Render.MeshMaterialIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(original.MeshMaterials[ring.Render.MeshMaterialIndex].TextureIndexes, Has.Length.EqualTo(6));
            Assert.That(original.Emitters.Count, Is.EqualTo(control.Emitters.Count));
            Assert.That(original.TexturePaths, Is.EqualTo(control.TexturePaths));
            Assert.That(original.TexturePaths.Any(p => p.EndsWith("no_texture.dds", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(original.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            foreach (var pair in originalHashes) Assert.That(Convert.ToHexString(SHA256.HashData(originals[pair.Key])), Is.EqualTo(pair.Value));
        });
    }

    private static (VfxConversion Plan, List<string> Reads) Diagnostic(string entries, int parts = 1, int lods = 1, bool missingTexture = false)
    {
        var source = VfxOpaqueModelMaterialTests.Source(Vector3.One, 1);
        var wrapper = Encoding.UTF8.GetBytes($"<model><geometry>vfx/model-0.rigid_model_v2</geometry><materials>{entries}</materials></model>");
        var model = ModelFactory.Create().Load(VfxModelVariantTests.Model(0));
        var header = model.Header; header.LodCount = (uint)lods; model.Header = header;
        model.ModelList = Enumerable.Range(0, lods).Select(_ => Enumerable.Range(0, parts)
            .Select(part => ModelFactory.Create().Load(VfxModelVariantTests.Model(part)).ModelList[0][0]).ToArray()).ToArray();
        model.LodHeaders = Enumerable.Range(0, lods).Select(lod => LodHeaderFactory.Create().CreateEmpty(header.Version, 0, (uint)lod, 0)).ToArray();
        model.RecalculateOffsets(); var modelBytes = ModelFactory.Create().Save(model);
        var reads = new List<string>();
        byte[]? Resolve(string path)
        {
            var key = path.Replace('\\', '/'); reads.Add(key);
            if (key == "vfx/character.wsmodel") return wrapper;
            if (key == "vfx/model-0.rigid_model_v2") return modelBytes;
            if (missingTexture && key == "vfx/shared.dds") return null;
            return VfxOpaqueModelMaterialTests.Resolve(key, [1]);
        }
        var plan = TotalWarVfxConverter.Convert(new(source, "vfx/diagnostic-wrapper-selection.xml", Resolve));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        return (plan, reads);
    }
}
