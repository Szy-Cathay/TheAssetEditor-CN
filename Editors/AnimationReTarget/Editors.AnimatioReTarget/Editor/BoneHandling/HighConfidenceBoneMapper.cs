using Shared.GameFormats.Animation;

namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    public enum BoneAutoMappingStatus
    {
        Confirmed,
        ReviewRequired,
        Unmatched
    }

    public enum BoneAutoMappingEvidence
    {
        None,
        ExistingMapping,
        ExactName,
        NormalizedName,
        KnownAlias,
        Hierarchy
    }

    public sealed record BoneAutoMappingCandidate(
        int SourceBoneIndex,
        string SourceBoneName,
        BoneAutoMappingEvidence Evidence);

    public sealed record BoneAutoMappingItem(
        int TargetBoneIndex,
        string TargetBoneName,
        BoneAutoMappingStatus Status,
        int? SourceBoneIndex,
        string? SourceBoneName,
        BoneAutoMappingEvidence Evidence,
        IReadOnlyList<BoneAutoMappingCandidate> Candidates);

    public sealed class BoneAutoMappingSummary
    {
        public BoneAutoMappingSummary(IReadOnlyList<BoneAutoMappingItem> items)
        {
            Items = items;
        }

        public IReadOnlyList<BoneAutoMappingItem> Items { get; }
        public int ConfirmedCount => Items.Count(item => item.Status == BoneAutoMappingStatus.Confirmed);
        public int ReviewRequiredCount => Items.Count(item => item.Status == BoneAutoMappingStatus.ReviewRequired);
        public int UnmatchedCount => Items.Count(item => item.Status == BoneAutoMappingStatus.Unmatched);
    }

    public static class HighConfidenceBoneMapper
    {
        private static readonly IReadOnlyDictionary<string, string> KnownAliases = CreateKnownAliases();

        public static BoneAutoMappingSummary CreateSummary(
            AnimationFile sourceSkeleton,
            AnimationFile targetSkeleton,
            IReadOnlyDictionary<int, int>? existingMappings = null)
        {
            var confirmedMappings = new Dictionary<int, int>();
            var items = new List<BoneAutoMappingItem>();
            foreach (var targetBone in targetSkeleton.Bones
                         .OrderBy(bone => GetBoneDepth(targetSkeleton, bone))
                         .ThenBy(bone => bone.Id))
            {
                var item = CreateItem(sourceSkeleton, targetBone, existingMappings, confirmedMappings);
                items.Add(item);
                if (item.Status == BoneAutoMappingStatus.Confirmed && item.SourceBoneIndex.HasValue)
                    confirmedMappings[targetBone.Id] = item.SourceBoneIndex.Value;
            }

            return new BoneAutoMappingSummary(items.OrderBy(item => item.TargetBoneIndex).ToArray());
        }

        private static BoneAutoMappingItem CreateItem(
            AnimationFile sourceSkeleton,
            AnimationFile.BoneInfo targetBone,
            IReadOnlyDictionary<int, int>? existingMappings,
            IReadOnlyDictionary<int, int> confirmedMappings)
        {
            if (existingMappings?.TryGetValue(targetBone.Id, out var existingSourceBoneIndex) == true)
            {
                var existingSourceBone = sourceSkeleton.Bones
                    .SingleOrDefault(sourceBone => sourceBone.Id == existingSourceBoneIndex);
                if (existingSourceBone != null)
                {
                    var existingCandidate = new BoneAutoMappingCandidate(
                        existingSourceBone.Id,
                        existingSourceBone.Name,
                        BoneAutoMappingEvidence.ExistingMapping);
                    return new BoneAutoMappingItem(
                        targetBone.Id,
                        targetBone.Name,
                        BoneAutoMappingStatus.Confirmed,
                        existingSourceBone.Id,
                        existingSourceBone.Name,
                        BoneAutoMappingEvidence.ExistingMapping,
                        [existingCandidate]);
                }
            }

            var candidates = CreateCandidates(
                sourceSkeleton.Bones.Where(sourceBone =>
                    string.Equals(sourceBone.Name, targetBone.Name, StringComparison.OrdinalIgnoreCase)),
                BoneAutoMappingEvidence.ExactName);

            if (candidates.Length == 0)
            {
                var normalizedTargetName = NormalizeName(targetBone.Name);
                candidates = CreateCandidates(
                    sourceSkeleton.Bones.Where(sourceBone =>
                        NormalizeName(sourceBone.Name) == normalizedTargetName),
                    BoneAutoMappingEvidence.NormalizedName);
            }

            if (candidates.Length == 0 && KnownAliases.TryGetValue(NormalizeName(targetBone.Name), out var aliasGroup))
            {
                candidates = CreateCandidates(
                    sourceSkeleton.Bones.Where(sourceBone =>
                        KnownAliases.TryGetValue(NormalizeName(sourceBone.Name), out var sourceAliasGroup) &&
                        sourceAliasGroup == aliasGroup),
                    BoneAutoMappingEvidence.KnownAlias);
            }

            if (confirmedMappings.TryGetValue(targetBone.ParentId, out var sourceParentBoneIndex))
            {
                if (candidates.Length > 1)
                {
                    var hierarchyCandidates = candidates
                        .Where(candidate => sourceSkeleton.Bones
                            .Single(sourceBone => sourceBone.Id == candidate.SourceBoneIndex)
                            .ParentId == sourceParentBoneIndex)
                        .ToArray();
                    if (hierarchyCandidates.Length == 1)
                    {
                        var hierarchyCandidate = hierarchyCandidates[0] with
                        {
                            Evidence = BoneAutoMappingEvidence.Hierarchy
                        };
                        candidates = [hierarchyCandidate];
                    }
                }

                if (candidates.Length == 1 &&
                    sourceSkeleton.Bones.Single(sourceBone =>
                        sourceBone.Id == candidates[0].SourceBoneIndex).ParentId != sourceParentBoneIndex)
                {
                    return new BoneAutoMappingItem(
                        targetBone.Id,
                        targetBone.Name,
                        BoneAutoMappingStatus.ReviewRequired,
                        null,
                        null,
                        BoneAutoMappingEvidence.None,
                        candidates);
                }
            }

            if (candidates.Length == 1)
            {
                var candidate = candidates[0];
                return new BoneAutoMappingItem(
                    targetBone.Id,
                    targetBone.Name,
                    BoneAutoMappingStatus.Confirmed,
                    candidate.SourceBoneIndex,
                    candidate.SourceBoneName,
                    candidate.Evidence,
                    candidates);
            }

            var status = candidates.Length == 0
                ? BoneAutoMappingStatus.Unmatched
                : BoneAutoMappingStatus.ReviewRequired;
            return new BoneAutoMappingItem(
                targetBone.Id,
                targetBone.Name,
                status,
                null,
                null,
                BoneAutoMappingEvidence.None,
                candidates);
        }

        private static BoneAutoMappingCandidate[] CreateCandidates(
            IEnumerable<AnimationFile.BoneInfo> sourceBones,
            BoneAutoMappingEvidence evidence)
        {
            return sourceBones
                .OrderBy(sourceBone => sourceBone.Id)
                .Select(sourceBone => new BoneAutoMappingCandidate(
                    sourceBone.Id,
                    sourceBone.Name,
                    evidence))
                .ToArray();
        }

        private static string NormalizeName(string name)
        {
            var normalizedName = name.Trim();
            if (normalizedName.Length >= 2 &&
                normalizedName[^1] == '0' &&
                !char.IsLetterOrDigit(normalizedName[^2]))
            {
                normalizedName = normalizedName[..^2];
            }

            return new string(normalizedName
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static int GetBoneDepth(AnimationFile skeleton, AnimationFile.BoneInfo bone)
        {
            var depth = 0;
            var parentId = bone.ParentId;
            while (parentId != AnimationFile.BoneIndexNoParent)
            {
                depth++;
                parentId = skeleton.Bones.Single(parent => parent.Id == parentId).ParentId;
            }

            return depth;
        }

        private static IReadOnlyDictionary<string, string> CreateKnownAliases()
        {
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            AddAliasGroup(aliases, "hips", "root", "bn_hips");
            AddAliasGroup(aliases, "spine0", "spine_0", "bn_spine");
            AddAliasGroup(aliases, "neck0", "neck_0", "bn_neck");
            AddAliasGroup(aliases, "eyebrows", "eyebrow", "eyebrows_0", "bn_eyebrows");

            AddMirroredAliasGroup(aliases, "handleft", "hand_left", "arm_left_2", "bn_lefthand");
            AddMirroredAliasGroup(aliases, "upperarmleft", "arm_left_0", "upperarm_left", "bn_leftarm");
            AddMirroredAliasGroup(aliases, "lowerarmleft", "arm_left_1", "lowerarm_left", "bn_leftforearm");
            AddMirroredAliasGroup(aliases, "upperarmrollleft", "arm_left_0_roll_0", "upperarm_roll_left_0", "bn_leftarmroll");
            AddMirroredAliasGroup(aliases, "lowerarmrollleft", "arm_left_1_roll_0", "lowerarm_left_roll", "lowerarm_roll_left", "bn_leftforearmroll");
            AddMirroredAliasGroup(aliases, "shoulderleft", "clav_left", "bn_leftshoulder");
            AddMirroredAliasGroup(aliases, "shoulderpadleft", "shoulder_pad_left", "shoulderpad_left_0");
            AddMirroredAliasGroup(aliases, "upperlegleft", "leg_left_0", "upperleg_left", "bn_leftupleg");
            AddMirroredAliasGroup(aliases, "lowerlegleft", "leg_left_1", "lowerleg_left", "bn_leftleg");
            AddMirroredAliasGroup(aliases, "footleft", "leg_left_2", "foot_left", "bn_leftfoot");
            AddMirroredAliasGroup(aliases, "toeleft", "toe_left_0", "bn_lefttoebase");
            AddMirroredAliasGroup(aliases, "eyeleft", "eye_left", "bn_lefteye");

            for (var segment = 0; segment < 3; segment++)
            {
                AddMirroredAliasGroup(
                    aliases,
                    $"indexleft{segment}",
                    $"finger_index_left_{segment}",
                    $"bn_lefthandindex{segment + 1}");
                AddMirroredAliasGroup(
                    aliases,
                    $"ringleft{segment}",
                    $"finger_ring_left_{segment}",
                    $"bn_lefthandring{segment + 1}");
                AddMirroredAliasGroup(
                    aliases,
                    $"thumbleft{segment}",
                    $"thumb_left_{segment}",
                    $"bn_lefthandthumb{segment + 1}");
            }

            for (var weapon = 1; weapon <= 6; weapon++)
                AddAliasGroup(aliases, $"weapon{weapon}", $"be_prop_{weapon - 1}", $"weapon_{weapon}");

            return aliases;
        }

        private static void AddMirroredAliasGroup(
            IDictionary<string, string> aliases,
            string groupName,
            params string[] names)
        {
            AddAliasGroup(aliases, groupName, names);
            AddAliasGroup(
                aliases,
                groupName.Replace("left", "right", StringComparison.Ordinal),
                names.Select(name => name.Replace("left", "right", StringComparison.Ordinal)).ToArray());
        }

        private static void AddAliasGroup(
            IDictionary<string, string> aliases,
            string groupName,
            params string[] names)
        {
            foreach (var name in names)
                aliases[NormalizeName(name)] = groupName;
        }
    }
}
