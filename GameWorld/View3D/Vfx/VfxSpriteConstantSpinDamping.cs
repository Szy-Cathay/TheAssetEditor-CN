using System.Globalization;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

// Ordinary GPU damping rows use the shipped signed half converter, not model CPU float tables.
internal static class VfxSpriteConstantSpinDamping
{
    private const string Field = "ParticleSpinDeceleration";

    public static float Create(string text)
    {
        if (!VfxCurve.TryParse(text, out var curve)) throw new VfxMappingException(Field, "Vfx.Preview.InvalidData");
        var channels = curve!.Channels;
        // The GPU writer reads raw channels 0/1. Missing channels and link remapping are not inferred.
        if (channels.Count != 2 || channels[0].Name != "x_min" || channels[1].Name != "x_max"
            || channels.Any(channel => channel.Points.Count != 1)
            || (float)channels[0].Points[0].Value != (float)channels[1].Points[0].Value)
            throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var mapping = text.Split(':')[1].Split(';');
        var header = mapping[0].Split(',');
        var indexes = mapping.Length == 2 ? mapping[1].Split(',') : [];
        if (header.Length != 2 || header[0] != "x"
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var split) || split is < 0 or > 1
            || indexes.Length != 2 || !int.TryParse(indexes[0], out var lower) || lower != 0
            || !int.TryParse(indexes[1], out var upper) || upper != 1)
            throw new VfxMappingException(Field, "Vfx.Preview.AmbiguousField");
        var value = (float)BitConverter.UInt16BitsToHalf(HalfBits((float)channels[0].Points[0].Value));
        if (!float.IsFinite(value)) throw new VfxMappingException(Field, "Vfx.Preview.RangeUnsupported");
        return value;
    }

    internal static ushort HalfBits(float value)
    {
        // Official 804e40 rounds ties toward zero, including the overflow boundary.
        var bits = BitConverter.SingleToUInt32Bits(value);
        var sign = (bits >> 16) & 0x8000u;
        var exponent = (bits >> 23) & 0xffu;
        var magnitude = bits & 0x7fffffffu;
        if (exponent > 142) return (ushort)(sign | 0x7c00u);
        if (exponent > 112)
        {
            magnitude += ((magnitude - 1u) & magnitude) & 0x1fffu;
            return (ushort)(sign | (((magnitude >> 23) - 112u) << 10) | ((magnitude >> 13) & 0x3ffu));
        }
        if (exponent > 102)
        {
            var fraction = magnitude & 0x7fffffu;
            var shift = (int)exponent - 103;
            if (((fraction << shift) & 0x3fffffu) > 0x200u)
            {
                var remaining = fraction & (0x7fffffu >> shift);
                fraction += (remaining - 1u) & remaining;
            }
            return (ushort)(sign | ((fraction + 0x800000u) >> (126 - (int)exponent)));
        }
        return (ushort)(sign | (magnitude > 0x33000400u ? 1u : 0u));
    }
}
