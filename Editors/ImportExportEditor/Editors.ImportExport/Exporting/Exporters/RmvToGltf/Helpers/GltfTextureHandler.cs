using System.IO;
using System.Security.Cryptography;
using System.Text;
using Editors.ImportExport.Exporting.Exporters.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Exporters.DdsToNormalPng;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using SharpGLTF.Materials;

namespace Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;

public record TextureResult(int MeshIndex, string SystemFilePath, KnownChannel GlftTexureType);

public interface IGltfTextureHandler
{
    List<TextureResult> HandleTextures(RmvFile rmvFile, RmvToGltfExporterSettings settings);
}

public class GltfTextureHandler : IGltfTextureHandler
{
    private readonly IDdsToNormalPngExporter _ddsToNormalPngExporter;
    private readonly IDdsToMaterialPngExporter _ddsToMaterialPngExporter;
    private readonly IPackFileService _packFileService;

    public GltfTextureHandler(
        IDdsToNormalPngExporter ddsToNormalPngExporter,
        IDdsToMaterialPngExporter ddsToMaterialPngExporter,
        IPackFileService packFileService)
    {
        _ddsToNormalPngExporter = ddsToNormalPngExporter;
        _ddsToMaterialPngExporter = ddsToMaterialPngExporter;
        _packFileService = packFileService;
    }

    public List<TextureResult> HandleTextures(RmvFile rmvFile, RmvToGltfExporterSettings settings)
    {
        var output = new List<TextureResult>();
        if (!settings.ExportMaterials)
            return output;

        var exportedTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outputFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>>? texturePathsByFileName = null;
        var lodLevel = rmvFile.ModelList.First();

        for (var meshIndex = 0; meshIndex < lodLevel.Length; meshIndex++)
        {
            foreach (var texture in ExtractTextures(lodLevel[meshIndex])
                         .Where(texture => IsTextureSupported(settings.SelectedGame, texture.Type)))
            {
                var channel = texture.Type switch
                {
                    TextureType.Normal => KnownChannel.Normal,
                    TextureType.MaterialMap => KnownChannel.MetallicRoughness,
                    TextureType.BaseColour => KnownChannel.BaseColor,
                    TextureType.Diffuse => KnownChannel.BaseColor,
                    TextureType.Specular => KnownChannel.SpecularColor,
                    TextureType.Gloss => KnownChannel.MetallicRoughness,
                    _ => (KnownChannel?)null,
                };
                if (channel == null)
                    continue;

                if (!exportedTextures.TryGetValue(texture.Path, out var systemPath))
                {
                    var outputFileName = GetOutputFileName(texture.Path, outputFileNames);
                    systemPath = ExportTexture(texture, settings, outputFileName);
                    if (string.IsNullOrWhiteSpace(systemPath))
                    {
                        texturePathsByFileName ??= BuildTexturePathIndex();
                        var fallbackPath = FindUniqueTexturePath(
                            texture.Path,
                            texturePathsByFileName);
                        if (fallbackPath != null)
                        {
                            systemPath = ExportTexture(
                                texture with { Path = fallbackPath },
                                settings,
                                outputFileName);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(systemPath))
                        throw new FileNotFoundException($"找不到模型引用的纹理“{texture.Path}”。");

                    exportedTextures[texture.Path] = systemPath;
                }

                output.Add(new TextureResult(meshIndex, systemPath, channel.Value));
            }
        }

        return output;
    }

    private Dictionary<string, List<string>> BuildTexturePathIndex()
    {
        var output = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, _) in PackFileServiceUtility
                     .FindAllWithExtentionIncludePaths(_packFileService, ".dds"))
        {
            var fileName = Path.GetFileName(path);
            if (!output.TryGetValue(fileName, out var paths))
            {
                paths = [];
                output[fileName] = paths;
            }

            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        return output;
    }

    private static string? FindUniqueTexturePath(
        string referencedPath,
        Dictionary<string, List<string>> texturePathsByFileName)
    {
        var fileName = Path.GetFileName(referencedPath);
        return texturePathsByFileName.TryGetValue(fileName, out var paths) &&
               paths.Count == 1
            ? paths[0]
            : null;
    }

    private string ExportTexture(
        MaterialBuilderTextureInput texture,
        RmvToGltfExporterSettings settings,
        string outputFileName)
    {
        return texture.Type switch
        {
            TextureType.Normal => _ddsToNormalPngExporter.Export(
                texture.Path,
                settings.OutputPath,
                settings.ConvertNormalTextureToBlue,
                outputFileName),
            TextureType.MaterialMap => _ddsToMaterialPngExporter.Export(
                texture.Path,
                settings.OutputPath,
                settings.ConvertMaterialTextureToBlender,
                outputFileName),
            _ => _ddsToMaterialPngExporter.Export(
                texture.Path,
                settings.OutputPath,
                false,
                outputFileName),
        };
    }

    private static List<MaterialBuilderTextureInput> ExtractTextures(RmvModel model) =>
        model.Material
            .GetAllTextures()
            .Select(texture => new MaterialBuilderTextureInput(texture.Path, texture.TexureType))
            .ToList();

    private static bool IsTextureSupported(GameTypeEnum game, TextureType textureType)
    {
        if (game is GameTypeEnum.Warhammer3 or GameTypeEnum.ThreeKingdoms)
        {
            return textureType is TextureType.BaseColour or
                TextureType.MaterialMap or
                TextureType.Normal;
        }

        if (game is GameTypeEnum.Warhammer or
            GameTypeEnum.Warhammer2 or
            GameTypeEnum.Troy or
            GameTypeEnum.Pharaoh)
        {
            return textureType is TextureType.Diffuse or
                TextureType.Specular or
                TextureType.Gloss or
                TextureType.Normal;
        }

        return true;
    }

    private static string GetOutputFileName(
        string packPath,
        Dictionary<string, string> outputFileNames)
    {
        var baseFileName = Path.GetFileNameWithoutExtension(packPath);
        var outputFileName = baseFileName + ".png";
        if (!outputFileNames.TryGetValue(outputFileName, out var existingPackPath))
        {
            outputFileNames[outputFileName] = packPath;
            return outputFileName;
        }

        if (string.Equals(existingPackPath, packPath, StringComparison.OrdinalIgnoreCase))
            return outputFileName;

        var pathHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(packPath.ToLowerInvariant())))[..8];
        outputFileName = $"{baseFileName}_{pathHash}.png";
        outputFileNames[outputFileName] = packPath;
        return outputFileName;
    }

    private sealed record MaterialBuilderTextureInput(string Path, TextureType Type);
}
