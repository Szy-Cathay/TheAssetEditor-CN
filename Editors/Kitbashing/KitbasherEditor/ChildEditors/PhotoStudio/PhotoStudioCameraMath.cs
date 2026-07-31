using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;

namespace Editors.KitbasherEditor.ChildEditors.PhotoStudio
{
    internal static class PhotoStudioCameraMath
    {
        public static bool TryCalculateOrbit(
            Vector3 position,
            Vector3 lookAt,
            float fallbackYaw,
            float minPitch,
            float maxPitch,
            out float yaw,
            out float pitch,
            out float zoom)
        {
            yaw = 0;
            pitch = 0;
            zoom = 0;

            if (!IsFinite(position) || !IsFinite(lookAt))
                return false;

            var offset = position - lookAt;
            zoom = offset.Length();
            if (!float.IsFinite(zoom) ||
                zoom + 0.000001f <
                    ArcBallCamera.MinZoom)
            {
                return false;
            }

            var horizontalLength = MathF.Sqrt(
                offset.X * offset.X +
                offset.Z * offset.Z);
            yaw = horizontalLength < 0.000001f
                ? fallbackYaw
                : MathF.Atan2(offset.X, offset.Z);
            if (!float.IsFinite(yaw))
                yaw = 0;

            pitch = MathHelper.Clamp(
                MathF.Atan2(-offset.Y, horizontalLength),
                minPitch,
                maxPitch);
            return float.IsFinite(pitch);
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z);
        }
    }
}
