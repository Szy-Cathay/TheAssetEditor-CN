using Microsoft.Xna.Framework;

namespace GameWorld.Core.Components.Selection;

internal static class EditOverlayVisibility
{
    private const float HiddenPixelsPerPrimitive = 1.0f;
    private const float FullPixelsPerPrimitive = 12.0f;

    public static float CalculateDetailOpacity(
        BoundingBox localBounds,
        Matrix world,
        Matrix view,
        Matrix projection,
        int viewportWidth,
        int viewportHeight,
        int primitiveCount)
    {
        if (viewportWidth <= 0 ||
            viewportHeight <= 0 ||
            primitiveCount <= 0)
        {
            return 0.0f;
        }

        var transform = world * view * projection;
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var projectedCornerCount = 0;
        var clippedCornerCount = 0;

        foreach (var corner in localBounds.GetCorners())
        {
            var clip = Vector4.Transform(
                new Vector4(corner, 1.0f),
                transform);
            if (clip.W <= 0.0001f)
            {
                clippedCornerCount++;
                continue;
            }

            var ndcX = clip.X / clip.W;
            var ndcY = clip.Y / clip.W;
            var pixelX = (ndcX * 0.5f + 0.5f) * viewportWidth;
            var pixelY = (1.0f - (ndcY * 0.5f + 0.5f)) * viewportHeight;
            minX = MathF.Min(minX, pixelX);
            minY = MathF.Min(minY, pixelY);
            maxX = MathF.Max(maxX, pixelX);
            maxY = MathF.Max(maxY, pixelY);
            projectedCornerCount++;
        }

        if (projectedCornerCount == 0)
            return 0.0f;

        if (clippedCornerCount != 0)
            return 1.0f;

        if (primitiveCount <= 4)
            return 1.0f;

        var width = Math.Clamp(maxX - minX, 0.0f, viewportWidth);
        var height = Math.Clamp(maxY - minY, 0.0f, viewportHeight);
        var projectedArea = width * height;
        var pixelsPerPrimitive = projectedArea / primitiveCount;
        var opacity = Math.Clamp(
            (pixelsPerPrimitive - HiddenPixelsPerPrimitive) /
                (FullPixelsPerPrimitive - HiddenPixelsPerPrimitive),
            0.0f,
            1.0f);

        return opacity * opacity * (3.0f - 2.0f * opacity);
    }
}
