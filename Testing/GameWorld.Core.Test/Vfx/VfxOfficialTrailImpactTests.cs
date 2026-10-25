#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxOfficialTrailImpactTests
{
    private readonly VfxOfficialDistortionPixelTests _official = new();

    [OneTimeSetUp]
    public void RequireCurrentOfficialResources() => _official.LoadCurrentOfficialResourcesOnly();

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void CompleteOfficialImpact_KeepsTheAuthoredExpandingGroundWave(string quality)
        => AssertLiveLayer(@"vfx\wh3_main_entity_storm_dragon_attack_impact.xml", quality, "impact_ground_wave", .15f);

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void CompleteOfficialArc90_KeepsTheAuthoredSmokeRing(string quality)
        => AssertLiveLayer(@"vfx\wh3_dlc28_neferata_slash_arc90.xml", quality, "smoke_ring", .3f);

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void CompleteOfficialFlowerTrail_PreservesBothModelDistanceEmitters(string quality)
    {
        const string path = @"vfx\wh3_dlc24_aekold_flower_trail.xml";
        var bytes = Read(path) ?? throw new FileNotFoundException(path);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, path, Read, quality));
        foreach (var (name, spacing) in new[] { ("Flowers_trail", 1f), ("Flowers_trail2", .5f) })
        {
            var emitter = conversion.Emitters.Single(item => item.Name == name);
            Assert.Multiple(() =>
            {
                Assert.That(emitter.Kind, Is.EqualTo(1));
                Assert.That(emitter.SpawnSpacing, Is.EqualTo(spacing));
                Assert.That(emitter.IsMotionTrail, Is.True);
            });
        }
        Report(path, quality, bytes, conversion);
    }

    private void AssertLiveLayer(string path, string quality, string name, float time)
    {
        var bytes = Read(path) ?? throw new FileNotFoundException(path);
        var sourceHash = SHA256.HashData(bytes);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, path, Read, quality));
        var index = conversion.Emitters.FindIndex(emitter => emitter.Name == name);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), JsonSerializer.Serialize(conversion.Diagnostics));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(time, Matrix.Identity);
        var eye = new Vector3(10, 15, 25);
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 200), eye,
            Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);
        var layer = simulation.Frame(camera).Where(particle => particle.Layer == index).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(layer, Is.Not.Empty, "Conversion alone must not hide a subsequent projection failure.");
            Assert.That(layer.All(particle => float.IsFinite(particle.Transform.Up.LengthSquared())
                && particle.Transform.Up.LengthSquared() > 0 && particle.Transform.Right.LengthSquared() > 0), Is.True);
            Assert.That(SHA256.HashData(bytes), Is.EqualTo(sourceHash));
            Assert.That(conversion.Diagnostics.Where(item => item.Layer == name
                && item.ReasonKey is "Vfx.Preview.AmbiguousField" or "Vfx.Preview.InvalidData"), Is.Empty);
        });
        Report(path, quality, bytes, conversion);
    }

    private static void Report(string path, string quality, byte[] bytes, VfxConversion conversion)
        => TestContext.Progress.WriteLine(JsonSerializer.Serialize(new
        {
            Path = path, Quality = quality, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Emitters = conversion.Emitters.Select(emitter => emitter.Name),
            Scope = "Current complete official source CPU conversion and simulation; no native visual-equivalence claim."
        }));

    private byte[]? Read(string path) => (byte[]?)typeof(VfxOfficialDistortionPixelTests)
        .GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_official, [path]);
}
