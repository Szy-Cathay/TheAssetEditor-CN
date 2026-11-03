using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// Rigid force tables add A, B and C before unit conversion and particle filtering.
internal sealed class VfxModelNativeForce
{
    private readonly Axis[] _axes;

    private VfxModelNativeForce(Axis[] axes) => _axes = axes;

    public static VfxModelNativeForce Create(string? forceA, string? forceB = null, string? forceC = null)
    {
        string?[] sources = [forceA, forceB, forceC];
        return new(new[] { "x", "y", "z" }.Select(axis => new Axis(sources.Select((text, index) => text == null ? null
            : VfxModelNativeCurve.Create(text, "Force" + (char)('A' + index), axis)).ToArray())).ToArray());
    }

    public Vector3 Sample(double phase, float unary)
        => new(_axes[0].Sample(phase, unary), _axes[1].Sample(phase, unary), _axes[2].Sample(phase, unary));

    private sealed class Axis
    {
        private readonly float[] _minimum;
        private readonly float[] _maximum;

        public Axis(VfxModelNativeCurve?[] curves)
        {
            var count = curves.Max(curve => curve?.SampleCount ?? 1);
            _minimum = new float[count]; _maximum = new float[count];
            for (var index = 0; index < count; index++)
            {
                float Bound(int force, bool maximum) => curves[force] is { } curve
                    ? curve.SampleBoundAt(curve.SampleCount == 1 ? 0 : index, maximum) : 0;
                var minimum = Bound(0, false); var maximum = Bound(0, true);
                // Native 71D8E0 performs float additions in this order before 5856F0 scales the sum.
                minimum += Bound(1, false); maximum += Bound(1, true);
                minimum += Bound(2, false); maximum += Bound(2, true);
                _minimum[index] = minimum * .01f;
                _maximum[index] = maximum * .01f;
            }
        }

        public float Sample(double phase, float unary)
        {
            var position = Math.Clamp((float)phase, 0, 1) * (_minimum.Length - 1);
            var lower = (int)position; var upper = Math.Min(lower + 1, _minimum.Length - 1);
            var fraction = position - lower;
            var minimum = _minimum[lower] + (_minimum[upper] - _minimum[lower]) * fraction;
            var maximum = _maximum[lower] + (_maximum[upper] - _maximum[lower]) * fraction;
            return minimum + (maximum - minimum) * unary;
        }
    }
}
