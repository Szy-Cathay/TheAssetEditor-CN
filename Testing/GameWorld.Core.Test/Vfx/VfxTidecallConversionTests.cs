#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxTidecallConversionTests
{
    [Test]
    public void CompleteOfficialTidecallReference_RetainsBothWaveModelsWithAuthoredDisplacement()
    {
        var original = new VfxOfficialDistortionPixelTests();
        original.LoadCurrentOfficialResourcesOnly();
        var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
        byte[]? Resolve(string path) => (byte[]?)read.Invoke(original, [path]);
        const string path = @"vfx\wh2_dlc11_deep_tidecall_breath_area.xml";
        var bytes = Resolve(path) ?? throw new FileNotFoundException(path);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, path, Resolve));
        foreach (var name in new[] { "MAIN_waves_small", "END_wave_large" })
        {
            var emitter = conversion.Emitters.Single(item => item.Name == name);
            Assert.That(emitter.Render.MeshMaterialIndex, Is.GreaterThanOrEqualTo(0));
            var material = conversion.MeshMaterials[emitter.Render.MeshMaterialIndex];
            Assert.Multiple(() =>
            {
                Assert.That(material.Parameters, Has.Length.EqualTo(104));
                Assert.That(material.Parameters[19], Is.EqualTo(2.2f));
                Assert.That(material.TextureIndexes, Has.Length.EqualTo(12));
                Assert.That(emitter.Kind, Is.EqualTo(1));
                Assert.That(conversion.Models[emitter.Render.ModelIndex].Vertices.Length, Is.GreaterThan(0));
            });
        }
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Source = path, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Scope = "Complete current official reference and dependencies, including the small and large tidecall waves; CPU contract only." }));
    }

    [Test]
    public void CompleteOfficialStormImpact_RetainsBothAuthoredWaterModelsAndTheirMaterialBindings()
    {
        var original = new VfxOfficialDistortionPixelTests();
        original.LoadCurrentOfficialResourcesOnly();
        var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
        byte[]? Resolve(string path) => (byte[]?)read.Invoke(original, [path]);
        const string path = @"vfx\wh3_main_entity_storm_dragon_attack_impact.xml";
        var bytes = Resolve(path) ?? throw new FileNotFoundException(path);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, path, Resolve));
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Names = conversion.Emitters.Select(emitter => emitter.Name) }));
        foreach (var (name, displacement) in new[] { ("impact_splash_radial_mesh", 0f), ("impact_splash_centre", .2f) })
        {
            var matches = conversion.Emitters.Where(emitter => emitter.Name.Contains(name, StringComparison.Ordinal)).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1),
                "The complete original source must retain the authored water layer; diagnostics: " + JsonSerializer.Serialize(conversion.Diagnostics));
            var emitter = matches.Single();
            Assert.That(emitter.Kind, Is.EqualTo(1));
            Assert.That(emitter.Render.MeshMaterialIndex, Is.GreaterThanOrEqualTo(0));
            var material = conversion.MeshMaterials[emitter.Render.MeshMaterialIndex];
            Assert.Multiple(() =>
            {
                Assert.That(material.Parameters, Has.Length.EqualTo(VfxTidecallMaterial.ParameterCount));
                Assert.That(material.TextureIndexes, Has.Length.EqualTo(12), "Keep each original logical binding before backend aliases.");
                Assert.That(material.Parameters[19], Is.EqualTo(displacement), "Retain each original splash's vertex displacement.");
                Assert.That(conversion.Models[emitter.Render.ModelIndex].Vertices.Length, Is.GreaterThan(0));
            });
        }
        Assert.That(conversion.Diagnostics.Any(item => item.Field.EndsWith("wh2_vfx_cst_tidecall.xml.shader", StringComparison.OrdinalIgnoreCase)
            && item.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.False);
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Source = path, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Emitters = conversion.Emitters.Select(emitter => new { emitter.Name, emitter.Kind, emitter.Render.MeshMaterialIndex }),
            Scope = "CPU conversion of complete current official source and original dependencies; no visual-equivalence claim." }));
    }
}
