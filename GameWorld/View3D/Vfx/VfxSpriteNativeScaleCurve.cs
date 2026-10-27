using System.Globalization;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

// Ordinary GPU size curves use two XYZ half rows, independently of rigid model tables.
internal sealed class VfxSpriteNativeScaleCurve
{
    private const string Field = "ParticleScale";
    private readonly float[][] _rows;

    private VfxSpriteNativeScaleCurve(float[][] rows) => _rows = rows;

    public static VfxSpriteNativeScaleCurve Create(string text)
    {
        if (!VfxCurve.TryParse(text, out var curve)) throw new VfxMappingException(Field, "Vfx.Preview.InvalidData");
        string[] names = ["x_min", "x_max", "y_min", "y_max", "z_min", "z_max"];
        if (curve!.Channels.Count != names.Length || !curve.Channels.Select(channel => channel.Name).SequenceEqual(names))
            throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var links = text.Split(':')[1].Split('|');
        if (links.Length != 3) throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var splitFlags = new int[3];
        for (var axis = 0; axis < 3; ++axis)
        {
            var parts = links[axis].Split(';'); var header = parts[0].Split(',');
            var indexes = parts.Length == 2 ? parts[1].Split(',') : [];
            if (header.Length != 2 || header[0] != "xyz"[axis].ToString()
                || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out splitFlags[axis]) || splitFlags[axis] is < 0 or > 1
                || indexes.Length != 2 || !int.TryParse(indexes[0], out var lower) || lower != axis * 2
                || !int.TryParse(indexes[1], out var upper) || upper != axis * 2 + 1)
                throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        }
        var points = curve.Channels.Select(Points).ToArray();
        var rows = new float[6][];
        for (var channel = 0; channel < rows.Length; ++channel)
        {
            rows[channel] = new float[128];
            for (var index = 0; index < 128; ++index)
            {
                var value = Evaluate(points[channel], index / 128f);
                rows[channel][index] = (float)BitConverter.UInt16BitsToHalf(VfxSpriteConstantSpinDamping.HalfBits(value));
                if (!float.IsFinite(rows[channel][index])) throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
            }
        }
        for (var axis = 0; axis < 3; ++axis)
            if (splitFlags[axis] == 0 && Enumerable.Range(0, 128).Any(index =>
                BitConverter.SingleToUInt32Bits(rows[axis * 2][index]) != BitConverter.SingleToUInt32Bits(rows[axis * 2 + 1][index])))
                throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        return new(rows);
    }

    public VfxRange Range(string axis, double phase) => new(Sample(axis, phase, 0), Sample(axis, phase, 1));

    internal static VfxSpriteNativeScaleCurve? TryCreateAnalytic(string? text, string? scalarAxis = null)
    {
        if (string.IsNullOrEmpty(text) || !VfxCurve.TryParse(text, out var parsed)) return null;
        if (parsed!.Channels.Any(channel => channel.Points.Count == 0 || channel.Points.Zip(channel.Points.Skip(1))
            .Any(pair => !Analytic(pair.First.OutMode) || !Analytic(pair.Second.InMode)))) return null;
        try
        {
            if (scalarAxis != null)
            {
                if (parsed.Channels.Count != 2 || parsed.Channels[0].Name != scalarAxis + "_min"
                    || parsed.Channels[1].Name != scalarAxis + "_max") return null;
                var links = text[(text.IndexOf(':') + 1)..].Split(';');
                var header = links[0].Split(',');
                if (links.Length != 2 || header.Length != 2 || header[0] != scalarAxis
                    || header[1] is not ("0" or "1") || links[1] != "0,1") return null;
                var channels = new List<VfxCurveChannel>();
                foreach (var axis in "xyz")
                    for (var bound = 0; bound < 2; bound++)
                    {
                        var name = axis + (bound == 0 ? "_min" : "_max");
                        channels.Add(new(name + ",0,1", name, parsed.Channels[bound].Points));
                    }
                text = string.Join('|', channels) + $":x,{header[1]};0,1|y,{header[1]};2,3|z,{header[1]};4,5";
            }
            var table = Create(text);
            // Different min/max tables and their hardware vertical filtering remain uncalibrated.
            for (var axis = 0; axis < 3; axis++)
                for (var index = 0; index < 128; index++)
                    if (BitConverter.SingleToUInt32Bits(table._rows[axis * 2][index])
                        != BitConverter.SingleToUInt32Bits(table._rows[axis * 2 + 1][index])) return null;
            return table;
        }
        catch (VfxMappingException) { return null; }
    }

    public float Sample(string axis, double phase, float unary)
    {
        if (double.IsNaN(phase) || !float.IsFinite(unary) || unary is < 0 or > 1)
            throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
        var channel = Axis(axis) * 2;
        var coordinate = Math.Clamp((float)phase, 0, 1) * 128.0 - .5;
        var index = (int)Math.Floor(coordinate); var fraction = coordinate - index;
        var lower = Math.Clamp(index, 0, 127); var upper = Math.Clamp(index + 1, 0, 127);
        var minimum = (1 - fraction) * _rows[channel][lower] + fraction * _rows[channel][upper];
        var maximum = (1 - fraction) * _rows[channel + 1][lower] + fraction * _rows[channel + 1][upper];
        // Portable bilinear geometry; fixed-function GPU subtexel/output rounding can differ.
        var weight = (double)unary;
        return (float)((1 - weight) * minimum + weight * maximum);
    }

    public float SampleBoundAt(string axis, int index, bool upper) => _rows[Axis(axis) * 2 + (upper ? 1 : 0)][index];

    private static int Axis(string axis) => axis switch
    {
        "x" => 0, "y" => 1, "z" => 2,
        _ => throw new VfxMappingException(Field, "Vfx.Preview.InvalidData"),
    };

    private static Point[] Points(VfxCurveChannel channel)
    {
        if (channel.Points.Count == 0) throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var result = channel.Points.Select(point =>
        {
            if (!point.TryGetHandles(out var incoming, out var outgoing)
                || !int.TryParse(point.ToString().Split(',')[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag) || flag is < 0 or > 1)
                throw new VfxMappingException(Field, "Vfx.Preview.InvalidData");
            return new Point((float)point.Time, (float)point.Value, point.InMode, point.OutMode,
                (float)incoming.Time, (float)incoming.Value, (float)outgoing.Time, (float)outgoing.Value);
        }).ToArray();
        for (var index = 1; index < result.Length; ++index)
        {
            var left = result[index - 1]; var right = result[index];
            if (right.Time <= left.Time) throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
            if (!float.IsFinite(right.Time - left.Time) || !float.IsFinite(left.Time + left.OutTime)
                || !float.IsFinite(left.Value + left.OutValue) || !float.IsFinite(right.Time + right.InTime)
                || !float.IsFinite(right.Value + right.InValue))
                throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
            var quadratic = left.OutMode == "bezier_q" || right.InMode == "bezier_q";
            if (quadratic)
            {
                // Only independently executed endpoint pairs are opened for this size path.
                if ((left.OutMode, right.InMode) is not (("linear", "bezier_q") or ("bezier_q", "linear")
                    or ("bezier_q", "bezier_q") or ("bezier_q", "sine_recip")))
                    throw new VfxMappingException(Field, "Vfx.Preview.InterpolationUnsupported");
                var controlTime = left.Time + left.OutTime;
                if (controlTime < left.Time || controlTime > right.Time)
                    throw new VfxMappingException(Field, "Vfx.Preview.InterpolationUnsupported");
            }
            else if (!Analytic(left.OutMode) || !Analytic(right.InMode))
                throw new VfxMappingException(Field, "Vfx.Preview.InterpolationUnsupported");
        }
        return result;
    }

    private static bool Analytic(string mode) => mode is "linear" or "exp1" or "exp3" or "log1" or "log3" or "sine" or "sine_recip" or "s";

    private static float Evaluate(Point[] points, float time)
    {
        if (time <= points[0].Time) return points[0].Value;
        for (var index = 1; index < points.Length; ++index)
        {
            var right = points[index];
            if (time > right.Time) continue;
            if (time == right.Time) return right.Value;
            var left = points[index - 1]; var fraction = (time - left.Time) / (right.Time - left.Time);
            var quadratic = left.OutMode == "bezier_q" || right.InMode == "bezier_q" ? Quadratic(left, right, time) : 0;
            var outgoing = left.OutMode == "bezier_q" ? quadratic : Endpoint(left.OutMode, left.Value, right.Value, fraction);
            var incoming = right.InMode == "bezier_q" ? quadratic : Endpoint(right.InMode, left.Value, right.Value, fraction);
            return outgoing + (incoming - outgoing) * fraction;
        }
        return points[^1].Value;
    }

    private static float Quadratic(Point left, Point right, float time)
    {
        var p0 = (Time: left.Time, Value: left.Value); var p3 = (Time: right.Time, Value: right.Value);
        // Native mode11 uses the LEFT outgoing control even for an incoming quadratic endpoint.
        var control = (Time: left.Time + left.OutTime, Value: left.Value + left.OutValue);
        var p1 = (Time: left.Time + (control.Time - left.Time) * .6667f,
            Value: left.Value + (control.Value - left.Value) * .6667f);
        var p2 = (Time: right.Time + (control.Time - right.Time) * .6667f,
            Value: right.Value + (control.Value - right.Value) * .6667f);
        if (p0.Value == p1.Value && p1.Value == p2.Value && p2.Value == p3.Value) return p0.Value;
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

    private static float Endpoint(string mode, float start, float end, float phase)
    {
        switch (mode)
        {
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
            default: throw new InvalidOperationException("Unvalidated native sprite scale endpoint.");
        }
    }

    private readonly record struct Point(float Time, float Value, string InMode, string OutMode,
        float InTime, float InValue, float OutTime, float OutValue);
}
