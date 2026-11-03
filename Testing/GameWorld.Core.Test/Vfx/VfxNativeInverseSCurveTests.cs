#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeInverseSCurveTests
{
    [Test]
    public void NativeModeTen_MatchesIndependentInstructionsAcrossItsHalfwayBranch()
    {
        var fixture=Fixture();var micro=fixture.GetProperty("Micro");
        var text=micro.GetProperty("Source").GetString()!;
        var curve=new VfxPreviewCurve(text,"ParticleScale",1);
        var times=micro.GetProperty("Times").EnumerateArray().ToArray();
        var values=micro.GetProperty("Values").EnumerateArray().ToArray();
        for(var i=0;i<times.Length;++i)
            Assert.That(curve.Sample("x",times[i].GetDouble(),.37f),Is.EqualTo(values[i].GetSingle()).Within(.000001),$"native mode10 phase={times[i]}");
    }

    [TestCase("x")]
    [TestCase("y")]
    [TestCase("z")]
    public void OriginalMixedBezierLogAndInverseS_MatchesNative128TablesWithoutReorderingSourceKeys(string axis)
    {
        var fixture=Fixture();var text=fixture.GetProperty("Field").GetString()!;
        var model=VfxModelNativeCurve.Create(text,"ParticleScale",axis);
        foreach(var upper in new[]{false,true})
        {
            var channel=fixture.GetProperty("Channels").EnumerateArray().Single(c=>c.GetProperty("Name").GetString()==axis+(upper?"_max":"_min"));
            var expected=channel.GetProperty("Baked").EnumerateArray().ToArray();
            for(var i=0;i<128;++i)
                Assert.That(model.SampleBoundAt(i,upper),Is.EqualTo(expected[i].GetSingle()).Within(.00001),$"axis={axis}, upper={upper},native128[{i}]");
        }
        Assert.That(VfxCurve.TryParse(text,out var editable),Is.True);
        Assert.That(editable!.ToString(),Is.EqualTo(text),"The original Z keys are authored t=1 before t=0; snapshots cannot rewrite that order.");
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void OriginalField_ReachesTheActualRigidScaleBindingAtEveryQuality(string quality)
    {
        var fixture=Fixture();var text=fixture.GetProperty("Field").GetString()!;
        var document=VfxDocument.Read(VfxModelVariantTests.Source(1));
        VfxParticleEventMotionTests.Set(document.Root.Descendants("emitter").Single(),"ParticleScale",text);
        var bytes=document.Write();
        var conversion=TotalWarVfxConverter.Convert(new(bytes,"DIAG/original-high-liche-priest-scale.xml",VfxModelVariantTests.Resolve,quality));
        Assert.That(conversion.Emitters,Has.Count.EqualTo(1),string.Join(';',conversion.Diagnostics.Select(d=>d.Field+":"+d.ReasonKey)));
        var model=conversion.Emitters.Single().ModelScaleTable;
        Assert.That(model,Is.Not.Null);
        var expected=fixture.GetProperty("Channels").EnumerateArray().Single(c=>c.GetProperty("Name").GetString()=="z_min").GetProperty("Baked")[85].GetSingle();
        Assert.That(model!.Sample(85/127f,.37f).Z,Is.EqualTo(expected).Within(.00001));
        Assert.That(conversion.Diagnostics.Any(d=>d.Field=="ParticleScale"&&d.ReasonKey=="Vfx.Preview.InterpolationUnsupported"),Is.False);
        Assert.That(VfxDocument.Read(bytes).Write(),Is.EqualTo(bytes));
        TestContext.Progress.WriteLine($"DIAG unchanged official field from {fixture.GetProperty("SourcePath")}, source SHA={fixture.GetProperty("SourceSha256")}; host is diagnostic, full source remains in official replay matrix.");
    }

    private static JsonElement Fixture()
    {
        using var input=new MemoryStream(Convert.FromBase64String(Payload));using var gzip=new GZipStream(input,CompressionMode.Decompress);
        using var output=new MemoryStream();gzip.CopyTo(output);var bytes=output.ToArray();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)),Is.EqualTo("8a4eadb8508e6ec2cef0c9c8e6ac4b6a17f77267048abd9c721068cd6b3540d0"));
        using var document=JsonDocument.Parse(bytes);return document.RootElement.Clone();
    }
    private const string Payload="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";
}
