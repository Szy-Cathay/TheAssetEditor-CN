#nullable enable
using System.Collections;
using System.Reflection;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxExactChannelBindingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase("linear", "linear")]
    [TestCase("exp1", "log3")]
    [TestCase("bezier_c", "bezier_c")]
    public void ExactMultiKeyChannelsShareOneBindingAndPreserveEverySampleBit(string outgoing, string incoming)
    {
        var points = $"0,-2,linear,{outgoing},-.2,0,.2,3,1;1,5,{incoming},linear,-.2,-3,.2,0,1";
        var text = Pair(points, points);
        var curve = new VfxPreviewCurve(text, "diagnostic", 1);
        var reference = new VfxPreviewCurve($"x,0,0;{points}:x,0;0,0", "diagnostic", 1);
        var binding = Binding(curve);
        Assert.That(binding.Min, Is.SameAs(binding.Max), "Identical immutable data must use the existing shared-channel evaluation path.");
        var channels = ((IEnumerable)typeof(VfxPreviewCurve).GetField("_channels", Private)!.GetValue(curve)!).Cast<object>().ToArray();
        Assert.That(channels.Select(channel => channel.GetType().GetProperty("Name")!.GetValue(channel)), Is.EqualTo(new[] { "x_min", "x_max" }));
        Assert.That(channels[0], Is.Not.SameAs(channels[1]), "Original named snapshots must remain separate for inspection.");
        foreach (var time in new[] { double.NegativeInfinity, -1, -0d, 0, .125, .5, .875, 1, 2, double.PositiveInfinity, double.NaN })
        foreach (var random in new[] { -0f, 0, .125f, .5f, 1, float.NegativeInfinity, float.PositiveInfinity, float.NaN })
        {
            var expected = reference.Range("x", time); var actual = curve.Range("x", time);
            Assert.That(Bits(actual.Min), Is.EqualTo(Bits(expected.Min)));
            Assert.That(Bits(actual.Max), Is.EqualTo(Bits(expected.Max)));
            Assert.That(Bits(curve.Sample("x", time, random)), Is.EqualTo(Bits(reference.Sample("x", time, random))));
        }
    }

    [TestCase("value-sign")]
    [TestCase("time-sign")]
    [TestCase("handle-sign")]
    [TestCase("handle-valid")]
    [TestCase("bezier-handles")]
    [TestCase("mode")]
    [TestCase("range")]
    public void AnyBitModeOrHandleDifferenceKeepsDistinctBindings(string kind)
    {
        var lower = "0,1,linear,linear,0,0,0,0,1";
        var upper = lower;
        switch (kind)
        {
            case "value-sign": lower = "0,-0,linear,linear,0,0,0,0,1"; upper = "0,0,linear,linear,0,0,0,0,1"; break;
            case "time-sign": upper = "-0,1,linear,linear,0,0,0,0,1"; break;
            case "handle-sign": upper = "0,1,linear,linear,-0,0,0,0,1"; break;
            case "handle-valid": upper = "0,1,linear,linear,unused,0,0,0,1"; break;
            case "bezier-handles":
                lower = "0,2,bezier_c,bezier_c,0,0,.3333333333333333,4,1;1,2,bezier_c,bezier_c,-.3333333333333333,4,0,0,1";
                upper = lower.Replace(",4,", ",3,", StringComparison.Ordinal); break;
            case "mode": lower += ";1,3,linear,linear,0,0,0,0,1"; upper = lower.Replace("linear,linear", "linear,log3", StringComparison.Ordinal); break;
            case "range": upper = "0,2,linear,linear,0,0,0,0,1"; break;
        }
        var curve = new VfxPreviewCurve(Pair(lower, upper), "diagnostic", 1);
        var binding = Binding(curve);
        Assert.That(binding.Min, Is.Not.SameAs(binding.Max));
        var referenceMin = new VfxPreviewCurve($"x,0,0;{lower}:x,0;0,0", "diagnostic", 1);
        var referenceMax = new VfxPreviewCurve($"x,0,0;{upper}:x,0;0,0", "diagnostic", 1);
        foreach (var time in new[] { -1d, 0, .25, .5, .75, 1, 2, double.NaN })
        {
            var actual = curve.Range("x", time);
            Assert.That(Bits(actual.Min), Is.EqualTo(Bits(referenceMin.Range("x", time).Min)));
            Assert.That(Bits(actual.Max), Is.EqualTo(Bits(referenceMax.Range("x", time).Max)));
        }
    }

    private static string Pair(string lower, string upper) => $"x_min,0,0;{lower}|x_max,0,0;{upper}:x,1;0,1";
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static (object Min, object Max) Binding(VfxPreviewCurve curve)
    {
        var bindings = (IDictionary)typeof(VfxPreviewCurve).GetField("_bindings", Private)!.GetValue(curve)!;
        var pair = bindings["x"]!;
        return (pair.GetType().GetField("Item1")!.GetValue(pair)!, pair.GetType().GetField("Item2")!.GetValue(pair)!);
    }
}
