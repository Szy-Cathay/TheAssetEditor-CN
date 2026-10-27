using System.Globalization;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

// Ordinary GPU scalar rows use i/128 half texels, independently of model CPU tables.
internal sealed class VfxSpriteNativeSpinDamping
{
    private const string Field = "ParticleSpinDeceleration";
    private readonly float[] _samples;
    public float Minimum { get; }
    public float Maximum { get; }

    private VfxSpriteNativeSpinDamping(float[] samples)
    {
        _samples = samples;
        Minimum = samples.Min(); Maximum = samples.Max();
    }

    public static VfxSpriteNativeSpinDamping Create(string text)
    {
        if (!VfxCurve.TryParse(text, out var curve)) throw new VfxMappingException(Field, "Vfx.Preview.InvalidData");
        var channels = curve!.Channels;
        // The GPU upload reads raw 0/1; do not infer missing channels or remap links.
        if (channels.Count != 2 || channels[0].Name != "x_min" || channels[1].Name != "x_max")
            throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var mapping = text.Split(':')[1].Split(';');
        var header = mapping[0].Split(',');
        var indexes = mapping.Length == 2 ? mapping[1].Split(',') : [];
        if (header.Length != 2 || header[0] != "x"
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var split) || split is < 0 or > 1
            || indexes.Length != 2 || !int.TryParse(indexes[0], out var lower) || lower != 0
            || !int.TryParse(indexes[1], out var upper) || upper != 1)
            throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var minimum = Points(channels[0]); var maximum = Points(channels[1]);
        if (!minimum.AsSpan().SequenceEqual(maximum))
            throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var values = new float[128];
        for (var index = 0; index < values.Length; ++index)
        {
            var time = index / 128f;
            var first = VfxSpriteConstantSpinDamping.HalfBits(Evaluate(minimum, time));
            var second = VfxSpriteConstantSpinDamping.HalfBits(Evaluate(maximum, time));
            if (first != second) throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
            values[index] = (float)BitConverter.UInt16BitsToHalf(first);
            if (!float.IsFinite(values[index])) throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
        }
        return new(values);
    }

    public float Sample(double phase)
    {
        if (double.IsNaN(phase)) throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
        var coordinate = Math.Clamp((float)phase, 0, 1) * 128.0 - .5;
        var lower = (int)Math.Floor(coordinate);
        var fraction = coordinate - lower;
        // Portable linear-clamp geometry. GPU subtexel and filter rounding can vary.
        return (float)((1 - fraction) * _samples[Math.Clamp(lower, 0, 127)]
            + fraction * _samples[Math.Clamp(lower + 1, 0, 127)]);
    }

    public float BoundAt(int index) => _samples[index];

    private static Point[] Points(VfxCurveChannel channel)
    {
        if (channel.Points.Count == 0) throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var result = channel.Points.Select(point =>
        {
            if (!point.TryGetHandles(out var incoming, out var outgoing)
                || !int.TryParse(point.ToString().Split(',')[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag) || flag is < 0 or > 1)
                throw new VfxMappingException(Field, "Vfx.Preview.InvalidData");
            return new Point((float)point.Time, (float)point.Value, point.InMode, point.OutMode,
                (float)incoming.Time, (float)incoming.Value, (float)outgoing.Time, (float)outgoing.Value, flag);
        }).ToArray();
        for (var index = 1; index < result.Length; ++index)
        {
            var left = result[index - 1]; var right = result[index];
            if (right.Time <= left.Time) throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
            if (!float.IsFinite(right.Time - left.Time)) throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
            if (!Supported(left.OutMode) || !Supported(right.InMode))
                throw new VfxMappingException(Field, "Vfx.Preview.InterpolationUnsupported");
        }
        return result;
    }

    private static bool Supported(string mode) => mode is "linear" or "exp1" or "exp3" or "log1" or "log3" or "sine" or "sine_recip" or "s";

    private static float Evaluate(Point[] points, float time)
    {
        if (time <= points[0].Time) return points[0].Value;
        for (var index = 1; index < points.Length; ++index)
        {
            var right = points[index];
            if (time > right.Time) continue;
            if (time == right.Time) return right.Value;
            var left = points[index - 1]; var fraction = (time - left.Time) / (right.Time - left.Time);
            var outgoing = Endpoint(left.OutMode, left.Value, right.Value, fraction);
            var incoming = Endpoint(right.InMode, left.Value, right.Value, fraction);
            return outgoing + (incoming - outgoing) * fraction;
        }
        return points[^1].Value;
    }

    private static float Endpoint(string mode, float start, float end, float phase)
    {
        // Same shipped Utility endpoint instructions, with no model table/time sampler.
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
            default: throw new InvalidOperationException("Unvalidated native sprite curve mode.");
        }
    }

    private readonly record struct Point(float Time, float Value, string InMode, string OutMode,
        float InTime, float InValue, float OutTime, float OutValue, int Flag);
}
