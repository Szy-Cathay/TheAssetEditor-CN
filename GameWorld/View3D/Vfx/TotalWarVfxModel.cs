using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal sealed class VfxMappingException(string field, string reason) : Exception
{
    public string Field { get; } = field;
    public string Reason { get; } = reason;
}

internal readonly record struct VfxRange(float Min, float Max)
{
    public float Sample(float random) => Min + (Max - Min) * random;
}

// Authored channels remain separate: a particle retains its random interpolation weight for its entire life.
internal sealed class VfxPreviewCurve
{
    private readonly IReadOnlyList<SampleChannel> _channels;
    private readonly Dictionary<string, (SampleChannel Min, SampleChannel Max)> _bindings = new(StringComparer.Ordinal);
    private readonly float _fallback;
    private readonly string _field;
    private readonly bool _omittedScaleAxes;
    private readonly VfxSpriteNativeScaleCurve? _nativeScale;

    public VfxPreviewCurve(string? text, string field, float fallback, bool planarScale = false)
    {
        _field = field;
        _fallback = fallback;
        if (text == null) { _channels = []; return; }
        if (!VfxCurve.TryParse(text, out var curve)) throw new VfxMappingException(field, "Vfx.Preview.InvalidData");
        var channels = curve!.Channels;
        if (planarScale && field == "ParticleScale" && channels.Any(channel => channel.Points.Zip(channel.Points.Skip(1))
            .Any(pair => pair.First.OutMode == "bezier_q" || pair.Second.InMode == "bezier_q")))
            _nativeScale = VfxSpriteNativeScaleCurve.Create(text);
        foreach (var channel in channels)
        {
            if (channel.Points.Count == 0 && planarScale && (field is "ParticleScale" or "ParticleInitialScale")
                && (channel.Name is "z" or "z_min" or "z_max")) continue;
            if (channel.Points.Count == 0 || channel.Points.GroupBy(x => x.Time).Any(group => group.Select(point => point.Value).Distinct().Count() != 1))
                throw new VfxMappingException(field, "Vfx.Preview.AmbiguousField");
            for (var index = 1; _nativeScale == null && index < channel.Points.Count; ++index)
            {
                var left = channel.Points[index - 1]; var right = channel.Points[index];
                var analytic = IsAnalyticMode(left.OutMode) && IsAnalyticMode(right.InMode);
                if (!analytic && !IsMixedBezier(left.OutMode, right.InMode) && (!IsMode(left.OutMode) || !IsMode(right.InMode)))
                    throw new VfxMappingException(field, "Vfx.Preview.InterpolationUnsupported");
                if (!left.TryGetHandles(out _, out var outgoing) || !right.TryGetHandles(out var incoming, out _))
                    throw new VfxMappingException(field, "Vfx.Preview.InvalidData");
                if (!analytic && left.OutMode != "linear" && right.InMode != "linear"
                    && (outgoing.Time < 0 || incoming.Time > 0))
                    throw new VfxMappingException(field, "Vfx.Preview.InterpolationUnsupported");
            }
        }
        // Playback uses immutable numeric snapshots, not repeated parsing of the editable format's tokens.
        _channels = channels.Select(channel =>
        {
            if (channel.Points.Count == 0)
                return new SampleChannel(channel.Name, [new(0, 1, "linear", "linear", (0, 0), (0, 0), true)]);
            var points = channel.Points.GroupBy(point => point.Time).Select(group =>
            {
                // A zero-duration equal-value join keeps both adjacent segments' authored handles.
                var first = group.First(); var last = group.Last();
                var incomingValid = first.TryGetHandles(out var incoming, out _);
                var outgoingValid = last.TryGetHandles(out _, out var outgoing);
                return new SamplePoint(first.Time, first.Value, first.InMode, last.OutMode, incoming, outgoing, incomingValid && outgoingValid);
            }).ToArray();
            return new SampleChannel(channel.Name, points);
        }).ToArray();
        _omittedScaleAxes = (field is "ParticleScale" or "ParticleInitialScale")
            && _channels.Count > 0 && _channels.All(channel => channel.Name is "x" or "x_min" or "x_max");
        foreach (var axis in _channels.Select(channel => channel.Name.EndsWith("_min", StringComparison.Ordinal)
            || channel.Name.EndsWith("_max", StringComparison.Ordinal) ? channel.Name[..^4] : channel.Name).Distinct(StringComparer.Ordinal))
        {
            var min = _channels.FirstOrDefault(channel => channel.Name == axis + "_min" || channel.Name == axis);
            if (min != null) _bindings.Add(axis, (min, _channels.FirstOrDefault(channel => channel.Name == axis + "_max") ?? min));
        }
        bool Single(string axis) => _bindings.TryGetValue(axis, out var binding)
            && binding.Min.SingleValue is { } min && binding.Max.SingleValue is { } max
            && float.IsFinite(min) && float.IsFinite(max);
        HasSingleKeyVector = _nativeScale == null && channels.All(channel => channel.Points.Count == 1)
            && Single("x") && Single("y") && Single("z");
    }

