using System.Xml.Linq;
using System.IO;
using GameWorld.Core.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxSolidMeshMaterial
{
    // Matches the shipped 196-byte AUTO_PARAMETERS buffer; seven floats tag/pad this layout.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("colour_overlay_pan", 2), ("colour_overlay_strength", 1), ("colour_overlay_tile", 2),
        ("depth_fade_switch", 1), ("global_mask_pan", 2), ("global_mask_tiling", 2),
        ("glow_colour", 3), ("glow_strength", 1), ("normal_pan", 2), ("normal_tiling", 2),
        ("pixel_depth_fade", 1), ("texture_a_brightness", 1), ("texture_a_channels", 4),
        ("texture_a_pan", 2), ("texture_a_tiling", 2), ("texture_b_channels", 4),
        ("texture_b_pan", 2), ("texture_b_tiling", 2), ("texture_c_brightness", 1),
        ("texture_c_channels", 4), ("texture_c_pan", 2), ("texture_c_tiling", 2),
        ("time", 1), ("total_alpha_strength", 1), ("vertex_alpha_contrast", 1), ("vertex_colour", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root, Func<string, byte[]>? resolve)
    {
        string Texture(string slot)
        {
            var nodes = root.Elements("textures").Elements("texture").Where(t =>
                string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }
        // The bounded intrinsic-colour branch has constant dither/material maps, not inferred defaults.
        Constant(Texture("t_xml_dither_texture"), "commontextures/default_white.dds", 255);
        Constant(Texture("t_xml_material_map"), "commontextures/default_black.dds", 0);
        var paths = new[] { "t_xml_texture_a", "t_xml_texture_b", "t_xml_texture_c", "t_xml_colour_overlay", "t_xml_global_mask", "t_xml_normal" }
            .Select(Texture).ToArray();
        var values = new List<float>(56);
        foreach (var (name, components) in Fields)
        {
            var nodes = root.Elements("params").Elements("param").Where(p =>
                string.Equals(p.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var parts = (nodes[0].Element("value")?.Value ?? "").Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (!VfxCurve.TryNumber(part, out var number) || Math.Abs(number) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                values.Add((float)number);
            }
        }
        if (values[45] != 1) throw new VfxMappingException("time", "Vfx.Preview.MaterialUnsupported");
        if (values[5] is < 0 or > 1 || values[18] <= 0 || values[19] < 0 || values[36] < 0
            || values[46] < 0 || values[47] is <= 0 or > 16)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        while (values.Count < 56) values.Add(0);
        return new(paths, values.ToArray());

        void Constant(string path, string expectedPath, byte expected)
        {
            if (!string.Equals(path.Replace('\\', '/'), expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new VfxMappingException(path, "Vfx.Preview.MaterialUnsupported");
            if (resolve == null) throw new VfxMappingException(path, "Vfx.Preview.MissingResource");
            var bytes = resolve(path);
            Pfim.IImage decoded;
            try { decoded = ImageLoader.LoadImageFromBytes(bytes, out _); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or EndOfStreamException)
            { throw new VfxMappingException(path, "Vfx.Preview.MaterialUnsupported"); }
            using var image = decoded;
            if (image.Format != Pfim.ImageFormat.Rgba32 || image.Width is < 1 or > 4096 || image.Height is < 1 or > 4096
                || image.Stride < image.Width * 4 || (long)image.Stride * image.Height > image.DataLen)
                throw new VfxMappingException(path, "Vfx.Preview.MaterialUnsupported");
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
            {
                var offset = y * image.Stride + x * 4;
                if (image.Data[offset] != expected || image.Data[offset + 1] != expected || image.Data[offset + 2] != expected)
                    throw new VfxMappingException(path, "Vfx.Preview.MaterialUnsupported");
            }
        }
    }
}
