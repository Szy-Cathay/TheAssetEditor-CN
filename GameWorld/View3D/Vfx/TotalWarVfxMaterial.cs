namespace GameWorld.Core.Vfx;

internal static class TotalWarVfxMaterial
{
    public static void ConvertDxt5Normal(byte[] rgba)
    {
        // DXT5n stores X in alpha and Y in green; alpha is not distortion opacity.
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = rgba[offset + 3];
            rgba[offset + 2] = 0;
            rgba[offset + 3] = 255;
        }
    }
}
