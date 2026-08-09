using System.IO;
using System.Numerics;
using Editors.ImportExport.Importing;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

internal static class HumanoidScaleCalculator
{
    private static readonly IReadOnlyList<HashSet<string>> s_headAliases =
    [
        CreateAliasSet("head", "head_0", "bip_head", "bn_head"),
    ];

    private static readonly IReadOnlyList<HashSet<string>> s_leftFootAliases =
    [
        CreateAliasSet(
            "toe_left_0", "toe_left", "left_toe", "left_toe_base", "toe_l", "l_toe",
            "bn_lefttoebase", "ball_l", "ball_left", "left_ball", "l_ball"),
        CreateAliasSet(
            "foot_left", "left_foot", "foot_l", "l_foot", "bn_leftfoot", "leg_left_2",
            "ankle_left", "left_ankle"),
    ];

    private static readonly IReadOnlyList<HashSet<string>> s_rightFootAliases =
    [
        CreateAliasSet(
            "toe_right_0", "toe_right", "right_toe", "right_toe_base", "toe_r", "r_toe",
            "bn_righttoebase", "ball_r", "ball_right", "right_ball", "r_ball"),
        CreateAliasSet(
            "foot_right", "right_foot", "foot_r", "r_foot", "bn_rightfoot", "leg_right_2",
            "ankle_right", "right_ankle"),
    ];

    public static HumanoidScaleImportSummary Calculate(
        AnimationFile sourceSkeleton,
        Func<AnimationFile> getReferenceSkeleton)
    {
        var sourceMeasurement = Measure(sourceSkeleton, "源骨架");
        if (sourceMeasurement.Status == MeasurementStatus.NotHumanoid)
        {
            return new HumanoidScaleImportSummary(
                false,
                null,
                null,
                1,
                LocalizationManager.Instance.Get(
                    "GltfImporter.ScaleReason.NonHumanoid"));
        }

        var sourceHeight = sourceMeasurement.Height!.Value;
        var referenceMeasurement = Measure(getReferenceSkeleton(), "humanoid01");
        if (referenceMeasurement.Status == MeasurementStatus.NotHumanoid)
        {
            throw new InvalidDataException(LocalizationManager.Instance.Get(
                "GltfImporter.Error.InvalidHumanoidReference"));
        }

        var referenceHeight = referenceMeasurement.Height!.Value;
        var scaleFactor = referenceHeight / sourceHeight;
        if (!float.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            throw new InvalidDataException(LocalizationManager.Instance.Get(
                "GltfImporter.Error.InvalidHumanoidHeight"));
        }

        return new HumanoidScaleImportSummary(
            true,
            sourceHeight,
            referenceHeight,
            scaleFactor,
            LocalizationManager.Instance.Get("GltfImporter.ScaleReason.Applied"));
    }

    public static void ScaleTranslations(AnimationFile animationFile, float scaleFactor)
    {
        if (Math.Abs(scaleFactor - 1) <= 0.000001f)
            return;

        foreach (var part in animationFile.AnimationParts)
        {
            if (part.StaticFrame != null)
                ScaleFrame(part.StaticFrame, scaleFactor);
            foreach (var frame in part.DynamicFrames)
                ScaleFrame(frame, scaleFactor);
        }
    }