    public bool HasSingleKeyVector { get; }
    public bool IsConstant => _channels.All(channel => channel.Points.All(x => x.Value == channel.Points[0].Value));
    public bool TryConstantRange(string axis, out VfxRange range)
    {
        range = default;
        if (_nativeScale != null) return false;
        if (_channels.Count == 0 || _omittedScaleAxes && axis is "y" or "z")
        {
            range = new(_fallback, _fallback);
            return true;
        }
        if (!_bindings.TryGetValue(axis, out var binding)) return false;
        static bool Constant(SampleChannel channel)
        {
            for (var index = 1; index < channel.Points.Count; ++index)
                if (channel.Points[index].Value != channel.Points[0].Value) return false;
            return true;
        }
        if (!Constant(binding.Min) || !Constant(binding.Max)) return false;
        range = new((float)binding.Min.Points[0].Value, (float)binding.Max.Points[0].Value);
        return true;
    }
    public bool IsZero => _channels.Count == 0 ? _fallback == 0 : _channels.All(x => x.Points.All(p => p.Value == 0));
    public float Maximum => _channels.Count == 0 ? _fallback : (float)_channels.Max(x => x.Points.Max(p => p.Value));
    public float Minimum => _channels.Count == 0 ? _fallback : (float)_channels.Min(x => x.Points.Min(p => p.Value));
    public bool HasAnalyticSegments => _channels.Any(x => x.Points.Zip(x.Points.Skip(1)).Any(pair =>
        IsAnalyticMode(pair.First.OutMode) && IsAnalyticMode(pair.Second.InMode)
        && (pair.First.OutMode != "linear" || pair.Second.InMode != "linear")));
    public bool HasBezierSegments => _channels.Any(x => x.Points.Zip(x.Points.Skip(1)).Any(pair =>
        !IsAnalyticMode(pair.First.OutMode) || !IsAnalyticMode(pair.Second.InMode)));
    public bool TryConstantVector(out Vector3 value)
    {
        value = Vector3.Zero;
        if (!IsConstant) return false;
        try
        {
            var x = Range("x", 0); var y = Range("y", 0); var z = Range("z", 0);
            if (x.Min != x.Max || y.Min != y.Max || z.Min != z.Max) return false;
            value = new(x.Min, y.Min, z.Min); return true;
        }
        catch (VfxMappingException) { return false; }
    }
    public void RequireChannels(params string[] names)
    {
        if (_channels.Count == 0) return;
        foreach (var name in names)
            if (!_channels.Any(x => x.Name == name || x.Name == name + "_min") && !(_omittedScaleAxes && name is "y" or "z"))
                throw new VfxMappingException(_field, "Vfx.Preview.InvalidData");
    }

    public VfxRange Range(string axis, double time)
    {
        if (_nativeScale != null) return _nativeScale.Range(axis, time);
        if (_channels.Count == 0) return new(_fallback, _fallback);
        if (!_bindings.TryGetValue(axis, out var binding))
        {
            if (_omittedScaleAxes && axis is "y" or "z") return new(_fallback, _fallback);
            if (axis == "brightness") return new(_fallback, _fallback);
            throw new VfxMappingException(_field, "Vfx.Preview.InvalidData");
        }
        var min = binding.Min.SingleValue ?? Evaluate(binding.Min, time);
        var max = ReferenceEquals(binding.Min, binding.Max) ? min : binding.Max.SingleValue ?? Evaluate(binding.Max, time);
        return new(min, max);
    }

    public float Sample(string axis, double time, float random) => _nativeScale?.Sample(axis, time, random) ?? Range(axis, time).Sample(random);
    public Vector3 Vector(double time, Vector3 random) => new(Sample("x", time, random.X), Sample("y", time, random.Y), Sample("z", time, random.Z));

