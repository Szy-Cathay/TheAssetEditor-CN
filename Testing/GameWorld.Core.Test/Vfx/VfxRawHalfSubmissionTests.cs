#nullable enable
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace GameWorld.Core.Test.Vfx;

public class VfxRawHalfSubmissionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void RawHalfSubmissionPreservesOriginalRgbAlphaAndOneParticle()
    {
        var conversion = Plan();
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.CoveredColourMultiplier))!.SetValue(conversion.Emitters[0], .25f);
        using var preview = Preview(conversion);
        var mappingType = typeof(EffekseerVfxPreview).GetNestedType("RenderLayers", BindingFlags.NonPublic)!;
        var mapping = Activator.CreateInstance(mappingType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [new[] { 0 }, new[] { -1 }], null)!;
        mappingType.GetProperty("RawHalf")?.SetValue(mapping, new[] { true });
        var variants = Array.CreateInstance(mappingType, 1); variants.SetValue(mapping, 0);
        var mappings = Array.CreateInstance(variants.GetType(), 1); mappings.SetValue(variants, 0);
        typeof(EffekseerVfxPreview).GetField("_renderLayers", Private)!.SetValue(preview, mappings);
        var particle = new EffekseerParticle { Transform = Matrix.Identity, Colour = new(2, 3, 4, .375f), UV = new(.1f, .2f, .3f, .4f), Time = .5f };
        var frame = new[] { new VfxPreviewParticle(particle, 0) };
        var before = MemoryMarshal.AsBytes(frame.AsSpan()).ToArray();
        Assert.That(Prepare(preview, frame), Is.EqualTo(1));
        var actual = Packets(preview)[0];
        Assert.That(actual.Colour, Is.EqualTo(particle.Colour), "A raw half layer receives the authored tint once; native keeps legacy coverage/emission multipliers separately.");
        Assert.That(MemoryMarshal.AsBytes(frame.AsSpan()).ToArray(), Is.EqualTo(before));
    }

    [Test]
    public void HalfSourceMarkerAndNewConfigDoNotChangeTheExistingAbis()
    {
        var conversion = Plan();
        var property = typeof(VfxPreviewEmitter).GetProperty("HalfResolution");
        Assert.That(property, Is.Not.Null, "Keep the explicit source pass separately from minimum-pixel size behavior.");
        Assert.That(property!.GetValue(conversion.Emitters[0]), Is.EqualTo(true));
        var type = typeof(EffekseerLayer).Assembly.GetType("GameWorld.Core.Vfx.EffekseerHalfLayer");
        Assert.That(type, Is.Not.Null);
        Assert.That(Marshal.SizeOf(type!), Is.EqualTo(24));
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<EffekseerLayer>(), Is.EqualTo(132));
            Assert.That(Marshal.SizeOf<EffekseerParticle>(), Is.EqualTo(148));
            Assert.That(Marshal.SizeOf<EffekseerLighting>(), Is.EqualTo(176));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.HalfResolutionUnsupported"), Is.True);
        });
    }

    [TestCase(null, 2, 0, 1, 1, 2)]
    [TestCase((byte)255, 1, 0, -1, 5, -1)]
    [TestCase((byte)0, 1, -1, 0, -1, 6)]
    public void LegacyCoverageAndEmissionRemainExactBeforeTheRawLayer(byte? constantAlpha, int rawIndex, int coverage, int emission, int coverageMode, int emissionMode)
    {
        using var preview = Preview(Plan());
        var built = Build(preview, [constantAlpha]);
        Assert.That(built.Half, Has.Length.EqualTo(1));
        Assert.That(built.Layers, Has.Length.EqualTo(rawIndex + 1));
        var config = built.Half.GetValue(0)!;
        Assert.Multiple(() =>
        {
            Assert.That(Field<int>(config, "LayerIndex"), Is.EqualTo(rawIndex));
            Assert.That(Field<int>(config, "LegacyCoverageIndex"), Is.EqualTo(coverage));
            Assert.That(Field<int>(config, "LegacyEmissionIndex"), Is.EqualTo(emission));
            Assert.That(Field<int>(config, "Premultiplied"), Is.EqualTo(1));
            Assert.That(Field<float>(config, "CoveredColourMultiplier"), Is.EqualTo(1));
            Assert.That(Field<float>(config, "EmissionMultiplier"), Is.EqualTo(2));
            Assert.That(built.Layers[rawIndex].MaterialMode, Is.EqualTo(1));
        });
        if (coverage >= 0) Assert.That(built.Layers[coverage].MaterialMode, Is.EqualTo(coverageMode));
        if (emission >= 0) Assert.That(built.Layers[emission].MaterialMode, Is.EqualTo(emissionMode));
        Assert.That(Prepare(preview, [new(new() { Transform = Matrix.Identity, Colour = Vector4.One }, 0)]), Is.EqualTo(1));
        Assert.That(Packets(preview)[0].Layer, Is.EqualTo(rawIndex));
    }

    [TestCase("unmarked")]
    [TestCase("distortion")]
    [TestCase("mesh")]
    [TestCase("gradient")]
    [TestCase("model")]
    [TestCase("opaque")]
    [TestCase("untextured")]
    public void UnsupportedCombinationsRetainTheirLegacyPackets(string control)
    {
        var plan = Plan(control == "unmarked" ? null : "HALFRES_DEFERRED_ALPHA");
        var layer = plan.Emitters[0].Render;
        if (control == "distortion") layer.Distortion = 1;
        if (control == "mesh") layer.MeshMaterialIndex = 0;
        if (control == "gradient") layer.GradientIndex = 0;
        if (control == "model") layer.Kind = 1;
        if (control == "opaque") layer.OpaqueModel = 1;
        if (control == "untextured") layer.TextureIndex = -1;
        plan.Emitters[0].Render = layer;
        using var preview = Preview(plan);
        var built = Build(preview, [null]);
        Assert.That(built.Half, Is.Empty);
        Assert.That(built.Layers, Has.Length.EqualTo(control == "opaque" ? 1 : 2));
    }

    [TestCase(null)]
    [TestCase((byte)255)]
    [TestCase((byte)0)]
    public void RawFallbackMetadataReconstructsTheOriginalLegacyPacketsExactly(byte? constantAlpha)
    {
        var half = Plan(); var full = Plan("FULLRES_DEFERRED_ALPHA");
        // DIAG disables only the new routing tag; an omitted pass has a different legacy emission-shape default.
        typeof(VfxPreviewEmitter).GetProperty("FullResolution")?.SetValue(full.Emitters[0], false);
        foreach (var plan in new[] { half, full })
            typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.CoveredColourMultiplier))!.SetValue(plan.Emitters[0], .25f);
        using var halfPreview = Preview(half); using var legacyPreview = Preview(full);
        var halfBuilt = Build(halfPreview, [constantAlpha]); var legacyBuilt = Build(legacyPreview, [constantAlpha]);
        var config = halfBuilt.Half.GetValue(0)!;
        var rawIndex = Field<int>(config, "LayerIndex");
        Assert.That(MemoryMarshal.AsBytes(halfBuilt.Layers.AsSpan(0, rawIndex)).ToArray(),
            Is.EqualTo(MemoryMarshal.AsBytes(legacyBuilt.Layers.AsSpan()).ToArray()), "Raw configuration must retain the exact original C/E layers.");
        var frame = new[] { new VfxPreviewParticle(new()
        {
            Transform = Matrix.Identity, Colour = new(2, 3, 4, .375f), UV = new(.1f, .2f, .3f, .4f),
            NormalRotation = new(.1f, .2f, .3f, .4f), ParticleSize = 1.5f, Time = .5f,
        }, 0) };
        var legacyCount = Prepare(legacyPreview, frame);
        Assert.That(Prepare(halfPreview, frame), Is.EqualTo(1));
        var raw = Packets(halfPreview)[0]; var restored = new List<EffekseerParticle>();
        foreach (var item in new[] { ("LegacyCoverageIndex", "CoveredColourMultiplier"), ("LegacyEmissionIndex", "EmissionMultiplier") })
        {
            var index = Field<int>(config, item.Item1);
            if (index < 0) continue;
            var packet = raw; packet.Layer = index;
            var multiplier = Field<float>(config, item.Item2);
            packet.Colour.X *= multiplier; packet.Colour.Y *= multiplier; packet.Colour.Z *= multiplier;
            restored.Add(packet);
        }
        Assert.That(restored, Has.Count.EqualTo(legacyCount));
        Assert.That(MemoryMarshal.AsBytes(restored.ToArray().AsSpan()).ToArray(),
            Is.EqualTo(MemoryMarshal.AsBytes(Packets(legacyPreview).AsSpan(0, legacyCount)).ToArray()),
            "The fallback protocol must reconstruct legacy tint, alpha, indices and ordering without any simulated birth change.");
    }

    [TestCase(255, 2)]
    [TestCase(256, 0)]
    public void ExtraRawLayersNeverConsumeTheReservedLegacyLayerBudget(int definitions, int raw)
    {
        var plan = Plan(); var definition = plan.Emitters.Single();
        for (var index = 1; index < definitions; index++) plan.Emitters.Add(definition);
        using var preview = Preview(plan);
        var built = Build(preview, [null]);
        Assert.That(built.Layers, Has.Length.EqualTo(512));
        Assert.That(built.Half, Has.Length.EqualTo(raw));
    }

    [TestCase(@"vfx\wh2_dlc17_weapon_trail_blowpipe_hero.xml", "0", 0, 2)]
    [TestCase(@"vfx\wh2_dlc17_weapon_trail_blowpipe_hero.xml", "1", 2, 0)]
    [TestCase(@"vfx\wh2_dlc17_weapon_trail_blowpipe_hero.xml", "2", 2, 0)]
    [TestCase(@"vfx\wh_dlc03_enviro_large_siege_smoke.xml", "0", 0, 0)]
    [TestCase(@"vfx\wh_dlc03_enviro_large_siege_smoke.xml", "1", 0, 0)]
    public void CompleteOfficialSourcePassesSelectRawOrLegacyWithoutChangingDefinitions(string sourcePath, string quality, int expectedHalf, int expectedFull)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("The original game manifest is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(row => row.Length > 1)
            .ToDictionary(row => row[0], row => long.Parse(row[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack" })
        {
            var path = Path.Combine(data!, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var pair in new PackFileContainerLoader(null!).Load(path)!.FileList) files[pair.Key.Replace('/', '\\')] = pair.Value;
        }
        var source = files[sourcePath].DataSource.ReadData();
        var hash = Convert.ToHexStringLower(SHA256.HashData(source));
        var plan = TotalWarVfxConverter.Convert(new(source, sourcePath, path => files.TryGetValue(path.Replace('/', '\\'), out var file) ? file.DataSource.ReadData() : null, quality));
        Assert.That(plan.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
        using var preview = Preview(plan);
        var built = Build(preview, new byte?[plan.Textures.Count]);
        TestContext.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(plan.Emitters.Select(e => new
        {
            e.Name, e.Kind, e.PremultipliedAlpha, e.MinimumPixelHalfResolution, e.Render.Blend, e.Render.MaterialMode,
            e.Render.ModelIndex, e.Render.MeshMaterialIndex, e.Render.TextureIndex,
            SourceHalf = typeof(VfxPreviewEmitter).GetProperty("HalfResolution")?.GetValue(e),
            SourceFull = typeof(VfxPreviewEmitter).GetProperty("FullResolution")?.GetValue(e),
        })));
        Assert.That(built.Half.Length, Is.EqualTo(expectedHalf));
        Assert.That(built.Full.Length, Is.EqualTo(expectedFull), "Explicit FULL sources keep full resolution and their own native configuration.");
        Assert.That(plan.Emitters.All(e => (bool)typeof(VfxPreviewEmitter).GetProperty("HalfResolution")!.GetValue(e)! == (expectedHalf > 0)), Is.True);
        Assert.That(plan.Emitters.All(e => (bool)typeof(VfxPreviewEmitter).GetProperty("FullResolution")!.GetValue(e)! == (expectedFull > 0)), Is.True);
        Assert.That(files[sourcePath].DataSource.ReadData(), Is.EqualTo(source));
        TestContext.Progress.WriteLine($"Official {sourcePath} q{quality} rootSHA={hash} definitions={plan.Emitters.Count} half={built.Half.Length} full={built.Full.Length}");
    }

    [Test]
    public void FullResolutionUsesItsOwnConfigAndTheSameUnmodifiedLegacyAndRawPackets()
    {
        var full = Plan("FULLRES_DEFERRED_ALPHA"); var half = Plan();
        using var fullPreview = Preview(full); using var halfPreview = Preview(half);
        var fullBuilt = Build(fullPreview, [null]); var halfBuilt = Build(halfPreview, [null]);
        Assert.That(fullBuilt.Full, Has.Length.EqualTo(1));
        Assert.That(fullBuilt.Half, Is.Empty);
        Assert.That(halfBuilt.Full, Is.Empty);
        Assert.That(halfBuilt.Half, Has.Length.EqualTo(1));
        Assert.That(MemoryMarshal.AsBytes(fullBuilt.Layers.AsSpan()).ToArray(), Is.EqualTo(MemoryMarshal.AsBytes(halfBuilt.Layers.AsSpan()).ToArray()));
        Assert.That(typeof(VfxPreviewEmitter).GetProperty("FullResolution")!.GetValue(full.Emitters[0]), Is.EqualTo(true));
        Assert.That(typeof(VfxPreviewEmitter).GetProperty("HalfResolution")!.GetValue(full.Emitters[0]), Is.EqualTo(false));
        var frame = new[] { new VfxPreviewParticle(new() { Transform = Matrix.Identity, Colour = new(2, 3, 4, .375f) }, 0) };
        Assert.That(Prepare(fullPreview, frame), Is.EqualTo(1));
        Assert.That(Prepare(halfPreview, frame), Is.EqualTo(1));
        Assert.That(Packets(fullPreview)[0].Colour, Is.EqualTo(Packets(halfPreview)[0].Colour));
    }

    private static VfxConversion Plan(string? pass = "HALFRES_DEFERRED_ALPHA")
        => TotalWarVfxConverter.Convert(new(VfxSimpleEmissiveTests.Source(.7f, true, new(.3f), 2, pass, premultiplied: true),
            "DIAG/raw-half.xml", _ => VfxMaterialCompositingTests.Texture(128, 128)));

    private static EffekseerVfxPreview Preview(VfxConversion conversion)
    {
        var preview = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_conversion", Private)!.SetValue(preview, conversion);
        return preview;
    }

    private static (EffekseerLayer[] Layers, Array Half, Array Full) Build(EffekseerVfxPreview preview, byte?[] alpha)
    {
        var method = typeof(EffekseerVfxPreview).GetMethod("BuildRenderLayers", Private);
        Assert.That(method, Is.Not.Null, "Invoke the actual Reload layer builder without constructing a renderer.");
        var result = method!.Invoke(preview, [alpha])!;
        return ((EffekseerLayer[])result.GetType().GetField("Item1")!.GetValue(result)!, (Array)result.GetType().GetField("Item2")!.GetValue(result)!,
            (Array?)result.GetType().GetField("Item3")?.GetValue(result) ?? Array.Empty<EffekseerHalfLayer>());
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name)!.GetValue(value)!;
    private static int Prepare(EffekseerVfxPreview preview, VfxPreviewParticle[] frame)
        => (int)typeof(EffekseerVfxPreview).GetMethod("PrepareSubmission", Private)!.Invoke(preview, [frame])!;
    private static EffekseerParticle[] Packets(EffekseerVfxPreview preview)
        => (EffekseerParticle[])typeof(EffekseerVfxPreview).GetField("_submissionParticles", Private)!.GetValue(preview)!;
}
