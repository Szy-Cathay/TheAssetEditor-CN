using System;
using System.IO;
using System.Linq;

namespace Editors.Audio.Shared.AudioProject
{
    public static class AudioProjectNameValidator
    {
        public static bool TryNormalize(string value, out string normalizedName)
        {
            normalizedName = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                normalizedName = null;
                return false;
            }

            if (normalizedName.EndsWith(".aproj", StringComparison.OrdinalIgnoreCase))
                normalizedName = normalizedName[..^".aproj".Length].Trim();

            if (!IsSafeFileNameSegment(normalizedName))
            {
                normalizedName = null;
                return false;
            }

            return true;
        }

        public static bool IsSafeFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Any(char.IsWhiteSpace) ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.EndsWith(".", StringComparison.Ordinal))
            {
                return false;
            }

            var extensionIndex = value.IndexOf('.');
            var deviceName = extensionIndex >= 0
                ? value[..extensionIndex]
                : value;
            if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return deviceName.Length != 4 ||
                (!deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                 !deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) ||
                deviceName[3] is < '1' or > '9';
        }
    }
}