    private float Evaluate(SampleChannel channel, double time)
    {
        var points = channel.Points;
        if (time <= points[0].Time) return (float)points[0].Value;
        for (var index = 1; index < points.Count; ++index)
        {
            var right = points[index];
            if (time > right.Time) continue;
            var left = points[index - 1];
            var fraction = (time - left.Time) / (right.Time - left.Time);
            if (left.OutMode is "step" or "constant") return (float)left.Value;
            if (left.OutMode == "linear" && right.InMode == "linear")
                return (float)(left.Value + (right.Value - left.Value) * fraction);
            if (IsAnalyticMode(left.OutMode) && IsAnalyticMode(right.InMode))
            {
                // Native controls blend both endpoint functions at the segment's normalized time.
                var weight = (1 - fraction) * Analytic(left.OutMode, fraction) + fraction * Analytic(right.InMode, fraction);
                return (float)(left.Value + (right.Value - left.Value) * weight);
            }
            if (IsMixedBezier(left.OutMode, right.InMode))
            {
                var cubic = BezierSegment(left, right, fraction);
                var delta = right.Value - left.Value;
                var outgoing = left.OutMode == "bezier_c" ? cubic : left.Value + delta * Analytic(left.OutMode, fraction);
                var incoming = right.InMode == "bezier_c" ? cubic : left.Value + delta * Analytic(right.InMode, fraction);
                return (float)((1 - fraction) * outgoing + fraction * incoming);
            }
            return (float)BezierSegment(left, right, fraction);
        }
        return (float)points[^1].Value;
    }

    private double BezierSegment(SamplePoint left, SamplePoint right, double fraction)
    {
        if (!left.HasHandles || !right.HasHandles)
            throw new VfxMappingException(_field, "Vfx.Preview.InvalidData");
        var outgoing = left.Outgoing; var incoming = right.Incoming;
        var duration = right.Time - left.Time;
        var x1 = Math.Clamp(outgoing.Time / duration, 0, 1);
        var x2 = Math.Clamp(1 + incoming.Time / duration, x1, 1);
        var y1 = left.Value + outgoing.Value;
        var y2 = right.Value + incoming.Value;
        // Solve Bezier x(t), rather than assuming evenly spaced tangent handles.
        var low = 0.0; var high = 1.0;
        for (var iteration = 0; iteration < 24; ++iteration)
        {
            var middle = (low + high) * 0.5;
            if (Bezier(0, x1, x2, 1, middle) < fraction) low = middle; else high = middle;
        }
        return Bezier(left.Value, y1, y2, right.Value, (low + high) * 0.5);
    }

    private sealed record SampleChannel(string Name, IReadOnlyList<SamplePoint> Points)
    {
        public float? SingleValue { get; } = Points.Count == 1 ? (float)Points[0].Value : null;
    }
    private readonly record struct SamplePoint(double Time, double Value, string InMode, string OutMode,
        (double Time, double Value) Incoming, (double Time, double Value) Outgoing, bool HasHandles);

    private static double Bezier(double p0, double p1, double p2, double p3, double t)
    {
        var u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }
    private static bool IsMode(string mode) => mode is "linear" or "bezier" or "bezier_c" or "bezier_u" or "spline" or "smooth" or "auto" or "flat" or "step" or "constant";
    private static bool IsMixedBezier(string outgoing, string incoming) => outgoing == "bezier_c" && incoming != "linear" && IsAnalyticMode(incoming)
        || incoming == "bezier_c" && outgoing != "linear" && IsAnalyticMode(outgoing);
    private static bool IsAnalyticMode(string mode) => mode is "linear" or "exp1" or "exp3" or "log1" or "log3" or "sine" or "sine_recip" or "s";
    private static double Analytic(string mode, double t) => mode switch
    {
        "linear" => t,
        "exp1" => (t * t + t) * .5,
        "exp3" => t * t * t,
        "log1" => (3 * t - t * t) * .5,
        "log3" => 1 - Math.Pow(1 - t, 3),
        "sine" => Math.Sin(t * Math.PI * .5),
        "sine_recip" => 1 - Math.Cos(t * Math.PI * .5),
        "s" => t * t * (3 - 2 * t),
        _ => throw new InvalidOperationException("Unvalidated analytic curve mode."),
    };
}

