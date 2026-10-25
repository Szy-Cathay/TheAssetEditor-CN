namespace GameWorld.Core.Vfx;

internal sealed record VfxMaterialMaskAtlas(byte[] Pixels, int Width, int Height, float[] Rectangles)
{
    public static VfxMaterialMaskAtlas PackThree((byte[] Pixels, int Width, int Height) first,
        (byte[] Pixels, int Width, int Height) second, (byte[] Pixels, int Width, int Height) third)
    {
        var pair = Pack(first.Pixels, first.Width, first.Height, second.Pixels, second.Width, second.Height);
        var atlas = Pack(pair.Pixels, pair.Width, pair.Height, third.Pixels, third.Width, third.Height);
        var rectangles = new float[12];
        for (var index = 0; index < 2; ++index)
        {
            rectangles[index * 4] = atlas.Rectangles[0] + pair.Rectangles[index * 4] * atlas.Rectangles[2];
            rectangles[index * 4 + 1] = atlas.Rectangles[1] + pair.Rectangles[index * 4 + 1] * atlas.Rectangles[3];
            rectangles[index * 4 + 2] = pair.Rectangles[index * 4 + 2] * atlas.Rectangles[2];
            rectangles[index * 4 + 3] = pair.Rectangles[index * 4 + 3] * atlas.Rectangles[3];
        }
        atlas.Rectangles.AsSpan(4, 4).CopyTo(rectangles.AsSpan(8));
        return atlas with { Rectangles = rectangles };
    }

    public static VfxMaterialMaskAtlas Pack(byte[] first, int firstWidth, int firstHeight, byte[] second, int secondWidth, int secondHeight)
    {
        Validate(first, firstWidth, firstHeight); Validate(second, secondWidth, secondHeight);
        var layouts = new[]
        {
            (Width: firstWidth + secondWidth + 4, Height: Math.Max(firstHeight, secondHeight) + 2, X: firstWidth + 2, Y: 0),
            (Width: Math.Max(firstWidth, secondWidth) + 2, Height: firstHeight + secondHeight + 4, X: 0, Y: firstHeight + 2),
        };
        var layout = layouts.Where(x => x.Width <= 4096 && x.Height <= 4096).OrderBy(x => x.Width * x.Height).FirstOrDefault();
        if (layout.Width == 0) throw new NotSupportedException("VFX masks exceed the packed texture budget");
        var pixels = new byte[checked(layout.Width * layout.Height * 4)];
        Copy(first, firstWidth, firstHeight, 0, 0); Copy(second, secondWidth, secondHeight, layout.X, layout.Y);
        return new(pixels, layout.Width, layout.Height,
        [
            1f / layout.Width, 1f / layout.Height, (float)firstWidth / layout.Width, (float)firstHeight / layout.Height,
            (layout.X + 1f) / layout.Width, (layout.Y + 1f) / layout.Height, (float)secondWidth / layout.Width, (float)secondHeight / layout.Height,
        ]);

        void Copy(byte[] source, int width, int height, int offsetX, int offsetY)
        {
            // A wrapped one-texel gutter preserves bilinear repeat filtering at every tile edge.
            for (var y = -1; y <= height; ++y)
                for (var x = -1; x <= width; ++x)
                    source.AsSpan((((y + height) % height) * width + (x + width) % width) * 4, 4)
                        .CopyTo(pixels.AsSpan(((offsetY + y + 1) * layout.Width + offsetX + x + 1) * 4, 4));
        }
    }

    private static void Validate(byte[] pixels, int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096 || (long)width * height * 4 != pixels.Length)
            throw new NotSupportedException("Invalid VFX mask layout");
    }
}
