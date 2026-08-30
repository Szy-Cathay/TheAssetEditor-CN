using Shared.GameFormats.Animation;

namespace Editors.AnimatioReTarget.Editor.BoneHandling;

public sealed record HumanoidBoneMapping(
    int TargetBoneIndex,
    int SourceBoneIndex);

public sealed class HumanoidBoneMappingResult
{
    public HumanoidBoneMappingResult(
        IReadOnlyList<HumanoidBoneMapping> mappings,
        int targetBoneCount,
        IReadOnlySet<int> translationTargetBoneIndices)
    {
        Mappings = mappings;
        TargetBoneCount = targetBoneCount;
        TranslationTargetBoneIndices = translationTargetBoneIndices;
    }

    public IReadOnlyList<HumanoidBoneMapping> Mappings { get; }
    public int TargetBoneCount { get; }
    public IReadOnlySet<int> TranslationTargetBoneIndices { get; }
    public int MatchedCount => Mappings.Count;
    public int UnmatchedCount => TargetBoneCount - MatchedCount;
}

public static class HumanoidBoneMapper
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        CreateAliases();

    public static HumanoidBoneMappingResult CreateMappings(
        AnimationFile sourceSkeleton,
        AnimationFile targetSkeleton)
    {
        var mappings = new Dictionary<int, int>();
        var sourceHips = FindHips(sourceSkeleton);
        var targetHips = FindHips(targetSkeleton);

        if (sourceHips != null && targetHips != null)
        {
            AddMapping(mappings, targetHips.Id, sourceHips.Id);
            MapRootMotion(
                sourceSkeleton,
                targetSkeleton,
                sourceHips,
                targetHips,
                mappings);
        }

        MapNamedChain(sourceSkeleton, targetSkeleton, "spine", mappings);
        MapNamedChain(sourceSkeleton, targetSkeleton, "neck", mappings);
        MapAliases(sourceSkeleton, targetSkeleton, mappings);

        var translationTargetBoneIndices = new HashSet<int>();
        if (sourceHips != null && targetHips != null)
        {
            translationTargetBoneIndices.Add(targetHips.Id);
            var targetRoot = FindTopAncestor(targetSkeleton, targetHips);
            if (mappings.ContainsKey(targetRoot.Id))
                translationTargetBoneIndices.Add(targetRoot.Id);
        }

        return new HumanoidBoneMappingResult(
            mappings
                .OrderBy(mapping => mapping.Key)
                .Select(mapping => new HumanoidBoneMapping(
                    mapping.Key,
                    mapping.Value))
                .ToArray(),
            targetSkeleton.Bones.Length,
            translationTargetBoneIndices);
    }

    private static void MapRootMotion(
        AnimationFile sourceSkeleton,
        AnimationFile targetSkeleton,
        AnimationFile.BoneInfo sourceHips,
        AnimationFile.BoneInfo targetHips,
        IDictionary<int, int> mappings)
    {
        var sourceRoot = FindTopAncestor(sourceSkeleton, sourceHips);
        var targetRoot = FindTopAncestor(targetSkeleton, targetHips);
        if (sourceRoot.Id == sourceHips.Id || targetRoot.Id == targetHips.Id)
            return;

        AddMapping(mappings, targetRoot.Id, sourceRoot.Id);
    }

    private static void MapNamedChain(
        AnimationFile sourceSkeleton,
        AnimationFile targetSkeleton,
        string nameFragment,
        IDictionary<int, int> mappings)
    {
        var sourceChain = FindNamedChain(sourceSkeleton, nameFragment);
        var targetChain = FindNamedChain(targetSkeleton, nameFragment);
        if (sourceChain.Length == 0 || targetChain.Length == 0)
            return;

        for (var targetIndex = 0; targetIndex < targetChain.Length; targetIndex++)
        {
            var sourceIndex = targetChain.Length == 1
                ? sourceChain.Length - 1
                : (int)MathF.Round(
                    targetIndex * (sourceChain.Length - 1f) /
                    (targetChain.Length - 1f));
            AddMapping(
                mappings,
                targetChain[targetIndex].Id,
                sourceChain[sourceIndex].Id);
        }
    }

    private static AnimationFile.BoneInfo[] FindNamedChain(
        AnimationFile skeleton,
        string nameFragment)
    {
        return skeleton.Bones
            .Where(bone => NormalizeName(bone.Name).Contains(nameFragment))
            .OrderBy(bone => GetBoneDepth(skeleton, bone))
            .ThenBy(bone => bone.Id)
            .ToArray();
    }

    private static void MapAliases(
        AnimationFile sourceSkeleton,
        AnimationFile targetSkeleton,
        IDictionary<int, int> mappings)
    {
        var sourceByRole = sourceSkeleton.Bones
            .Select(bone => (Bone: bone, Role: GetAliasRole(bone.Name)))
            .Where(item => item.Role != null)
            .GroupBy(item => item.Role!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Bone).ToArray(),
                StringComparer.Ordinal);

        foreach (var targetBone in targetSkeleton.Bones)
        {
            if (mappings.ContainsKey(targetBone.Id))
                continue;

            var role = GetAliasRole(targetBone.Name);
            if (role == null || IsRigSpecificDeformationRole(role))
                continue;

            var sourceBone = GetUniqueSourceBone(sourceByRole, role);
            if (sourceBone == null && TryGetOuterFingerFallback(role, out var fallbackRole))
                sourceBone = GetUniqueSourceBone(sourceByRole, fallbackRole);

            if (sourceBone == null)
                continue;

            AddMapping(mappings, targetBone.Id, sourceBone.Id);
        }
    }

    private static AnimationFile.BoneInfo? GetUniqueSourceBone(
        IReadOnlyDictionary<string, AnimationFile.BoneInfo[]> sourceByRole,
        string role)
    {
        return sourceByRole.TryGetValue(role, out var candidates) &&
               candidates.Length == 1
            ? candidates[0]
            : null;
    }

    private static bool TryGetOuterFingerFallback(
        string role,
        out string fallbackRole)
    {
        if (role.StartsWith("middle_", StringComparison.Ordinal))
        {
            fallbackRole = "ring_" + role["middle_".Length..];
            return true;
        }

        if (role.StartsWith("pinky_", StringComparison.Ordinal))
        {
            fallbackRole = "ring_" + role["pinky_".Length..];
            return true;
        }

        fallbackRole = "";
        return false;
    }

    private static AnimationFile.BoneInfo? FindHips(AnimationFile skeleton)
    {
        var upperLegs = skeleton.Bones
            .Where(bone => GetAliasRole(bone.Name)?.StartsWith(
                "upperleg_",
                StringComparison.Ordinal) == true)
            .ToArray();
        if (upperLegs.Length != 0)
        {
            var parentId = upperLegs[0].ParentId;
            if (upperLegs.All(bone => bone.ParentId == parentId))
                return skeleton.Bones.SingleOrDefault(bone => bone.Id == parentId);
        }

        string[] preferredNames = ["bip", "hips", "bnhips", "pelvis", "root"];
        foreach (var preferredName in preferredNames)
        {
            var match = skeleton.Bones.SingleOrDefault(bone =>
                NormalizeName(bone.Name) == preferredName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static AnimationFile.BoneInfo FindTopAncestor(
        AnimationFile skeleton,
        AnimationFile.BoneInfo bone)
    {
        var byId = skeleton.Bones.ToDictionary(item => item.Id);
        var current = bone;
        while (current.ParentId != AnimationFile.BoneIndexNoParent &&
               byId.TryGetValue(current.ParentId, out var parent))
        {
            current = parent;
        }

        return current;
    }

    private static int GetBoneDepth(
        AnimationFile skeleton,
        AnimationFile.BoneInfo bone)
    {
        var byId = skeleton.Bones.ToDictionary(item => item.Id);
        var depth = 0;
        var parentId = bone.ParentId;
        while (parentId != AnimationFile.BoneIndexNoParent &&
               byId.TryGetValue(parentId, out var parent))
        {
            depth++;
            parentId = parent.ParentId;
        }

        return depth;
    }

    private static void AddMapping(
        IDictionary<int, int> mappings,
        int targetBoneIndex,
        int sourceBoneIndex)
    {
        mappings.TryAdd(targetBoneIndex, sourceBoneIndex);
    }

    private static string? GetAliasRole(string boneName)
    {
        return Aliases.TryGetValue(NormalizeName(boneName), out var role)
            ? role
            : null;
    }

    internal static bool IsRigSpecificDeformationBone(string boneName) =>
        IsRigSpecificDeformationRole(GetAliasRole(boneName));

    private static bool IsRigSpecificDeformationRole(string? role) =>
        role is "upperarmtwist_left" or
            "upperarmtwist_right" or
            "lowerarmtwist_left" or
            "lowerarmtwist_right";

    private static string NormalizeName(string name)
    {
        return new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static IReadOnlyDictionary<string, string> CreateAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        AddAliasGroup(aliases, "head", "head", "head_0", "bn_head");
        AddAliasGroup(aliases, "jaw", "jaw", "jaw_0", "jaw_01", "bn_jaw");

        AddSideAliases(
            aliases,
            "left",
            "clavicle",
            "clav_left",
            "clavicle_l",
            "shoulder_left",
            "bn_leftshoulder");
        AddSideAliases(
            aliases,
            "right",
            "clavicle",
            "clav_right",
            "clavicle_r",
            "shoulder_right",
            "bn_rightshoulder");
        AddSideAliases(aliases, "left", "upperarm", "upperarm_left", "upperarm_l", "arm_left_0", "bn_leftarm");
        AddSideAliases(aliases, "right", "upperarm", "upperarm_right", "upperarm_r", "arm_right_0", "bn_rightarm");
        AddSideAliases(aliases, "left", "lowerarm", "lowerarm_left", "lowerarm_l", "forearm_left", "arm_left_1", "bn_leftforearm");
        AddSideAliases(aliases, "right", "lowerarm", "lowerarm_right", "lowerarm_r", "forearm_right", "arm_right_1", "bn_rightforearm");
        AddSideAliases(aliases, "left", "hand", "hand_left", "hand_l", "arm_left_2", "bn_lefthand");
        AddSideAliases(aliases, "right", "hand", "hand_right", "hand_r", "arm_right_2", "bn_righthand");

        AddSideAliases(aliases, "left", "upperleg", "upperleg_left", "thigh_l", "leg_left_0", "bn_leftupleg");
        AddSideAliases(aliases, "right", "upperleg", "upperleg_right", "thigh_r", "leg_right_0", "bn_rightupleg");
        AddSideAliases(aliases, "left", "lowerleg", "lowerleg_left", "calf_l", "leg_left_1", "bn_leftleg");
        AddSideAliases(aliases, "right", "lowerleg", "lowerleg_right", "calf_r", "leg_right_1", "bn_rightleg");
        AddSideAliases(aliases, "left", "foot", "foot_left", "foot_l", "leg_left_2", "bn_leftfoot");
        AddSideAliases(aliases, "right", "foot", "foot_right", "foot_r", "leg_right_2", "bn_rightfoot");
        AddSideAliases(aliases, "left", "toe", "toe_left_0", "ball_l", "bn_lefttoebase");
        AddSideAliases(aliases, "right", "toe", "toe_right_0", "ball_r", "bn_righttoebase");

        AddSideAliases(aliases, "left", "upperarmtwist", "upperarm_roll_left_0", "upperarm_twist_01_l", "bn_leftarmroll");
        AddSideAliases(aliases, "right", "upperarmtwist", "upperarm_roll_right_0", "upperarm_twist_01_r", "bn_rightarmroll");
        AddSideAliases(aliases, "left", "lowerarmtwist", "lowerarm_roll_left_0", "lowerarm_twist_01_l", "bn_leftforearmroll");
        AddSideAliases(aliases, "right", "lowerarmtwist", "lowerarm_roll_right_0", "lowerarm_twist_01_r", "bn_rightforearmroll");
        AddSideAliases(aliases, "left", "eye", "eye_left", "eye_l_01", "bn_lefteye");
        AddSideAliases(aliases, "right", "eye", "eye_right", "eye_r_01", "bn_righteye");

        AddFingerAliases(aliases, "thumb", "left", "l");
        AddFingerAliases(aliases, "thumb", "right", "r");
        AddFingerAliases(aliases, "index", "left", "l");
        AddFingerAliases(aliases, "index", "right", "r");
        AddFingerAliases(aliases, "middle", "left", "l");
        AddFingerAliases(aliases, "middle", "right", "r");
        AddFingerAliases(aliases, "ring", "left", "l");
        AddFingerAliases(aliases, "ring", "right", "r");
        AddFingerAliases(aliases, "pinky", "left", "l");
        AddFingerAliases(aliases, "pinky", "right", "r");

        return aliases;
    }

    private static void AddSideAliases(
        IDictionary<string, string> aliases,
        string side,
        string role,
        params string[] names)
    {
        AddAliasGroup(aliases, $"{role}_{side}", names);
    }

    private static void AddFingerAliases(
        IDictionary<string, string> aliases,
        string finger,
        string side,
        string shortSide)
    {
        for (var segment = 0; segment < 3; segment++)
        {
            AddAliasGroup(
                aliases,
                $"{finger}_{side}_{segment}",
                $"{finger}_{side}_{segment}",
                $"{finger}_{segment + 1:00}_{shortSide}",
                $"finger_{finger}_{side}_{segment}",
                $"bn_{side}hand{finger}{segment + 1}");
        }
    }

    private static void AddAliasGroup(
        IDictionary<string, string> aliases,
        string role,
        params string[] names)
    {
        foreach (var name in names)
            aliases[NormalizeName(name)] = role;
    }
}
