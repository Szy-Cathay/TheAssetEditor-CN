using System.IO;
using Editors.ImportExport.Importing;
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
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using SharpGLTF.Schema2;


namespace Editors.ImportExport.Importing.Importers.GltfToRmv
{
    public class GltfImporter
    {
        private const string Value = "//skeleton//";
        private const string HumanoidSkeletonPath = @"animations\skeletons\humanoid01.anim";
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

        private static RmvMeshBuildResult ImportMeshes(
            GltfImporterSettings settings,
            ModelRoot modelRoot,
            AnimationFile? skeletonAnimFile,
            string skeletonName,
            float scaleFactor) =>
            RmvMeshBuilder.BuildWithSummary(
                settings,
                modelRoot,
                skeletonAnimFile,
                skeletonName,
                scaleFactor);

        private static NewPackFileEntry CreateRmvEntry(
            GltfImporterSettings settings,
            string importedFileName,
            RmvFile rmv2File)
        {
            var bytesRmv2 = ModelFactory.Create().Save(rmv2File);

            var packFileImported = new PackFile(importedFileName, new MemorySource(bytesRmv2));
            return new NewPackFileEntry(settings.DestinationPackPath, packFileImported);
        }

        private RmvMaterialBuildResult ImportMaterials(
            GltfImporterSettings settings,
            ModelRoot modelRoot,
            RmvFile rmv2File) =>
            _materialBuilder.BuildRmvFileMaterials(settings, modelRoot, rmv2File);

        private static IReadOnlyList<NewPackFileEntry> ImportAnimations(
            GltfImporterSettings settings,
            ModelRoot modelRoot,
            AnimationFile? skeletonAnimFile,
            string skeletonName,
            float scaleFactor)
        {
            if (modelRoot.LogicalAnimations.Count == 0)
                return [];
            if (skeletonAnimFile == null)
                throw new InvalidDataException("glTF 包含动画，但未能确定对应的游戏骨架，已停止导入以避免生成损坏的 ANIM 文件。");

            var baseFileName = Path.GetFileNameWithoutExtension(settings.InputGltfFile);
            var animationEntries = new List<NewPackFileEntry>();
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
                HumanoidScaleCalculator.ScaleTranslations(animFile, scaleFactor);
                GltfSourceForwardConverter.ConvertAnimation(
                    animFile,
                    settings.SourceForwardDirection);

                var candidateFileName =
                    $"{baseFileName}_{GetSafeAnimationName(animation, animationIndex)}.anim";
                var animBytes = AnimationFile.ConvertToBytes(animFile);
                var packFileImported = new PackFile(candidateFileName, new MemorySource(animBytes));
                animationEntries.Add(new NewPackFileEntry(
                    settings.DestinationPackPath,
                    packFileImported));
            }

            return animationEntries;
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

        public ImportResult Import(GltfImporterSettings settings)
        {
            try
            {
                return ImportCore(settings);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to import glTF file {InputFile}", settings.InputGltfFile);
                return ImportResult.Failure(exception);
            }
        }

        private ImportResult ImportCore(GltfImporterSettings settings)
        {
            var modelRoot = CreateModelRoot(settings);
            if (settings.ImportMeshes || settings.ImportMaterials)
                RmvMaterialBuilder.ValidateMaterialModes(modelRoot);

            var skeletonData = GetSkeletonData(settings, modelRoot);
            var humanoidScale = GetHumanoidScale(settings, skeletonData);
            var scaleFactor = humanoidScale.ScaleFactor;
            var pendingFiles = new List<NewPackFileEntry>();
            MaterialImportSummary? materialSummary = null;

            if (settings.ImportAnimations && modelRoot.LogicalAnimations.Count > 0 && skeletonData.skeletonAnimFile == null)
                throw new InvalidDataException("导入 glTF 动画需要有效的游戏骨架标识和已加载的 CA Pack 文件。");

            RmvFile? rmv2File = null;
            var meshSummary = new RmvMeshImportSummary([]);
            if (settings.ImportMeshes || settings.ImportMaterials)
            {
                var meshBuildResult = ImportMeshes(
                    settings,
                    modelRoot,
                    skeletonData.skeletonAnimFile,
                    skeletonData.skeletonName ?? "",
                    scaleFactor);
                rmv2File = meshBuildResult.File;
                meshSummary = meshBuildResult.Summary;
                if (rmv2File == null)
                    throw new InvalidDataException("glTF 场景中没有可导入的网格。");

                if (settings.ImportMaterials)
                {
                    var materialBuildResult = ImportMaterials(settings, modelRoot, rmv2File);
                    pendingFiles.AddRange(materialBuildResult.Files);
                    materialSummary = materialBuildResult.Summary;
                }
            }

            if (settings.ImportAnimations)
            {
                pendingFiles.AddRange(ImportAnimations(
                    settings,
                    modelRoot,
                    skeletonData.skeletonAnimFile,
                    skeletonData.skeletonName ?? "",
                    scaleFactor));
            }

            if (skeletonData.wasCreated &&
                skeletonData.skeletonAnimFile != null &&
                (settings.ImportMeshes || settings.ImportAnimations))
            {
                HumanoidScaleCalculator.ScaleTranslations(
                    skeletonData.skeletonAnimFile,
                    scaleFactor);
                GltfSourceForwardConverter.ConvertAnimation(
                    skeletonData.skeletonAnimFile,
                    settings.SourceForwardDirection);
                pendingFiles.Add(CreateSkeletonEntry(
                    skeletonData.skeletonName!,
                    skeletonData.skeletonAnimFile));
            }

            if (settings.ImportMeshes && rmv2File != null)
            {
                pendingFiles.Add(CreateRmvEntry(
                    settings,
                    GetImportedPackFileName(settings),
                    rmv2File));
            }

            var validation = ValidatePendingFiles(pendingFiles);
            if (validation.Errors.Count != 0)
                return ImportResult.Failure(validation.Errors);

            if (validation.Files.Count != 0)
            {
                var conflicts = PackFileDispatcherWriter.AddFilesToPackIfNoConflicts(
                    _packFileService,
                    settings.DestinationPackFileContainer,
                    validation.Files,
                    validation.Paths);
                if (conflicts.Count != 0)
                {
                    return ImportResult.Failure(conflicts
                        .Select(path => LocalizationManager.Instance.GetFormat(
                            "GltfImporter.TargetConflict",
                            path))
                        .ToList());
                }
            }

            return ImportResult.Success(
                validation.Paths,
                BuildMeshWarnings(meshSummary),
                humanoidScale,
                materialSummary,
                BuildSourceForwardSummary(settings.SourceForwardDirection));
        }

