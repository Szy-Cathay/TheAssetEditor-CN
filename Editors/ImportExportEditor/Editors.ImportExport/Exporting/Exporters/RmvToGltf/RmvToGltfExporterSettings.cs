using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Exporting.Exporters.RmvToGltf
{
    public record RmvToGltfExporterSettings(

        PackFile InputModelFile,
        List<PackFile> InputAnimationFiles,
        string OutputPath,
        bool ExportMaterials, 
        bool ConvertMaterialTextureToBlender,
        bool ConvertNormalTextureToBlue,
        bool ExportAnimations,
        bool MirrorMesh,
        global::Shared.Core.Settings.GameTypeEnum SelectedGame = global::Shared.Core.Settings.GameTypeEnum.Unknown,
        bool ExportSkeleton = true
    );
}
