using System.Text.Json;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxAnalyticCurveTests
{
    public static IEnumerable<TestCaseData> NativeCases()
    {
        var assembly = typeof(VfxAnalyticCurveTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("NativeAnalyticCurves.json", StringComparison.Ordinal)))!;
        using var data = JsonDocument.Parse(stream);
        foreach (var item in data.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("NativeCurve_" + item.GetProperty("Name").GetString());
    }

    [TestCaseSource(nameof(NativeCases))]
    public void AnalyticCurve_MatchesLaterNativeCohortsAndIndependentControls(JsonElement item)
    {
        var curve = new VfxPreviewCurve(item.GetProperty("Curve").GetString(), "ParticleScale", 1);
        foreach (var sample in item.GetProperty("Samples").EnumerateArray())
        {
            var actual = curve.Sample("x", sample.GetProperty("Phase").GetDouble(), .5f);
            Assert.That(actual, Is.EqualTo(sample.GetProperty("Scale").GetDouble())
                .Within(sample.GetProperty("Tolerance").GetDouble()),
                $"Native repeat cycle {sample.GetProperty("Cycle")}, source frame {sample.GetProperty("Frame")}");
        }
    }

    [TestCase("exp1", "bezier_q")]
    [TestCase("bezier_u", "sine")]
    [TestCase("log3", "future_mode")]
    [TestCase("s_inv", "s_inv")]
    public void UncalibratedEndpointCombinations_StayUnsupported(string outgoing, string incoming)
    {
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(
            $"x,0,0;0,0,linear,{outgoing},0,0,0,0,1;1,1,{incoming},linear,0,0,0,0,1:x,0;0,0", "scale", 1));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.InterpolationUnsupported"));
    }
}
