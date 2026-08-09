using System.IO;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Misc;
using Editors.Shared.Core.Services;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Octokit;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using SharpGLTF.Schema2;


namespace Editors.ImportExport.Importing.Importers.GltfToRmv
{
    public class GltfImporter
    {
        private const string Value = "//skeleton//";
        private readonly IPackFileService _packFileService;
        private readonly ILogger _logger = Logging.Create<GltfImporter>();
        private readonly ISkeletonAnimationLookUpHelper _skeletonLookUpHelper;
        private readonly RmvMaterialBuilder _materialBuilder;

        public GltfImporter(IPackFileService packFileSerivce, ISkeletonAnimationLookUpHelper skeletonLookUpHelper, RmvMaterialBuilder materialBuilder)
        {
            _packFileService = packFileSerivce;
            _skeletonLookUpHelper = skeletonLookUpHelper;
            _materialBuilder = materialBuilder;
        }

        public ImportSupportEnum CanImportFile(PackFile file)
        {
            if (FileExtensionHelper.IsGltfFile(file.Name))
                return ImportSupportEnum.HighPriority;

            return ImportSupportEnum.NotSupported;
        }

        private RmvFile? ImportMeshes(GltfImporterSettings settings, ModelRoot modelRoot, AnimationFile? skeletonAnimFile, string skeletonName)
        {
            var importedFileName = GetImportedPackFileName(settings);

            var rmv2File = RmvMeshBuilder.Build(settings, modelRoot, skeletonAnimFile, skeletonName);
            if (rmv2File == null)
                return null;

            return rmv2File;
        }

        private void SaveRmvFileToPack(GltfImporterSettings settings, string importedFileName, RmvFile rmv2File)
        {
            var bytesRmv2 = ModelFactory.Create().Save(rmv2File);

            var packFileImported = new PackFile(importedFileName, new MemorySource(bytesRmv2));
            var newFile = new NewPackFileEntry(settings.DestinationPackPath, packFileImported);

            PackFileDispatcherWriter.AddFilesToPack(
                _packFileService,
                settings.DestinationPackFileContainer,
                [newFile]);
        }

        private void ImportMaterials(GltfImporterSettings settings, ModelRoot modelRoot, RmvFile rmv2File)
        {
            _materialBuilder.BuildRmvFileMaterials(settings, modelRoot, rmv2File);
        }

        private void ImportAnimations(GltfImporterSettings settings, ModelRoot modelRoot, AnimationFile? skeletonAnimFile, string skeletonName)
        {
            if (modelRoot.LogicalAnimations.Count == 0)
                return;
            if (skeletonAnimFile == null)
                throw new InvalidDataException("glTF 包含动画，但未能确定对应的游戏骨架，已停止导入以避免生成损坏的 ANIM 文件。");

            var baseFileName = Path.GetFileNameWithoutExtension(settings.InputGltfFile);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var animationIndex = 0; animationIndex < modelRoot.LogicalAnimations.Count; animationIndex++)
            {
                var animation = modelRoot.LogicalAnimations[animationIndex];
                var animFile = AnimationBuilder.Build(
                    new AnimationBuilderSettings(
                        modelRoot,
                        skeletonName,
                        settings.AnimationKeysPerSecond,
                        settings.DestinationPackFileContainer,
                        settings.DestinationPackPath,
                        settings.AutoDetectAnimationKeysPerSecond),
                    skeletonAnimFile,
                    animation);

                var candidateFileName = modelRoot.LogicalAnimations.Count == 1
                    ? $"{baseFileName}.anim"
                    : $"{baseFileName}_{GetSafeAnimationName(animation, animationIndex)}.anim";
                var importedFileName = GetUniqueFileName(candidateFileName, usedFileNames);
                var animBytes = AnimationFile.ConvertToBytes(animFile);
                var packFileImported = new PackFile(importedFileName, new MemorySource(animBytes));
                var newFile = new NewPackFileEntry(settings.DestinationPackPath, packFileImported);
                PackFileDispatcherWriter.AddFilesToPack(
                    _packFileService,
                    settings.DestinationPackFileContainer,
                    [newFile]);
            }
        }

        private static string GetUniqueFileName(string candidate, HashSet<string> usedFileNames)
        {
            if (usedFileNames.Add(candidate))
                return candidate;

            var baseName = Path.GetFileNameWithoutExtension(candidate);
            var extension = Path.GetExtension(candidate);
            for (var duplicateIndex = 2; ; duplicateIndex++)
            {
                var uniqueName = $"{baseName}_{duplicateIndex}{extension}";
                if (usedFileNames.Add(uniqueName))
                    return uniqueName;
            }
        }