    private static HumanoidMeasurement Measure(
        AnimationFile skeleton,
        string skeletonLabel)
    {
        var pose = CreatePose(skeleton);
        var head = FindAnchor(pose, "头部", s_headAliases);
        var leftFoot = FindAnchor(pose, "左脚", s_leftFootAliases);
        var rightFoot = FindAnchor(pose, "右脚", s_rightFootAliases);
        var anchors = new[] { head, leftFoot, rightFoot };
        var matchedCount = anchors.Count(anchor => anchor.Index != null);
        if (matchedCount == 0)
            return new HumanoidMeasurement(MeasurementStatus.NotHumanoid, null);

        if (anchors.Any(anchor => anchor.IsAmbiguous))
        {
            var ambiguousRoles = string.Join(
                "、",
                anchors.Where(anchor => anchor.IsAmbiguous).Select(anchor => anchor.Role));
            throw new InvalidDataException(LocalizationManager.Instance.GetFormat(
                "GltfImporter.Error.AmbiguousHumanoidAnchors",
                skeletonLabel,
                ambiguousRoles));
        }

        if (matchedCount != anchors.Length)
        {
            var missingRoles = string.Join(
                "、",
                anchors.Where(anchor => anchor.Index == null).Select(anchor => anchor.Role));
            throw new InvalidDataException(LocalizationManager.Instance.GetFormat(
                "GltfImporter.Error.PartialHumanoidAnchors",
                skeletonLabel,
                missingRoles));
        }

        var anchorIndexes = anchors.Select(anchor => anchor.Index!.Value).ToArray();
        var commonAncestors = GetAncestors(pose, anchorIndexes[0]);
        commonAncestors.IntersectWith(GetAncestors(pose, anchorIndexes[1]));
        commonAncestors.IntersectWith(GetAncestors(pose, anchorIndexes[2]));
        if (commonAncestors.Count == 0)
        {
            throw new InvalidDataException(LocalizationManager.Instance.GetFormat(
                "GltfImporter.Error.HumanoidAnchorHierarchy",
                skeletonLabel));
        }

        var feetMidpoint = (pose[anchorIndexes[1]].WorldPosition +
                            pose[anchorIndexes[2]].WorldPosition) * 0.5f;
        var height = Vector3.Distance(pose[anchorIndexes[0]].WorldPosition, feetMidpoint);
        if (!float.IsFinite(height) || height <= 0.000001f)
        {
            throw new InvalidDataException(LocalizationManager.Instance.GetFormat(
                "GltfImporter.Error.InvalidHumanoidHeightForSkeleton",
                skeletonLabel));
        }

        return new HumanoidMeasurement(MeasurementStatus.Measured, height);
    }

    private static IReadOnlyList<PoseBone> CreatePose(AnimationFile skeleton)
    {
        if (skeleton.Bones == null || skeleton.Bones.Length == 0 ||
            skeleton.AnimationParts.Count == 0)
        {
            throw new InvalidDataException(LocalizationManager.Instance.Get(
                "GltfImporter.Error.InvalidHumanoidReference"));
        }

        var part = skeleton.AnimationParts[0];
        var dynamicFrame = part.DynamicFrames.FirstOrDefault();
        var localMatrices = new Matrix4x4[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            var translation = GetTranslation(part, dynamicFrame, boneIndex);
            var rotation = GetRotation(part, dynamicFrame, boneIndex);
            localMatrices[boneIndex] =
                Matrix4x4.CreateFromQuaternion(rotation) *
                Matrix4x4.CreateTranslation(translation);
        }

        var worldMatrices = new Matrix4x4[skeleton.Bones.Length];
        var visitStates = new byte[skeleton.Bones.Length];
        Matrix4x4 ResolveWorld(int boneIndex)
        {
            if (visitStates[boneIndex] == 2)
                return worldMatrices[boneIndex];
            if (visitStates[boneIndex] == 1)
                throw new InvalidDataException(LocalizationManager.Instance.Get(
                    "GltfImporter.Error.InvalidHumanoidReference"));

            visitStates[boneIndex] = 1;
            var parentIndex = skeleton.Bones[boneIndex].ParentId;
            if (parentIndex < AnimationFile.BoneIndexNoParent ||
                parentIndex >= skeleton.Bones.Length)
            {
                throw new InvalidDataException(LocalizationManager.Instance.Get(
                    "GltfImporter.Error.InvalidHumanoidReference"));
            }

            worldMatrices[boneIndex] = parentIndex == AnimationFile.BoneIndexNoParent
                ? localMatrices[boneIndex]
                : localMatrices[boneIndex] * ResolveWorld(parentIndex);
            visitStates[boneIndex] = 2;
            return worldMatrices[boneIndex];
        }

        var pose = new PoseBone[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            var world = ResolveWorld(boneIndex);
            pose[boneIndex] = new PoseBone(
                skeleton.Bones[boneIndex].Name,
                skeleton.Bones[boneIndex].ParentId,
                new Vector3(world.M41, world.M42, world.M43));
        }

        return pose;
    }