        private static SourceForwardImportSummary BuildSourceForwardSummary(
            GltfSourceForwardDirection sourceForwardDirection) =>
            sourceForwardDirection switch
            {
                GltfSourceForwardDirection.PositiveZ => new(
                    LocalizationManager.Instance.Get(
                        "GltfImporter.SourceForward.PositiveZ"),
                    LocalizationManager.Instance.Get(
                        "GltfImporter.SourceForward.Conversion.PositiveZ")),
                GltfSourceForwardDirection.PositiveX => new(
                    LocalizationManager.Instance.Get(
                        "GltfImporter.SourceForward.PositiveX"),
                    LocalizationManager.Instance.Get(
                        "GltfImporter.SourceForward.Conversion.PositiveX")),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(sourceForwardDirection),
                    sourceForwardDirection,
                    null),
            };

        private HumanoidScaleImportSummary GetHumanoidScale(
            GltfImporterSettings settings,
            (string? skeletonName, AnimationFile? skeletonAnimFile, bool wasCreated, bool isExternal) skeletonData)
        {
            if (!settings.AutoScaleHumanoid)
            {
                return new HumanoidScaleImportSummary(
                    false,
                    null,
                    null,
                    1,
                    LocalizationManager.Instance.Get("GltfImporter.ScaleReason.Disabled"));
            }
            if (!skeletonData.isExternal || skeletonData.skeletonAnimFile == null)
            {
                return new HumanoidScaleImportSummary(
                    false,
                    null,
                    null,
                    1,
                    LocalizationManager.Instance.Get("GltfImporter.ScaleReason.NoExternalSkeleton"));
            }
            if (settings.SelectedGame != GameTypeEnum.Warhammer3)
            {
                throw new InvalidDataException(LocalizationManager.Instance.Get(
                    "GltfImporter.Error.AutoScaleRequiresWarhammer3"));
            }

            return HumanoidScaleCalculator.Calculate(
                skeletonData.skeletonAnimFile,
                GetCaHumanoidSkeleton);
        }

        private AnimationFile GetCaHumanoidSkeleton()
        {
            foreach (var container in _packFileService
                         .GetAllPackfileContainers()
                         .Where(container => container.IsCaPackFile)
                         .Reverse())
            {
                var entry = container.FileList.FirstOrDefault(item =>
                    string.Equals(
                        item.Key.Replace('/', '\\'),
                        HumanoidSkeletonPath,
                        StringComparison.OrdinalIgnoreCase));
                if (entry.Value != null)
                    return AnimationFile.Create(entry.Value);
            }

            throw new InvalidDataException(LocalizationManager.Instance.Get(
                "GltfImporter.Error.MissingCaHumanoidSkeleton"));
        }