internal sealed record VfxAtlas(int Columns = 1, int Rows = 1, int Cell = 0, bool Random = false,
    bool Animated = false, bool Loop = true, VfxRange Loops = default, bool RowRestricted = false, int Row = 0,
    bool RandomRow = false, bool Blend = false, int OuterColumns = 1, int OuterRows = 1, int OuterCell = 0, bool FixedSpeed = false)
{
    public float FrameAtLife(double phase, float random) => FrameAt(phase * Frequency(phase), random);

    public float FrameAtSeconds(double phase, double sourceAge, float random) => FrameAt(sourceAge * Frequency(phase), random);

    public int RowAt(float random) => RandomRow ? (int)MathF.Floor((random - MathF.Floor(random)) * Rows)
        : RowRestricted ? Row : 0;

    private double Frequency(double phase) => Loops.Min + (Loops.Max - Loops.Min) * Math.Clamp(phase, 0, 1);

    private float FrameAt(double unary, float random)
    {
        var count = RowRestricted ? Columns : Columns * Rows;
        var frame = (unary + (Random ? random : 0)) * count;
        return (float)(Loop ? (frame % count + count) % count : Math.Clamp(frame, 0, count - 1));
    }
}

internal sealed record VfxPreviewReference(VfxRange Scale, Vector3 Position, Vector3 Rotation, VfxRange Start,
    float Speed, VfxRange Rate, VfxRange LifeBias, VfxRange DurationBias, Vector4 TintMin, Vector4 TintMax)
{
    public object InstanceKey { get; init; } = new();
}

internal readonly record struct VfxPreviewParticle(EffekseerParticle Render, int ModelVariant);

internal sealed record VfxPreviewParticleEvent(string Path, bool OnDeath, float Delay, bool TrackPosition,
    bool TrackScale, bool TrackColour, bool TrackAlpha, bool TrackLifetime, string Orientation)
{
    public IReadOnlyList<VfxPreviewEmitter> Children { get; set; } = [];
    public bool Resolved { get; set; }
}

