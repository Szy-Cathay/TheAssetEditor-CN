namespace GameWorld.Core.Vfx;

internal sealed record VfxPreviewGradient(byte[] Pixels, float ColourScale, bool MapAlpha)
{
    public const int Width = 256;

    public static VfxPreviewGradient Bake(VfxPreviewCurve colour, VfxPreviewCurve alpha, bool mapAlpha)
    {
        // ps_gradient looks up the filtered texture red/alpha, independently of particle age.
        var samples = Enumerable.Range(0, Width).Select(index =>
        {
            var t = (double)index / (Width - 1);
            var brightness = colour.Sample("brightness", t, 1);
            return new[] { colour.Sample("r", t, 1) * brightness, colour.Sample("g", t, 1) * brightness,
                colour.Sample("b", t, 1) * brightness, Math.Clamp(alpha.Sample("alpha", t, 1), 0, 1) };
        }).ToArray();
        var gain = Math.Max(1, samples.Max(sample => sample.Take(3).Max()));
        if (!float.IsFinite(gain) || gain > 1000000 || samples.Any(sample => sample.Any(value => !float.IsFinite(value) || value < 0)))
            throw new VfxMappingException("ps_gradient", "Vfx.Preview.RangeUnsupported");
        var pixels = new byte[Width * 4];
        for (var index = 0; index < samples.Length; index++)
            for (var channel = 0; channel < 4; channel++)
                pixels[index * 4 + channel] = (byte)Math.Clamp(MathF.Round(samples[index][channel] / (channel == 3 ? 1 : gain) * 255), 0, 255);
        return new(pixels, gain, mapAlpha);
    }
}
