using System.Globalization;

namespace Shared.GameFormats.Vfx;

// Keep the channel metadata, tangent modes, handles and channel links verbatim.
// Only the selected key's time/value tokens are edited; unknown layouts stay opaque.
public sealed class VfxCurve
{
    private readonly string _links;
    public IReadOnlyList<VfxCurveChannel> Channels { get; }

    private VfxCurve(List<VfxCurveChannel> channels, string links)
    {
        Channels = channels;
        _links = links;
    }

    public static bool TryParse(string text, out VfxCurve? curve)
    {
        curve = null;
        var split = text.Split(':');
        if (split.Length != 2 || split[1].Length == 0)
            return false;
        var channels = new List<VfxCurveChannel>();
        foreach (var rawChannel in split[0].Split('|'))
        {
            var parts = rawChannel.Split(';');
            var header = parts[0].Split(',');
            if (header.Length != 3 || string.IsNullOrWhiteSpace(header[0]))
                return false;
            var points = new List<VfxCurvePoint>();
            foreach (var rawPoint in parts.Skip(1))
            {
                var tokens = rawPoint.Split(',');
                if (tokens.Length != 9 || !TryNumber(tokens[0], out _) || !TryNumber(tokens[1], out _))
                    return false;
                points.Add(new VfxCurvePoint(tokens));
            }
            channels.Add(new VfxCurveChannel(parts[0], header[0], points));
        }
        if (channels.Count == 0)
            return false;
        curve = new VfxCurve(channels, split[1]);
        return true;
    }

    public static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && Math.Abs(value) <= float.MaxValue;

    public override string ToString() => string.Join('|', Channels.Select(x => x.ToString())) + ":" + _links;
}

public sealed class VfxCurveChannel(string header, string name, IReadOnlyList<VfxCurvePoint> points)
{
    public string Name { get; } = name;
    // Display order must not change the serialized key order or discard coincident keys.
    public IReadOnlyList<VfxCurvePoint> Points { get; } = points.OrderBy(x => x.Time).ToArray();
    public override string ToString() => points.Count == 0 ? header : header + ";" + string.Join(';', points.Select(x => x.ToString()));

    public bool CanMovePoint(int index, double time) => double.IsFinite(time) && Math.Abs(time) <= float.MaxValue
        && (time == Points[index].Time
            || ((index == 0 || time > Points[index - 1].Time)
                && (index == Points.Count - 1 || time < Points[index + 1].Time)));

    public void MovePoint(int index, double time, double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (!CanMovePoint(index, time))
            throw new ArgumentOutOfRangeException(nameof(time));
        Points[index].Set(time, value);
    }
}

public sealed class VfxCurvePoint
{
    private readonly string[] _tokens;
    public double Time => double.Parse(_tokens[0], CultureInfo.InvariantCulture);
    public double Value => double.Parse(_tokens[1], CultureInfo.InvariantCulture);
    public string InMode => _tokens[2];
    public string OutMode => _tokens[3];

    public bool TryGetHandles(out (double Time, double Value) incoming, out (double Time, double Value) outgoing)
    {
        incoming = outgoing = default;
        if (!VfxCurve.TryNumber(_tokens[4], out var ix) || !VfxCurve.TryNumber(_tokens[5], out var iy)
            || !VfxCurve.TryNumber(_tokens[6], out var ox) || !VfxCurve.TryNumber(_tokens[7], out var oy))
            return false;
        incoming = (ix, iy);
        outgoing = (ox, oy);
        return true;
    }

    internal VfxCurvePoint(string[] tokens) => _tokens = tokens;

    internal void Set(double time, double value)
    {
        if (time != Time)
            _tokens[0] = time.ToString("G17", CultureInfo.InvariantCulture);
        if (value != Value)
            _tokens[1] = value.ToString("G17", CultureInfo.InvariantCulture);
    }

    public override string ToString() => string.Join(',', _tokens);
}
