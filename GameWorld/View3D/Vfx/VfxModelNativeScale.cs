using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed class VfxModelNativeScale
{
    private readonly VfxModelNativeCurve _x;
    private readonly VfxModelNativeCurve _y;
    private readonly VfxModelNativeCurve _z;

    private VfxModelNativeScale(VfxModelNativeCurve x, VfxModelNativeCurve y, VfxModelNativeCurve z)
        => (_x, _y, _z) = (x, y, z);

    public static VfxModelNativeScale? Create(string text)
    {
        var x = VfxModelNativeCurve.Create(text, "ParticleScale", "x");
        var y = VfxModelNativeCurve.Create(text, "ParticleScale", "y");
        var z = VfxModelNativeCurve.Create(text, "ParticleScale", "z");
        // Leave the existing constant path bit-identical, including its default axis values.
        return x.SampleCount == 1 && y.SampleCount == 1 && z.SampleCount == 1 ? null : new(x, y, z);
    }

    public Vector3 Sample(double phase, float unary)
        => new(_x.Sample(phase, unary), _y.Sample(phase, unary), _z.Sample(phase, unary));
}
