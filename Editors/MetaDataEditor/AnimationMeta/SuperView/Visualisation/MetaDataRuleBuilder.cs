using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using GameWorld.Core.SceneNodes;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    internal sealed class MetaDataRuleBuilder
    {
        private readonly ILogger _logger = Logging.Create<MetaDataRuleBuilder>();
        private readonly MetaDataResourceResolver _resourceResolver;

        private sealed record DockRuleSource(
            ParsedMetadataAttribute Source,
            int EquipmentBoneId,
            string AnimationSlotName,
            string[] SkeletonNameAlternatives,
            float StartTime,
            float EndTime);

        public MetaDataRuleBuilder(
            MetaDataResourceResolver resourceResolver)
        {
            _resourceResolver = resourceResolver;
        }

        public void AppendRules(
            ParsedMetadataFile? file,
            MetaDataDocumentOwner owner,
            IAnimationBinGenericFormat? fragment,
            ISkeletonProvider skeleton,
            ICollection<IAnimationChangeRule> rules,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            if (file == null)
                return;

            foreach (var item in file.Attributes)
            {
                var dock = item switch
                {
                    DockEquipment current => new DockRuleSource(current,
                        current.PropBoneId, current.AnimationSlotName,
                        current.SkeletonNameAlternatives, current.StartTime, current.EndTime),
                    DockEquipment_v3 legacy when !string.IsNullOrEmpty(legacy.AnimationSlotName) =>
                        // Legacy v3 stores equipment slots from one; modern tags store be_prop indices.
                        new DockRuleSource(legacy, legacy.PropBoneId > 0 ? legacy.PropBoneId - 1 : -1, legacy.AnimationSlotName,
                            legacy.SkeletonNameAlternatives, legacy.StartTime, legacy.EndTime),
                    _ => null,
                };
                if (dock == null)
                    continue;

                try
                {
                    TryAddDockRule(
                        dock,
                        owner,
                        fragment,
                        skeleton,
                        rules,
                        diagnostics);
                }
                catch (Exception exception)
                {
                    _logger.Here().Warning(
                        $"Skipping metadata preview rule for '{item.Name}': {exception.Message}");
                    AddDiagnostic(
                        item,
                        owner,
                        "SuperView.Diagnostics.RuleUnavailable",
                        diagnostics);
                }
            }

            foreach (var item in file.GetItemsOfType<Transform_v10>())
            {
                TryAddRule(
                    item,
                    owner,
                    () => new TransformBoneRule(item),
                    rules,
                    diagnostics);
            }
        }

        private void TryAddDockRule(
            DockRuleSource source,
            MetaDataDocumentOwner owner,
            IAnimationBinGenericFormat? fragment,
            ISkeletonProvider skeleton,
            ICollection<IAnimationChangeRule> rules,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            if (source.Source is DockEquipment_v3 && source.EquipmentBoneId < 0)
            {
                AddDiagnostic(source.Source, owner,
                    "SuperView.Diagnostics.RuleUnavailable", diagnostics);
                return;
            }

            if (fragment == null)
            {
                AddDiagnostic(
                    source.Source,
                    owner,
                    "SuperView.Diagnostics.MissingAnimation",
                    diagnostics,
                    source.AnimationSlotName);
                return;
            }

            var animationPath = fragment.Entries.FirstOrDefault(entry =>
                    entry.SlotName == source.AnimationSlotName)?.AnimationFile ??
                fragment.Entries.FirstOrDefault(entry =>
                    entry.SlotName == source.AnimationSlotName + "_2")?.AnimationFile;
            if (string.IsNullOrWhiteSpace(animationPath))
            {
                AddDiagnostic(
                    source.Source,
                    owner,
                    "SuperView.Diagnostics.MissingAnimation",
                    diagnostics,
                    source.AnimationSlotName);
                return;
            }

            var boneIndex = FindBoneIndex(source, skeleton);
            if (boneIndex < 0)
            {
                AddDiagnostic(
                    source.Source,
                    owner,
                    "SuperView.Diagnostics.MissingBone",
                    diagnostics,
                    boneName: string.Join(", ", source.SkeletonNameAlternatives));
                return;
            }

            var animationFile = _resourceResolver.FindAnimation(
                source.Source,
                owner,
                animationPath,
                diagnostics);
            if (animationFile == null)
                return;

            var equipmentBoneName = DockEquipmentRule.GetEquipmentBoneName(source.EquipmentBoneId);
            if (source.Source is DockEquipment_v3 &&
                skeleton.Skeleton.GetBoneIndexByName(equipmentBoneName) < 0)
            {
                AddDiagnostic(source.Source, owner,
                    "SuperView.Diagnostics.MissingBone", diagnostics,
                    boneName: equipmentBoneName);
                return;
            }

            TryAddRule(
                source.Source,
                owner,
                () =>
                {
                    var clip = new AnimationClip(
                        AnimationFile.Create(animationFile),
                        skeleton.Skeleton);
                    return new DockEquipmentRule(
                        boneIndex,
                        source.EquipmentBoneId,
                        clip,
                        skeleton,
                        source.StartTime,
                        source.EndTime);
                },
                rules,
                diagnostics);
        }

        private static int FindBoneIndex(
            DockRuleSource source,
            ISkeletonProvider skeleton)
        {
            if (skeleton?.Skeleton == null)
                return -1;

            foreach (var boneName in source.SkeletonNameAlternatives)
            {
                var boneIndex = skeleton.Skeleton.GetBoneIndexByName(boneName);
                if (boneIndex >= 0)
                    return boneIndex;
            }

            return -1;
        }

        private void TryAddRule(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            Func<IAnimationChangeRule> create,
            ICollection<IAnimationChangeRule> rules,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            try
            {
                rules.Add(create());
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(
                    $"Skipping metadata preview rule for '{source.Name}': {exception.Message}");
                AddDiagnostic(
                    source,
                    owner,
                    "SuperView.Diagnostics.RuleUnavailable",
                    diagnostics);
            }
        }

        private static void AddDiagnostic(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            string reasonKey,
            ICollection<MetaDataBuildDiagnostic> diagnostics,
            string? resourcePath = null,
            string? boneName = null) =>
            diagnostics.Add(MetaDataDiagnosticFactory.Create(
                source,
                owner,
                reasonKey,
                resourcePath,
                boneName));
    }
}