    private static Vector3 GetTranslation(
        AnimationFile.AnimationPart part,
        AnimationFile.Frame? dynamicFrame,
        int boneIndex)
    {
        if (boneIndex < part.TranslationMappings.Count)
        {
            var mapping = part.TranslationMappings[boneIndex];
            if (mapping.IsDynamic && dynamicFrame != null &&
                mapping.Id >= 0 && mapping.Id < dynamicFrame.Transforms.Count)
            {
                return ToVector3(dynamicFrame.Transforms[mapping.Id]);
            }
            if (mapping.IsStatic && part.StaticFrame != null &&
                mapping.Id >= 0 && mapping.Id < part.StaticFrame.Transforms.Count)
            {
                return ToVector3(part.StaticFrame.Transforms[mapping.Id]);
            }
        }
        if (dynamicFrame?.Transforms.Count > boneIndex)
            return ToVector3(dynamicFrame.Transforms[boneIndex]);

        return Vector3.Zero;
    }

    private static Quaternion GetRotation(
        AnimationFile.AnimationPart part,
        AnimationFile.Frame? dynamicFrame,
        int boneIndex)
    {
        RmvVector4? value = null;
        if (boneIndex < part.RotationMappings.Count)
        {
            var mapping = part.RotationMappings[boneIndex];
            if (mapping.IsDynamic && dynamicFrame != null &&
                mapping.Id >= 0 && mapping.Id < dynamicFrame.Quaternion.Count)
            {
                value = dynamicFrame.Quaternion[mapping.Id];
            }
            else if (mapping.IsStatic && part.StaticFrame != null &&
                     mapping.Id >= 0 && mapping.Id < part.StaticFrame.Quaternion.Count)
            {
                value = part.StaticFrame.Quaternion[mapping.Id];
            }
        }
        if (value == null && dynamicFrame?.Quaternion.Count > boneIndex)
            value = dynamicFrame.Quaternion[boneIndex];
        if (value == null)
            return Quaternion.Identity;

        var quaternion = new Quaternion(value.Value.X, value.Value.Y, value.Value.Z, value.Value.W);
        return quaternion.LengthSquared() <= float.Epsilon
            ? Quaternion.Identity
            : Quaternion.Normalize(quaternion);
    }

    private static AnchorMatch FindAnchor(
        IReadOnlyList<PoseBone> pose,
        string role,
        IReadOnlyList<HashSet<string>> aliasPriority)
    {
        foreach (var aliases in aliasPriority)
        {
            var matches = pose
                .Select((bone, index) => (bone, index))
                .Where(item => aliases.Contains(NormalizeName(item.bone.Name)))
                .Select(item => item.index)
                .ToList();
            if (matches.Count != 0)
                return new AnchorMatch(role, matches.Count == 1 ? matches[0] : null, matches.Count > 1);
        }

        return new AnchorMatch(role, null, false);
    }

    private static HashSet<int> GetAncestors(IReadOnlyList<PoseBone> pose, int boneIndex)
    {
        var ancestors = new HashSet<int>();
        while (boneIndex != AnimationFile.BoneIndexNoParent)
        {
            if (!ancestors.Add(boneIndex))
                break;
            boneIndex = pose[boneIndex].ParentId;
        }
        return ancestors;
    }

    private static HashSet<string> CreateAliasSet(params string[] aliases) =>
        aliases.Select(NormalizeName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeName(string name)
    {
        var leafName = name
            .Split([':', '|'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? name;
        return new string(leafName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static Vector3 ToVector3(RmvVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static void ScaleFrame(AnimationFile.Frame frame, float scaleFactor)
    {
        for (var transformIndex = 0; transformIndex < frame.Transforms.Count; transformIndex++)
        {
            var transform = frame.Transforms[transformIndex];
            frame.Transforms[transformIndex] = new RmvVector3(
                transform.X * scaleFactor,
                transform.Y * scaleFactor,
                transform.Z * scaleFactor);
        }
    }

    private enum MeasurementStatus
    {
        NotHumanoid,
        Measured,
    }

    private sealed record HumanoidMeasurement(
        MeasurementStatus Status,
        float? Height);

    private sealed record AnchorMatch(
        string Role,
        int? Index,
        bool IsAmbiguous);

    private sealed record PoseBone(
        string Name,
        int ParentId,
        Vector3 WorldPosition);
}
