using System.IO;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxCoincidentCurveTests
{
    [TestCase(.2, .2)]
    [TestCase(.7, .3)]
    [TestCase(1, .6)]
    [TestCase(1.1, .3)]
    [TestCase(5, .6)]
    public void StaticMeshLoop_KeepsItsAuthoredAppearanceIntervalAfterTheFirstParticleLifetime(double age, double expected)
        => Assert.That(TotalWarVfxSimulation.LoopedAge(age, 1, new(.3f,.7f)), Is.EqualTo(expected).Within(.000001));

    [TestCase(7, .6)]
    [TestCase(7.2, .8)]
    [TestCase(7.4, 1)]
    public void StaticMeshLoop_FinishesAfterTheEmitterStops(double age, double expected)
        => Assert.That(TotalWarVfxSimulation.LoopedAge(age, 1, new(.3f,.7f), 7), Is.EqualTo(expected).Within(.000002));

    [TestCase(0, 0)]
    [TestCase(.25, .15)]
    [TestCase(.5, .3)]
    [TestCase(.75, .65)]
    [TestCase(1, 1)]
    public void IdenticalCoincidentKeys_PreviewWithoutDiscardingSerializedKeys(double time, double expected)
    {
        const string point = "0.5,0.3,linear,linear,-0.05,0,0.05,0,0";
        var text = $"x,0,1;0,0,linear,linear,-0.05,0,0.05,0,0;{point};{point};1,1,linear,linear,-0.05,0,0.05,0,0:x,0;0,0";
        var preview = new VfxPreviewCurve(text, "ParticleInitialSpeed", 0);
        Assert.That(preview.Sample("x", time, .5f), Is.EqualTo(expected).Within(.000001));
        Assert.That(VfxCurve.TryParse(text, out var source), Is.True);
        Assert.That(source!.Channels.Single().Points, Has.Count.EqualTo(4));
        Assert.That(source.ToString(), Is.EqualTo(text));
    }

    [TestCase("0.5,0.4,linear,linear,-0.05,0,0.05,0,0")]
    [TestCase("0.5,0.300000000000001,linear,linear,-0.05,0,0.1,0,0")]
    public void ConflictingCoincidentKeys_StillStopTheAffectedLayer(string conflicting)
    {
        var text = $"x,0,1;0.5,0.3,linear,linear,-0.05,0,0.05,0,0;{conflicting}:x,0;0,0";
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(text, "ParticleScale", 1));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.AmbiguousField"));
    }
}

[NonParallelizable]
public class VfxCommonVisibilityTests
{
    private readonly Dictionary<string, PackFile> _resources = new(StringComparer.OrdinalIgnoreCase);

    [OneTimeSetUp]
    public void LoadOriginalResources()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA is not set.");
        var loader = new PackFileContainerLoader(null!);
        var primary = new[] { "vfx_desc_bl.pack", "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "data.pack", "commontextures.pack" };
        var others = Directory.GetFiles(data!, "*.pack").Select(Path.GetFileName)
            .Where(name => name!.StartsWith("models", StringComparison.Ordinal) || name.StartsWith("terrain", StringComparison.Ordinal) || name.StartsWith("variants", StringComparison.Ordinal)).Order();
        foreach (var name in primary.Concat(others).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var pair in loader.Load(Path.Combine(data!, name!))!.FileList)
                _resources.TryAdd(pair.Key, pair.Value);
    }

    [TestCase("wh2_dlc11_enviro_campaign_spotlight.xml", 1, 1, 0)]
    [TestCase("wh2_dlc11_abilities_sea_shanty_mesh.xml", 1, 1, 0)]
    [TestCase("wh2_dlc10_campaign_kaine_settlement.xml", 5, 6, 1)]
    [TestCase("wh_main_cinematic_smoke_blast.xml", 1, 1, 0)]
    [TestCase("wh3_main_nurgle_bubble_pop.xml", 3, 3, 0)]
    [TestCase("wh2_dlc09_arkhan_chariot.xml", 9, 9, 0)]
    public void RealCohort_KeepsEveryEnabledLayerWithoutChangingGameBytes(string name, int mapped, int total, int disabled)
    {
        var path = @"vfx\" + name;
        var bytes = _resources[path].DataSource.ReadData();
        var result = TotalWarVfxConverter.Convert(new(bytes, path,
            resource => _resources.GetValueOrDefault(resource.Replace('/', '\\'))?.DataSource.ReadData()));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(mapped), JsonSerializer.Serialize(result.Diagnostics));
            Assert.That(result.TotalEmitterCount, Is.EqualTo(total));
            Assert.That(result.DisabledEmitterCount, Is.EqualTo(disabled));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void NativeNeutralSmoke_UsesOneRandomWeightAcrossTheColourCurve()
    {
        const string path=@"vfx\wh_main_cinematic_smoke_blast.xml";
        var bytes=_resources[path].DataSource.ReadData();
        var conversion=TotalWarVfxConverter.Convert(new(bytes,path,resource=>_resources.GetValueOrDefault(resource.Replace('/','\\'))?.DataSource.ReadData()));
        var simulation=new TotalWarVfxSimulation(conversion); simulation.Seek(2,Matrix.Identity);
        var parameters=new GameWorld.Core.Rendering.CommonShaderParameters(Matrix.Identity,Matrix.Identity,Vector3.Backward*10,Vector3.Zero,0,0,0,1,Vector3.One,[]);
        var frame=simulation.Frame(parameters);
        Assert.That(frame.Length,Is.GreaterThan(100));
        Assert.That(frame.All(p=>Math.Abs(p.Colour.X-p.Colour.Y)<.000001f && Math.Abs(p.Colour.Y-p.Colour.Z)<.000001f),Is.True,
            "The original's identical RGB bounds must stay neutral, as in Terry and the shipped vs_colour unary lookup.");
        Assert.That(frame.Select(p=>p.Colour.X).Distinct().Count(),Is.GreaterThan(10),"Keep authored brightness variation between particles.");
    }
}
