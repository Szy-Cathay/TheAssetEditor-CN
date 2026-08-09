using System.IO;
using System.Text.Json;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

internal static class GltfSkeletonNameReader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;

    public static string GetDefaultName(string inputFile)
    {
        var fallback = Path.GetFileNameWithoutExtension(inputFile);
        try
        {
            using var document = LoadJsonDocument(inputFile);
            if (!document.RootElement.TryGetProperty("skins", out var skins) ||
                skins.GetArrayLength() == 0)
            {
                return fallback;
            }

            var skin = skins[0];
            if (skin.TryGetProperty("name", out var skinName) &&
                !string.IsNullOrWhiteSpace(skinName.GetString()))
            {
                return skinName.GetString()!;
            }

            if (skin.TryGetProperty("skeleton", out var skeletonIndex) &&
                skeletonIndex.TryGetInt32(out var nodeIndex) &&
                document.RootElement.TryGetProperty("nodes", out var nodes) &&
                nodeIndex >= 0 &&
                nodeIndex < nodes.GetArrayLength() &&
                nodes[nodeIndex].TryGetProperty("name", out var nodeName) &&
                !string.IsNullOrWhiteSpace(nodeName.GetString()))
            {
                return nodeName.GetString()!;
            }
        }
        catch (Exception)
        {
            return fallback;
        }

        return fallback;
    }

    private static JsonDocument LoadJsonDocument(string inputFile)
    {
        if (Path.GetExtension(inputFile).Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(inputFile);
            return JsonDocument.Parse(stream);
        }

        using var reader = new BinaryReader(File.OpenRead(inputFile));
        if (reader.ReadUInt32() != GlbMagic || reader.ReadUInt32() != 2)
            throw new InvalidDataException("不是有效的 glTF 2.0 GLB 文件。");

        reader.ReadUInt32();
        var chunkLength = reader.ReadUInt32();
        var chunkType = reader.ReadUInt32();
        if (chunkType != JsonChunkType || chunkLength > int.MaxValue)
            throw new InvalidDataException("GLB 缺少有效的 JSON 数据块。");

        var json = reader.ReadBytes((int)chunkLength);
        if (json.Length != chunkLength)
            throw new EndOfStreamException("GLB JSON 数据块不完整。");

        return JsonDocument.Parse(json);
    }
}