internal sealed class VfxPreviewEmitter
{
    public required string Name { get; init; }
    public required EffekseerLayer Render { get; set; }
    public required VfxPreviewCurve Emission { get; init; }
    public required VfxPreviewCurve Life { get; init; }
    public required VfxPreviewCurve Scale { get; init; }
    public required VfxPreviewCurve InitialScale { get; init; }
    public required VfxPreviewCurve InitialAlpha { get; init; }
    public VfxSpriteNativeScaleCurve? SpriteScaleTable { get; init; }
    public VfxSpriteNativeScaleCurve? SpriteInitialAlphaTable { get; init; }
    public VfxSpriteNativeScaleCurve? SpriteAlphaTable { get; init; }
    public float InitialAlphaAt(double phase, float random)
        => SpriteInitialAlphaTable?.Sample("x", phase, random) ?? InitialAlpha.Sample("x", phase, random);
    public required VfxPreviewCurve Position { get; init; }
    public required VfxPreviewCurve Orientation { get; init; }
    public required VfxPreviewCurve Speed { get; init; }
    public required VfxPreviewCurve SpawnBox { get; init; }
    public required VfxPreviewCurve SpawnRadius { get; init; }
    public required VfxPreviewCurve Colour { get; init; }
    public required VfxPreviewCurve Alpha { get; init; }
    public VfxPreviewCurve? AlphaCrush { get; init; }
    public bool SharpAlphaCrush { get; init; }
    public Vector2 AlphaStartOffset { get; init; }
    public Vector2 AlphaEndOffset { get; init; }
    public float AlphaAt(double phase, float random)
    {
        var start = MathHelper.Lerp(AlphaStartOffset.X, AlphaStartOffset.Y, random);
        var end = MathHelper.Lerp(AlphaEndOffset.X, AlphaEndOffset.Y, random);
        return Alpha.Sample("alpha", Math.Clamp((phase - start) / (1 - start - end), 0, 1), random);
    }
    public required VfxPreviewCurve Force { get; init; }
    public required VfxPreviewCurve Deceleration { get; init; }
    public float CalibratedVerticalAcceleration { get; init; }
    public float? ConstantForceDamping { get; init; }
    public bool ParticleForceCurve { get; init; }
    public VfxRange Start { get; init; }
    public VfxRange Duration { get; init; } = new(1, 1);
    public bool HasExplicitDuration { get; init; }
    public bool Infinite { get; set; }
    public bool LoopEmission { get; init; }
    public bool InitialParameterLoop { get; init; }
    public float BurstInterval { get; init; }
    public float BurstLength { get; init; }
    public float SpawnSpacing { get; init; }
    public float SpawnInheritedSpeed { get; init; }
    public float SpawnInheritedDirectionBlend { get; init; }
    public int MaximumParticles { get; init; } = 8192;
    public int SimulationSpace { get; init; }
    public Vector3 OrientationMin { get; init; }
    public Vector3 OrientationMax { get; init; }
    public Vector3 SpinMin { get; init; }
    public Vector3 SpinMax { get; init; }
    public Vector3 SpinDirection { get; init; }
    public VfxPreviewCurve SpinDeceleration { get; init; } = new(null, "ParticleSpinDeceleration", 0);
    public float? SpriteConstantSpinDamping { get; init; }
    public VfxSpriteNativeSpinDamping? SpriteNativeSpinDamping { get; init; }
    public bool SpriteIndependentSpinRandom { get; init; }
    public bool DynamicSpinDamping { get; init; }
    public bool ComplexSpinDamping { get; init; }
    public bool ModelQuaternionSpin { get; init; }
    public VfxModelNativeCurve? ModelSpinDeceleration { get; init; }
    public VfxModelNativeOrientation? ModelEmitterOrientation { get; init; }
    public VfxModelNativeMotion? ModelEmitterMotion { get; init; }
    public bool ModelEmitterBirthLoop { get; init; }
    public bool ModelEmitterFollowLoop { get; set; }
    public bool RelativeOrientation { get; init; }
    public bool RequiresFlatTerrain { get; init; }
    public bool SpawnOnTerrain { get; init; }
    public int TerrainSpawnDirection { get; init; } = 2;
    public Vector4? ParticleCullDistances { get; init; }
    public VfxRange SpawnAngle { get; init; }
    public VfxRange SpawnSpread { get; init; }
    public bool ZeroSpawnArc { get; init; }
    public bool DefaultVerticalSpawn { get; init; }
    public bool LegacySpreadSpawn { get; init; }
    public bool RadialSpawnDirection { get; init; }
    public VfxRange? SpawnAreaArc { get; init; }
    public VfxRange SpawnAreaAngle { get; init; }
    public int Alignment { get; init; }
    public VfxPreviewCurve VelocityStretch { get; init; } = new(null, "velocity_stretch_mul", 0);
    public float VelocityStretchCap { get; init; } = 1000;
    public bool TrailMode { get; init; }
    public float MinimumPixelSize { get; init; }
    public bool MinimumPixelHalfResolution { get; init; }
    public bool HalfResolution { get; init; }
    public bool FullResolution { get; init; }
    public bool IsMotionTrail => !IsEventTemplate && (Kind is 0 or 1) && (SpawnSpacing > 0 || TrailMode);
    public bool PremultipliedAlpha { get; init; }
    public float AdditiveMultiplier { get; init; }
    public float CoveredColourMultiplier { get; init; } = 1;
    public Vector2 UvFlipProbability { get; init; }
    public VfxAtlas Atlas { get; init; } = new();
    public IReadOnlyList<VfxPreviewReference> References { get; set; } = [];
    public bool IsEventTemplate { get; set; }
    public bool IsEventHelper { get; init; }
    public IReadOnlyList<VfxPreviewParticleEvent> Events { get; init; } = [];
    public IReadOnlyList<EffekseerLayer> AdditionalModelParts { get; set; } = [];
    public IReadOnlyList<IReadOnlyList<EffekseerLayer>> ModelVariants { get; set; } = [];
    public IReadOnlyList<EffekseerLayer> RenderParts(int variant) => IsEventHelper ? [] : variant == 0 ? [Render, .. AdditionalModelParts] : ModelVariants[variant - 1];
    public int RenderLayerCount => (IsEventHelper ? 0 : 1 + AdditionalModelParts.Count + ModelVariants.Sum(variant => variant.Count))
        * (PremultipliedAlpha && AdditiveMultiplier > 0 ? 2 : 1);
    public Vector2? MeshLoop { get; init; }
    public int Kind => Render.Kind;
    public float Distortion => Render.Distortion;
}
