#nullable enable
using System.Collections;
using System.Reflection;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxCurveFastPathTests
{
    [Test]
    public void SharedDynamicChannel_KeepsOneBindingAndTheOriginalSegmentBits()
    {
        var curve = new VfxPreviewCurve("x,0,0;0,1,linear,linear,0,0,0,0,1;1,5,linear,linear,0,0,0,0,1:x,0;0,0", "scale", 1);
        var binding = Binding(curve, "x");

        var actual = curve.Range("x", .25);

        Assert.Multiple(() =>
        {
            Bits(actual.Min, 2);
            Bits(actual.Max, 2);
            Assert.That(binding.GetType().GetField("Item1")!.GetValue(binding),
                Is.SameAs(binding.GetType().GetField("Item2")!.GetValue(binding)),
                "The existing ReferenceEquals branch must receive the same immutable channel.");
        });
    }

    [TestCase("0", 0)]
    [TestCase("-0", int.MinValue)]
    [TestCase("16777217", 0x4b800000)]
    public void SingleKey_KeepsItsFloatBitsWithoutReadingKeysDuringSampling(string value, int bits)
    {
        var curve = new VfxPreviewCurve($"x,0,0;.375,{value},linear,linear,unused,0,unused,0,1:x,0;0,0", "scale", 1);
        RemovePointKeys(curve, "x", upper: false);

        foreach (var time in new[] { double.NegativeInfinity, -1, 0, .375, .5, 2, double.PositiveInfinity, double.NaN })
        {
            var range = curve.Range("x", time);
            Assert.That(BitConverter.SingleToInt32Bits(range.Min), Is.EqualTo(bits));
            Assert.That(BitConverter.SingleToInt32Bits(range.Max), Is.EqualTo(bits));
        }
    }

    [Test]
    public void SeparateSingleKeys_KeepTheOriginalRangeSampleArithmetic()
    {
        var curve = new VfxPreviewCurve("x_min,0,0;0,-0,linear,linear,0,0,0,0,1|x_max,0,0;0,2,linear,linear,0,0,0,0,1:x,1;0,1", "scale", 1);
        RemovePointKeys(curve, "x", upper: false);
        RemovePointKeys(curve, "x", upper: true);
        var expectedMin = BitConverter.Int32BitsToSingle(int.MinValue);
        foreach (var random in new[] { 0f, BitConverter.Int32BitsToSingle(int.MinValue), .125f, .5f, 1f,
            float.NegativeInfinity, float.PositiveInfinity, float.NaN })
        {
            var expected = expectedMin + (2 - expectedMin) * random;
            Assert.That(BitConverter.SingleToInt32Bits(curve.Sample("x", .5, random)),
                Is.EqualTo(BitConverter.SingleToInt32Bits(expected)));
        }
    }

    [Test]
    public void EqualBezierEndpoints_WithNonzeroHandlesKeepTheirInteriorShape()
    {
        var curve = new VfxPreviewCurve("x,0,0;0,2,bezier,bezier,0,0,.3333333333333333,4,1;1,2,bezier,bezier,-.3333333333333333,4,0,0,1:x,0;0,0", "scale", 1);
        var actual = curve.Range("x", .5);
        // Symmetric cubic control values 2,6,6,2 have midpoint 5, despite equal endpoint values.
        Bits(actual.Min, 5);
        Bits(actual.Max, 5);
    }

    [Test]
    public void MultipleNegativeZeroKeys_KeepTheInteriorAdditionAndNonfiniteTimeBehaviour()
    {
        var curve = new VfxPreviewCurve("x,0,0;0,-0,linear,linear,0,0,0,0,1;1,-0,linear,linear,0,0,0,0,1:x,0;0,0", "scale", 1);
        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.SingleToInt32Bits(curve.Range("x", -1).Min), Is.EqualTo(int.MinValue));
            Assert.That(BitConverter.SingleToInt32Bits(curve.Range("x", .5).Min), Is.Zero);
            Assert.That(BitConverter.SingleToInt32Bits(curve.Range("x", 2).Min), Is.EqualTo(int.MinValue));
            Assert.That(float.IsNaN(curve.Range("x", double.NaN).Min), Is.True);
        });
    }

    private static void Bits(float actual, float expected)
        => Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)));

    private static object Binding(VfxPreviewCurve curve, string axis)
    {
        var bindings = (IDictionary)typeof(VfxPreviewCurve).GetField("_bindings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(curve)!;
        return bindings[axis]!;
    }

    private static void RemovePointKeys(VfxPreviewCurve curve, string axis, bool upper)
    {
        var binding = Binding(curve, axis);
        var channel = binding.GetType().GetField(upper ? "Item2" : "Item1")!.GetValue(binding)!;
        var points = channel.GetType().GetProperty("Points")!;
        // Sampling must still preserve every cached bit after all source keys become inaccessible.
        points.SetValue(channel, Array.CreateInstance(points.PropertyType.GetElementType()!, 0));
        Assert.That(((Array)points.GetValue(channel)!).Length, Is.Zero);
    }
}
