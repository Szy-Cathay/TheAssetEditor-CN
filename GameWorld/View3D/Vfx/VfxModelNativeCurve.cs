using System.Globalization;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

// Model CPU curves use the shipped 128-sample float tables, independently of sprite curves.
internal sealed class VfxModelNativeCurve
{
    private readonly float[] _minimum;
    private readonly float[] _maximum;
    public int SampleCount => _minimum.Length;
    public float Minimum { get; }
    public float Maximum { get; }

    private VfxModelNativeCurve(float[] minimum, float[] maximum)
    {
        _minimum = minimum; _maximum = maximum;
        Minimum = Math.Min(minimum.Min(), maximum.Min());
        Maximum = Math.Max(minimum.Max(), maximum.Max());
    }

    public static VfxModelNativeCurve Create(string text, string field, string axis = "x")
    {
        if (!VfxCurve.TryParse(text, out var curve)) throw new VfxMappingException(field, "Vfx.Preview.InvalidData");
        var links = text.Split(':')[1].Split('|').Select(link => link.Split(';')).ToArray();
        var matches = links.Where(link => link.Length == 2 && link[0].Split(',')[0] == axis).ToArray();
        if (matches.Length != 1) throw new VfxMappingException(field, "Vfx.Preview.AmbiguousField");
        var header = matches[0][0].Split(','); var indexes = matches[0][1].Split(',');
        if (header.Length != 2 || indexes.Length != 2 || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var split)
            || split is < 0 or > 1 || !int.TryParse(indexes[0], out var lower) || !int.TryParse(indexes[1], out var upper)
            || lower < 0 || upper < 0 || lower >= curve!.Channels.Count || upper >= curve.Channels.Count)
            throw new VfxMappingException(field, "Vfx.Preview.InvalidData");
        var minimum = Points(curve!.Channels[lower], field);
        var maximum = Points(curve.Channels[upper], field);
        var count = minimum.Length <= 1 && maximum.Length <= 1 ? 1 : 128;
        var minSamples = new float[count]; var maxSamples = new float[count];
        for (var index = 0; index < count; ++index)
        {
            var time = index * (1f / 127f);
            minSamples[index] = Evaluate(minimum, time);
            maxSamples[index] = Evaluate(maximum, time);
            if (!float.IsFinite(minSamples[index]) || !float.IsFinite(maxSamples[index]))
                throw new VfxMappingException(field, "Vfx.Preview.RangeUnsupported");
        }
        if (split == 0 && !minSamples.AsSpan().SequenceEqual(maxSamples))
            throw new VfxMappingException(field, "Vfx.Preview.AmbiguousField");
        return new(minSamples, maxSamples);
    }

    public float Sample(double phase, float unary)
    {
        var time = Math.Clamp((float)phase, 0, 1);
        var position = time * (SampleCount - 1);
        var lower = (int)position; var upper = Math.Min(lower + 1, SampleCount - 1);
        var fraction = position - lower;
        var minimum = _minimum[lower] + (_minimum[upper] - _minimum[lower]) * fraction;
        var maximum = _maximum[lower] + (_maximum[upper] - _maximum[lower]) * fraction;
        return minimum + (maximum - minimum) * unary;
    }

    public float SampleBoundAt(int index, bool upper) => (upper ? _maximum : _minimum)[Math.Clamp(index, 0, SampleCount - 1)];

    private static Point[] Points(VfxCurveChannel channel, string field)
    {
        if (channel.Points.Count == 0 || channel.Points.GroupBy(point => point.Time).Any(group => group.Count() > 1))
            throw new VfxMappingException(field, "Vfx.Preview.AmbiguousField");
        var result = channel.Points.Select(point =>
        {
            if (!point.TryGetHandles(out var incoming, out var outgoing))
                throw new VfxMappingException(field, "Vfx.Preview.InvalidData");
            return new Point((float)point.Time, (float)point.Value, point.InMode, point.OutMode,
                (float)incoming.Time, (float)incoming.Value, (float)outgoing.Time, (float)outgoing.Value);
        }).ToArray();
        for (var index = 1; index < result.Length; ++index)
        {
            var left = result[index - 1]; var right = result[index];
            if (right.Time <= left.Time) throw new VfxMappingException(field, "Vfx.Preview.AmbiguousField");
            if (!Supported(left.OutMode) || !Supported(right.InMode))
                throw new VfxMappingException(field, "Vfx.Preview.InterpolationUnsupported");
            if (!float.IsFinite(left.Time + left.OutTime) || !float.IsFinite(left.Value + left.OutValue)
                || !float.IsFinite(right.Time + right.InTime) || !float.IsFinite(right.Value + right.InValue))
                throw new VfxMappingException(field, "Vfx.Preview.RangeUnsupported");
        }
        return result;
    }

    private static bool Supported(string mode) => mode is "linear" or "exp1" or "exp3" or "log1" or "log3"
        or "sine" or "sine_recip" or "s" or "bezier_c";

    private static float Evaluate(Point[] points, float time)
    {
        if (time <= points[0].Time) return points[0].Value;
        for (var index = 1; index < points.Length; ++index)
        {
            var right = points[index];
            if (time > right.Time) continue;
            if (time == right.Time) return right.Value;
            var left = points[index - 1]; var fraction = (time - left.Time) / (right.Time - left.Time);
            var outgoing = Endpoint(left.OutMode, left, right, time, fraction);
            var incoming = Endpoint(right.InMode, left, right, time, fraction);
            return outgoing + (incoming - outgoing) * fraction;
        }
        return points[^1].Value;
    }

    private static float Endpoint(string mode, Point left, Point right, float time, float phase)
    {
        var start = left.Value; var end = right.Value;
        switch (mode)
        {
            case "bezier_c": return CubicValue(left, right, time);
            case "linear": return start + (end - start) * phase;
            case "exp1": return start + ((end - start) * .5f) * ((phase + 1) * phase);
            case "exp3": return start + ((end - start) * phase) * (phase * phase);
            case "log1": return start + ((start - end) * .5f) * ((phase - 3) * phase);
            case "log3": return end + ((start - end) * (1 - phase)) * ((1 - phase) * (1 - phase));
            case "sine":
            {
                var square = (phase * phase) * 2.4674012660980225f;
                var value = .008306325413286686f - square * .00018363654089625925f;
                value = value * square - .1666482836008072f;
                value = value * square + .9999966025352478f;
                return start + (value * (phase * 1.5707963705062866f)) * (end - start);
            }
            case "sine_recip":
            {
                var square = (phase * phase) * 2.4674012660980225f;
                var value = .04148774966597557f - square * .0012712094467133284f;
                value = value * square - .49991244077682495f;
                value = value * square + .9999933242797852f;
                return end + value * (start - end);
            }
            case "s":
            {
                var square = (phase * phase) * 9.86960506439209f;
                var value = square * .00048483398859389126f - .0196138396859169f;
                value = value * square + .24767479300498962f;
                value = value * square + .0006967021618038416f;
                return start + value * (end - start);
            }
            default: throw new InvalidOperationException("Unvalidated native model curve mode.");
        }
    }

    private static float CubicValue(Point left, Point right, float time)
    {
        var p0 = (left.Time, left.Value); var p1 = (left.Time + left.OutTime, left.Value + left.OutValue);
        var p2 = (right.Time + right.InTime, right.Value + right.InValue); var p3 = (right.Time, right.Value);
        if (p0.Value == p1.Item2 && p1.Item2 == p2.Item2 && p2.Item2 == p3.Value) return p0.Value;
        var low = 0.0; var high = 1.0;
        for (var iteration = 0; iteration < 10; ++iteration)
        {
            var middle = (low + high) * .5;
            if (CubicPoint(p0, p1, p2, p3, middle).Time > time) high = middle; else low = middle;
        }
        var step = (high - low) * .01; var maximumTime = p0.Time; var value = p0.Value;
        for (var parameter = low; parameter < high; parameter += step)
        {
            var point = CubicPoint(p0, p1, p2, p3, parameter);
            maximumTime = Math.Max(maximumTime, point.Time); value = point.Value;
            if (maximumTime >= time) break;
        }
        return value;
    }

    private static (float Time, float Value) CubicPoint((float Time, float Value) p0, (float Time, float Value) p1,
        (float Time, float Value) p2, (float Time, float Value) p3, double t)
    {
        var inverse = 1 - t; var square = t * t;
        var w0 = (float)(inverse * inverse * inverse); var w1 = (float)(((inverse * inverse) * 3) * t);
        var w2 = (float)((inverse * 3) * square); var w3 = (float)(square * t);
        var time = p0.Time * w0 + p1.Time * w1; time += p2.Time * w2; time += p3.Time * w3;
        var value = p0.Value * w0 + p1.Value * w1; value += p2.Value * w2; value += p3.Value * w3;
        return (time, value);
    }

    private readonly record struct Point(float Time, float Value, string InMode, string OutMode,
        float InTime, float InValue, float OutTime, float OutValue);
}