        private static IReadOnlyList<string> BuildMeshWarnings(
            RmvMeshImportSummary summary)
        {
            var warnings = new List<string>();
            if (summary.TotalAffectedVertices > 0)
            {
                var segments = string.Join(
                    "；",
                    summary.Segments
                        .Where(segment => segment.AffectedVertices > 0)
                        .Select(segment => LocalizationManager.Instance.GetFormat(
                            "GltfImporter.Warning.WeightSegment",
                            segment.ModelName,
                            segment.AffectedVertices)));
                warnings.Add(LocalizationManager.Instance.GetFormat(
                    "GltfImporter.Warning.WeightLimit",
                    summary.TotalAffectedVertices,
                    segments,
                    summary.MaximumDiscardedWeight,
                    summary.VerticesAboveTenPercentDiscarded));
            }

            AddSegmentWarning(
                warnings,
                summary.Segments.Where(segment => segment.RebuiltNormals),
                "GltfImporter.Warning.RebuiltNormals");
            AddSegmentWarning(
                warnings,
                summary.Segments.Where(segment => segment.RebuiltTangents),
                "GltfImporter.Warning.RebuiltTangents");
            AddSegmentWarning(
                warnings,
                summary.Segments.Where(segment => segment.DefaultedTextureCoordinates),
                "GltfImporter.Warning.DefaultedUv");
            AddSegmentWarning(
                warnings,
                summary.Segments.Where(segment => segment.IgnoredVertexColors),
                "GltfImporter.Warning.IgnoredVertexColors");
            AddSegmentWarning(
                warnings,
                summary.Segments.Where(segment => segment.IgnoredMorphTargets),
                "GltfImporter.Warning.IgnoredMorphTargets");
            return warnings;
        }

        private static void AddSegmentWarning(
            ICollection<string> warnings,
            IEnumerable<RmvMeshSegmentImportSummary> segments,
            string localizationKey)
        {
            var names = segments
                .Select(segment => segment.ModelName)
                .ToList();
            if (names.Count == 0)
                return;

            warnings.Add(LocalizationManager.Instance.GetFormat(
                localizationKey,
                string.Join("、", names)));
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

        private (string? skeletonName, AnimationFile? skeletonAnimFile, bool wasCreated, bool isExternal) GetSkeletonData(
            GltfImporterSettings settings,
            ModelRoot? modelRoot)
        {
            if (modelRoot == null)
                throw new ArgumentException($"Invalid Input: {nameof(modelRoot)} == {modelRoot}");

            var skeletonName = FetchSkeletonIdStringFromScene(modelRoot);

            if (string.IsNullOrWhiteSpace(skeletonName))
            {
                if (modelRoot.LogicalSkins.Count > 0)
                {
                    var importedSkeleton = GltfSkeletonImporter.BuildExternal(
                        modelRoot,
                        settings.InputGltfFile,
                        settings.NewSkeletonName,
                        mirrorMesh: true);
                    skeletonName = importedSkeleton.Header.SkeletonName;
                    return (skeletonName, importedSkeleton, true, true);
                }

                _logger.Information("Skeleton ID not found in scene; importing as a static model.");
                return ("", null, false, false);
            }

            var skeletonAnimFile = _skeletonLookUpHelper.GetSkeletonFileFromName(skeletonName);
            if (skeletonAnimFile != null)
                return (skeletonName, skeletonAnimFile, false, false);

            if (modelRoot.LogicalSkins.Count > 0)
            {
                var importedSkeleton = GltfSkeletonImporter.Build(
                    modelRoot,
                    skeletonName,
                    mirrorMesh: true);
                return (skeletonName, importedSkeleton, true, false);
            }

            throw new InvalidDataException(
                $"找不到游戏骨架“{skeletonName}”，且 glTF 不包含可用于创建骨架的蒙皮数据。");
        }

        private static NewPackFileEntry CreateSkeletonEntry(
            string skeletonName,
            AnimationFile skeleton)
        {
            var fileName = Path.GetFileNameWithoutExtension(skeletonName) + ".anim";
            var packFile = new PackFile(
                fileName,
                new MemorySource(AnimationFile.ConvertToBytes(skeleton)));
            return new NewPackFileEntry(@"animations\skeletons", packFile);
        }

        private static string GetImportedPackFileName(GltfImporterSettings settings) => Path.GetFileNameWithoutExtension(settings.InputGltfFile) + ".rigid_model_v2";

        private static (
            List<NewPackFileEntry> Files,
            IReadOnlyList<string> Paths,
            IReadOnlyList<string> Errors)
            ValidatePendingFiles(
                IReadOnlyList<NewPackFileEntry> pendingFiles)
        {
            var normalizedFiles = pendingFiles
                .Select(entry =>
                {
                    var path = FolderProjectPathPolicy.EnsureResourcePath(
                        Path.Combine(
                            (entry.DirectoyPath ?? "").Trim(),
                            entry.PackFile.Name.Trim()))
                        .ToLowerInvariant();
                    var file = new PackFile(
                        Path.GetFileName(path),
                        entry.PackFile.DataSource);
                    return new NewPackFileEntry(
                        Path.GetDirectoryName(path) ?? "",
                        file);
                })
                .ToList();
            var paths = normalizedFiles
                .Select(entry => Path.Combine(
                    entry.DirectoyPath,
                    entry.PackFile.Name))
                .ToList();
            var duplicatePaths = paths
                .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var errors = duplicatePaths
                .Select(path => LocalizationManager.Instance.GetFormat(
                    "GltfImporter.DuplicateTarget",
                    path))
                .ToList();
            return (normalizedFiles, paths, errors);
        }

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
