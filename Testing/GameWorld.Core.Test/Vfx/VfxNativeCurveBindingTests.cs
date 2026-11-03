#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeCurveBindingTests
{
    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void RigidConversion_BindsTheOriginalQuadraticAlphaToTheFloatTableAtEveryQuality(string quality)
    {
        var bytes = ModelAlphaSource(OriginalAlpha());
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "DIAG/native-curve-alpha.xml", VfxModelVariantTests.Resolve, quality));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
        var alpha = conversion.Emitters.Single().Alpha;
        Assert.That(alpha.Sample("alpha", 63 / 127f, .3f), Is.EqualTo(AlphaTable()[63]).Within(.00001));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "curve_alpha" && d.ReasonKey == "Vfx.Preview.InterpolationUnsupported"), Is.False);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void EmitterPositionEmptyXY_ConvertsAndKeepsTheAuthoredZMotion()
    {
        var item = VfxNativeCurveBoundaryTests.Fixture().GetProperty("Cases").EnumerateArray()
            .Single(item => item.GetProperty("Name").GetString()!.Contains("invocation_of_sotek_area", StringComparison.Ordinal));
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        VfxParticleEventMotionTests.Set(document.Root.Descendants("emitter").Single(), "EmitterPosition", item.GetProperty("Source").GetString()!);
        var bytes = document.Write();
        var conversion = VfxModelVariantTests.Convert(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
        var position = conversion.Emitters.Single().Position;
        Assert.That(position.Vector(.5, new(.37f)), Is.EqualTo(new Vector3(0, 0, position.Sample("z", .5, .37f))));
        var expected = item.GetProperty("Channels")[4].GetProperty("Continuous").EnumerateArray()
            .Single(sample => sample.GetProperty("Phase").GetDouble() == .5).GetProperty("Value").GetSingle();
        Assert.That(position.Sample("z", .5, 0), Is.EqualTo(expected).Within(.00001));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void RepeatedScaleAndSplitZeroDistinctBounds_ReachTheActualRigidScaleTable()
    {
        foreach (var part in new[] { "meteor_arc", "stealth_entity_buff" })
        {
            var item = VfxNativeCurveBoundaryTests.Fixture().GetProperty("Cases").EnumerateArray()
                .Single(item => item.GetProperty("Name").GetString()!.Contains(part, StringComparison.Ordinal));
            var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
            VfxParticleEventMotionTests.Set(document.Root.Descendants("emitter").Single(), "ParticleScale", item.GetProperty("Source").GetString()!);
            var bytes = document.Write();
            var conversion = VfxModelVariantTests.Convert(bytes);
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
            var emitter = conversion.Emitters.Single();
            Assert.That(emitter.ModelScaleTable, Is.Not.Null);
            foreach (var bound in new[] { 0, 1 })
            {
                var expected = item.GetProperty("Channels")[bound].GetProperty("Baked")[110].GetSingle();
                Assert.That(emitter.ModelScaleTable!.Sample(110 / 127f, bound).X, Is.EqualTo(expected).Within(.00001));
            }
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        }
    }

    internal static string OriginalAlpha() => VfxNativeCurveBoundaryTests.Fixture().GetProperty("Cases").EnumerateArray()
        .Single(item => item.GetProperty("Field").GetString() == "curve_alpha").GetProperty("Source").GetString()!;

    internal static float[] AlphaTable() => VfxNativeCurveBoundaryTests.Fixture().GetProperty("Cases").EnumerateArray()
        .Single(item => item.GetProperty("Field").GetString() == "curve_alpha").GetProperty("Channels")[0]
        .GetProperty("Baked").EnumerateArray().Select(value => value.GetSingle()).ToArray();

    internal static byte[] ModelAlphaSource(string alpha)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var quality = document.Root.Descendants("quality").Single();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"),
            new XElement("property", new XAttribute("name", "curve_alpha"), new XAttribute("type", "curve"), alpha)));
        return document.Write();
    }
}
