#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxWindIndependentForceTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 20), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(40, 40, .1f, 100), new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
    private Dictionary<string, (PackFile File, string Pack)>? _official;

    [TestCase(.3f, 0)]
    [TestCase(0, .1f)]
    [TestCase(.3f, .1f)]
    public void UnimplementedWind_DoesNotEraseTheIndependentForceCurve(float wind, float variance)
    {
        var original = Diagnostic(wind, variance);
        var control = Diagnostic(0, 0);
        var actualPlan = TotalWarVfxConverter.Convert(original);
        var expectedPlan = TotalWarVfxConverter.Convert(control);
        Assert.Multiple(() =>
        {
            Assert.That(expectedPlan.Emitters.Single().ParticleForceCurve, Is.True);
            Assert.That(actualPlan.Emitters.Single().ParticleForceCurve, Is.True,
                "Native adds the known ForceA contribution independently of the separate wind term.");
            Assert.That(actualPlan.Diagnostics.Any(d => d.Field is "EffectedByWindFactor" or "WindVarianceFactor"
                && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.True, "Keeping ForceA does not implement wind.");
            Assert.That(actualPlan.Diagnostics.Any(d => d.Field == "ForceA" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
        var expected = new TotalWarVfxSimulation(expectedPlan);
        var actual = new TotalWarVfxSimulation(actualPlan);
        foreach (var time in new[] { .5f, 1.5f, 3f })
        {
            expected.Seek(time, Matrix.Identity); actual.Seek(time, Matrix.Identity);
            Assert.That(actual.Frame(Camera).Single().Transform.Translation,
                Is.EqualTo(expected.Frame(Camera).Single().Transform.Translation),
                "With wind deliberately unimplemented, its source fields must not remove force or alter random draws.");
        }
        Assert.That(VfxDocument.Read(original.GameBytes).Write(), Is.EqualTo(original.GameBytes));
    }

    [TestCase("ForceB")]
    [TestCase("ForceC")]
    [TestCase("EmitterOrientation")]
    public void IndependentForce_DoesNotExpandUncalibratedCombinationGuards(string field)
    {
        var snapshot = Diagnostic(.3f, .1f);
        var document = VfxDocument.Read(snapshot.GameBytes);
        document.Root.Element("vfx")!.Element("emitters")!.Element("emitter")!.Add(
            new XElement("property", new XAttribute("name", field), VectorCurve(1, 1)));
        var plan = TotalWarVfxConverter.Convert(snapshot with { GameBytes = document.Write() });
        Assert.That(plan.Emitters, Has.Count.EqualTo(1));
        Assert.That(plan.Emitters.Single().ParticleForceCurve, Is.False,
            "Wind independence must not enable additional forces or rotated emitter bases.");
    }

    [TestCase(@"vfx\wh_main_terrain_monster_dust.xml", "dust_giant_ring")]
    [TestCase(@"vfx\wh_dlc03_large_smoke_loop.xml", "smoke_heavy")]
    [TestCase(@"vfx\wh_dlc20_large_smoke_loop.xml", "smoke_heavy")]
    [TestCase(@"vfx\wh_main_cannonball_impact_large_mud.xml", "dust_ring")]
    [TestCase(@"vfx\wh_dlc04_balefire_trail.xml", "balefire_weapon_embers")]
    [TestCase(@"vfx\wh_main_big_waaagh_foot_of_gork_impact.xml", "remain_smoke")]
    public void CompleteOfficialRoots_KeepForceWhenTheirSeparateWindFieldsArePresent(string root, string layer)
    {
        LoadOfficialIndex();
        var readBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? Read(string path)
        {
            var key = path.Replace('/', '\\');
            if (!_official!.TryGetValue(key, out var resource)) return null;
            if (!readBytes.TryGetValue(key, out var bytes))
            {
                readBytes[key] = bytes = resource.File.DataSource.ReadData();
                sourceHashes[key] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            return bytes;
        }
        var source = Read(root) ?? throw new FileNotFoundException(root);
        var original = TotalWarVfxConverter.Convert(new(source, root, Read, "1"));
        // DIAG: only wind fields are zeroed in private snapshots; this is not the official final sample.
        byte[]? Windless(string path) => Read(path) is { } bytes ? ZeroWind(bytes, path) : null;
        var diagnostic = TotalWarVfxConverter.Convert(new(ZeroWind(source, root), root, Windless, "1"));
        var expected = diagnostic.Emitters.Where(e => e.Name == layer && e.Kind == 0).ToArray();
        var actual = original.Emitters.Where(e => e.Name == layer && e.Kind == 0).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.Not.Empty, "The named complete-root component must exist.");
            Assert.That(expected.All(e => e.ParticleForceCurve), Is.True,
                "The unchanged force/basis combination is already supported when wind metadata is absent.");
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            Assert.That(actual.All(e => e.ParticleForceCurve), Is.True, "Official wind metadata must not zero the independent ForceA contribution.");
            Assert.That(original.Emitters, Has.Count.EqualTo(diagnostic.Emitters.Count));
            Assert.That(original.Diagnostics.Any(d => d.Field is "EffectedByWindFactor" or "WindVarianceFactor"
                && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
            Assert.That(original.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
        });
        foreach (var (path, bytes) in readBytes)
            Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(sourceHashes[path]),
                "The windless diagnostic must not mutate the original resource arrays.");
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        TestContext.Progress.WriteLine($"Official complete-root force boundary: {root}, layer={layer}, manifest Pack={_official![root].Pack}");
    }

    private void LoadOfficialIndex()
    {
        if (_official != null) return;
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Official resources require explicit VFX opt-in.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !Directory.Exists(data)) Assert.Ignore("Original game data directory is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(l => l.Split('\t')).Where(p => p.Length > 1)
            .ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var names = new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" };
        var loader = new PackFileContainerLoader(null!);
        var packs = names.Select(name =>
        {
            var path = Path.Combine(data!, name);
            Assert.That(manifest.TryGetValue(name, out var size), Is.True);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(size), "The sample must come from a manifest Pack.");
            return loader.Load(path)!;
        }).OrderBy(p => p.Header.LoadOrder).ThenBy(p => p.Name).ToArray();
        _official = new(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        foreach (var entry in pack.FileList) _official[entry.Key.Replace('/', '\\')] = (entry.Value, pack.SystemFilePath);
    }

    private static byte[] ZeroWind(byte[] bytes, string path)
    {
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return bytes;
        var document = VfxDocument.Read(bytes);
        foreach (var node in document.Root.Descendants().Where(n => (string?)n.Attribute("name") is "EffectedByWindFactor" or "WindVarianceFactor"))
            node.SetAttributeValue("value", "0");
        return document.Write();
    }

    private static VfxPreviewSnapshot Diagnostic(float wind, float variance)
    {
        var scalar = ScalarCurve(.2, .2);
        var source = Encoding.UTF8.GetBytes(FormattableString.Invariant($"""
            <vfx revision="2"><emitters><emitter id="diagnostic_force" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{scalar}</property>
            <property name="ParticleLifeTime">{ScalarCurve(4,4)}</property><property name="ParticleInitialSpeed">{ScalarCurve(8,8)}</property>
            <property name="SpawnAngle" x="0" y="0"/><ranged_property name="SpawnSpread" min="90" max="90"/>
            <property name="ParticleRelativeSpawnOrientation" value="RELATIVE_TO_VFX_UP_AXIS"/>
            <property name="ForceA">{VectorCurve(-981,0)}</property><property name="ParticleDeceleration">{ScalarCurve(.3,2)}</property>
            <property name="EffectedByWindFactor" value="{wind}"/><property name="WindVarianceFactor" value="{variance}"/>
            </emitter></emitters><behaviour_chains><behaviour_chain id="diagnostic_force"><quality value="0"><modifier fragment="ps_texture">
            <texture_property name="diffuse" path="vfx/diagnostic.tga"/></modifier></quality></behaviour_chain></behaviour_chains></vfx>
            """));
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(source, "vfx/diagnostic-wind-force.xml", _ => texture);
    }

    private static string Channel(string name, double a, double b) => FormattableString.Invariant($"{name},0,1;0,{a},linear,linear,0,0,0,0,0;1,{b},linear,linear,0,0,0,0,0");
    private static string ScalarCurve(double a, double b) => Channel("x_min", a, b) + "|" + Channel("x_max", a, b) + ":x,0;0,1";
    private static string VectorCurve(double a, double b) => string.Join("|", Channel("x_min", 0, 0), Channel("y_min", a, b), Channel("z_min", 0, 0)) + ":x,0;0,0|y,0;1,1|z,0;2,2";
}