        private static string GetSafeAnimationName(Animation animation, int animationIndex)
        {
            var name = string.IsNullOrWhiteSpace(animation.Name)
                ? $"animation_{animationIndex + 1}"
                : animation.Name;
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                name = name.Replace(invalidCharacter, '_');

            return name.Replace(' ', '_');
        }

        public bool Import(GltfImporterSettings settings)
        {
            var modelRoot = CreateModelRoot(settings);

            var skeletonData = GetSkeletonData(modelRoot);

            if (settings.ImportAnimations && modelRoot.LogicalAnimations.Count > 0 && skeletonData.skeletonAnimFile == null)
                throw new InvalidDataException("导入 glTF 动画需要有效的游戏骨架标识和已加载的 CA Pack 文件。");

            if (skeletonData.wasCreated &&
                skeletonData.skeletonAnimFile != null &&
                (settings.ImportMeshes || settings.ImportAnimations))
                SaveSkeletonToPack(skeletonData.skeletonName!, skeletonData.skeletonAnimFile, settings);

            RmvFile? rmv2File = null;
            if (settings.ImportMeshes || settings.ImportMaterials)
            {
                rmv2File = ImportMeshes(
                    settings,
                    modelRoot,
                    skeletonData.skeletonAnimFile,
                    skeletonData.skeletonName ?? "");
                if (rmv2File == null)
                    throw new InvalidDataException("glTF 场景中没有可导入的网格。");

                if (settings.ImportMaterials)
                    ImportMaterials(settings, modelRoot, rmv2File);
            }

            if (settings.ImportAnimations)
                ImportAnimations(settings, modelRoot, skeletonData.skeletonAnimFile, skeletonData.skeletonName ?? "");

            if (settings.ImportMeshes && rmv2File != null)
                SaveRmvFileToPack(settings, GetImportedPackFileName(settings), rmv2File);

            return true;
        }

        private static ModelRoot CreateModelRoot(GltfImporterSettings settings)
        {
            try
            {
                ModelRoot.Validate(settings.InputGltfFile);
                return ModelRoot.Load(settings.InputGltfFile);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("无法读取 glTF/GLB 文件。文件可能无效，或引用的外部文件不完整。", ex);
            }
        }

        private (string? skeletonName, AnimationFile? skeletonAnimFile, bool wasCreated) GetSkeletonData(ModelRoot? modelRoot)
        {
            if (modelRoot == null)
                throw new ArgumentException($"Invalid Input: {nameof(modelRoot)} == {modelRoot}");

            var skeletonName = FetchSkeletonIdStringFromScene(modelRoot);

            if (string.IsNullOrWhiteSpace(skeletonName))
            {
                if (modelRoot.LogicalSkins.Count > 0)
                    throw new InvalidDataException("glTF 包含蒙皮网格，但缺少“//skeleton//骨架名”节点，无法安全映射到游戏骨架。");

                _logger.Information("Skeleton ID not found in scene; importing as a static model.");
                return ("", null, false);
            }

            var skeletonAnimFile = _skeletonLookUpHelper.GetSkeletonFileFromName(skeletonName);
            if (skeletonAnimFile != null)
                return (skeletonName, skeletonAnimFile, false);

            if (modelRoot.LogicalSkins.Count > 0)
            {
                var importedSkeleton = GltfSkeletonImporter.Build(
                    modelRoot,
                    skeletonName,
                    mirrorMesh: true);
                return (skeletonName, importedSkeleton, true);
            }

            throw new InvalidDataException(
                $"找不到游戏骨架“{skeletonName}”，且 glTF 不包含可用于创建骨架的蒙皮数据。");
        }

        private void SaveSkeletonToPack(
            string skeletonName,
            AnimationFile skeleton,
            GltfImporterSettings settings)
        {
            var fileName = Path.GetFileNameWithoutExtension(skeletonName) + ".anim";
            var packFile = new PackFile(
                fileName,
                new MemorySource(AnimationFile.ConvertToBytes(skeleton)));
            PackFileDispatcherWriter.AddFilesToPack(
                _packFileService,
                settings.DestinationPackFileContainer,
                [new NewPackFileEntry(@"animations\skeletons", packFile)]);
        }

        private static string GetImportedPackFileName(GltfImporterSettings settings) => Path.GetFileNameWithoutExtension(settings.InputGltfFile) + ".rigid_model_v2";

        private static string FetchSkeletonIdStringFromScene(ModelRoot modelRoot)
        {
            var nodeSearchResult = modelRoot.LogicalNodes.Where(
                node => node.Name?.StartsWith(Value, StringComparison.OrdinalIgnoreCase) == true);

            if (nodeSearchResult == null || !nodeSearchResult.Any())
                return "";

            var skeletonName = nodeSearchResult.First().Name.Substring(Value.Length);

            return skeletonName.ToLower();
        }
    }
}
