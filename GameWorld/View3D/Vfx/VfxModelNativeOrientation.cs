using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// Rigid follow uses separately baked min/max quaternions, not interpolated Euler angles.
internal sealed class VfxModelNativeOrientation
{
    private readonly Quaternion[] _minimum;
    private readonly Quaternion[] _maximum;

    private VfxModelNativeOrientation(Quaternion[] minimum, Quaternion[] maximum)
    {
        _minimum = minimum;
        _maximum = maximum;
    }

    public static VfxModelNativeOrientation Create(string text, string field = "EmitterOrientation")
    {
        var x = VfxModelNativeCurve.Create(text, field, "x");
        var y = VfxModelNativeCurve.Create(text, field, "y");
        var z = VfxModelNativeCurve.Create(text, field, "z");
        var count = Math.Max(x.SampleCount, Math.Max(y.SampleCount, z.SampleCount));
        var minimum = new Quaternion[count]; var maximum = new Quaternion[count];
        for (var index = 0; index < count; ++index)
        {
            float Value(VfxModelNativeCurve curve, bool upper) => curve.SampleBoundAt(curve.SampleCount == 1 ? 0 : index, upper);
            minimum[index] = Euler(Value(x, false), Value(y, false), Value(z, false));
            maximum[index] = Euler(Value(x, true), Value(y, true), Value(z, true));
        }
        return new(minimum, maximum);
    }

    public Quaternion Sample(double phase, float unary)
    {
        var position = Math.Clamp((float)phase, 0, 1) * (_minimum.Length - 1);
        var index = (int)position; var next = Math.Min(index + 1, _minimum.Length - 1);
        var fraction = position - index;
        var minimum = Lerp(_minimum[index], _minimum[next], fraction);
        var maximum = Lerp(_maximum[index], _maximum[next], fraction);
        var q = Lerp(minimum, maximum, unary);
        // Native birth and rigid follow normalize after both component interpolations.
        var length = MathF.Sqrt(q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W);
        return new(q.X / length, q.Y / length, q.Z / length, q.W / length);
    }

    private static Quaternion Lerp(Quaternion first, Quaternion second, float amount) => new(
        first.X + (second.X - first.X) * amount, first.Y + (second.Y - first.Y) * amount,
        first.Z + (second.Z - first.Z) * amount, first.W + (second.W - first.W) * amount);

    private static Quaternion Euler(float x, float y, float z)
    {
        var radians = MathHelper.Pi / 180 * .5f;
        var sx = MathF.Sin(x * radians); var cx = MathF.Cos(x * radians);
        var sy = MathF.Sin(y * radians); var cy = MathF.Cos(y * radians);
        var sz = MathF.Sin(z * radians); var cz = MathF.Cos(z * radians);
        return new(sx*cy*cz - cx*sy*sz, cx*sy*cz + sx*cy*sz,
            cx*cy*sz - sx*sy*cz, cx*cy*cz + sx*sy*sz);
    }
}
