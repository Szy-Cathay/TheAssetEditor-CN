#nullable enable
using System.Collections;
using System.Reflection;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxCurveFastPathTests
{
    [Test]
    public void SharedDynamicChannel_RangeReadsTheSegmentOnlyOnce()
    {
        var curve = new VfxPreviewCurve("x,0,0;0,1,linear,linear,0,0,0,0,1;1,5,linear,linear,0,0,0,0,1:x,0;0,0", "scale", 1);
        var reads = ObservePoints(curve, "x", upper: false);

        var actual = curve.Range("x", .25);

        Assert.Multiple(() =>
        {
            Bits(actual.Min, 2);
            Bits(actual.Max, 2);
            Assert.That(reads.IndexReads, Is.EqualTo(3), "One shared immutable channel must not evaluate its same segment twice.");
        });
    }

    [TestCase("0", 0)]
    [TestCase("-0", int.MinValue)]
    [TestCase("16777217", 0x4b800000)]
    public void SingleKey_KeepsItsFloatBitsWithoutReadingKeysDuringSampling(string value, int bits)
    {
        var curve = new VfxPreviewCurve($"x,0,0;.375,{value},linear,linear,unused,0,unused,0,1:x,0;0,0", "scale", 1);
        var reads = ObservePoints(curve, "x", upper: false);

        foreach (var time in new[] { double.NegativeInfinity, -1, 0, .375, .5, 2, double.PositiveInfinity, double.NaN })
        {
            var range = curve.Range("x", time);
            Assert.That(BitConverter.SingleToInt32Bits(range.Min), Is.EqualTo(bits));
            Assert.That(BitConverter.SingleToInt32Bits(range.Max), Is.EqualTo(bits));
        }

        Assert.That(reads.IndexReads, Is.Zero, "A true single key was already copied to an immutable float at construction.");
    }

    [Test]
    public void SeparateSingleKeys_KeepTheOriginalRangeSampleArithmetic()
    {
        var curve = new VfxPreviewCurve("x_min,0,0;0,-0,linear,linear,0,0,0,0,1|x_max,0,0;0,2,linear,linear,0,0,0,0,1:x,1;0,1", "scale", 1);
        var lowerReads = ObservePoints(curve, "x", upper: false);
        var upperReads = ObservePoints(curve, "x", upper: true);
        var expectedMin = BitConverter.Int32BitsToSingle(int.MinValue);
        foreach (var random in new[] { 0f, BitConverter.Int32BitsToSingle(int.MinValue), .125f, .5f, 1f,
            float.NegativeInfinity, float.PositiveInfinity, float.NaN })
        {
            var expected = expectedMin + (2 - expectedMin) * random;
            Assert.That(BitConverter.SingleToInt32Bits(curve.Sample("x", .5, random)),
                Is.EqualTo(BitConverter.SingleToInt32Bits(expected)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(lowerReads.IndexReads, Is.Zero);
            Assert.That(upperReads.IndexReads, Is.Zero);
        });
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

    private static IPointReads ObservePoints(VfxPreviewCurve curve, string axis, bool upper)
    {
        var bindings = (IDictionary)typeof(VfxPreviewCurve).GetField("_bindings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(curve)!;
        var binding = bindings[axis]!;
        var channel = binding.GetType().GetField(upper ? "Item2" : "Item1")!.GetValue(binding)!;
        var points = channel.GetType().GetProperty("Points")!;
        var original = points.GetValue(channel)!;
        var element = points.PropertyType.GenericTypeArguments.Single();
        var observed = Activator.CreateInstance(typeof(ObservedPoints<>).MakeGenericType(element), original)!;
        // The wrapper delegates every value unchanged; it only observes reads from the production path.
        points.SetValue(channel, observed);
        return (IPointReads)observed;
    }

    private interface IPointReads
    {
        int IndexReads { get; }
    }

    private sealed class ObservedPoints<T> : IReadOnlyList<T>, IPointReads
    {
        private readonly IReadOnlyList<T> _source;
        public ObservedPoints(object source) => _source = (IReadOnlyList<T>)source;
        public int IndexReads { get; private set; }
        public int Count => _source.Count;
        public T this[int index] { get { ++IndexReads; return _source[index]; } }
        public IEnumerator<T> GetEnumerator() => _source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
