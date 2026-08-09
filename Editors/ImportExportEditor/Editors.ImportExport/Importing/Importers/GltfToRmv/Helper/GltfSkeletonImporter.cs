using System.IO;
using System.Numerics;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using SharpGLTF.Schema2;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

public static class GltfSkeletonImporter
{
    public static AnimationFile Build(
        ModelRoot modelRoot,
        string skeletonName,
        bool mirrorMesh)
    {
        ArgumentNullException.ThrowIfNull(modelRoot);
        if (string.IsNullOrWhiteSpace(skeletonName))
            throw new InvalidDataException("glTF 缺少骨架名称，无法创建游戏骨架。");

        var skin = modelRoot.LogicalSkins
            .OrderByDescending(candidate => candidate.JointsCount)
            .FirstOrDefault();
        if (skin == null || skin.JointsCount == 0)
            throw new InvalidDataException("glTF 不包含可导入的蒙皮骨架。");
        if (skin.JointsCount > 256)
            throw new InvalidDataException("glTF 骨架超过 RMV2 可表示的 256 根骨骼限制。");

        var sourceJoints = Enumerable.Range(0, skin.JointsCount)
            .Select(index => skin.GetJoint(index).Joint)
            .ToList();
        if (sourceJoints.Any(joint => string.IsNullOrWhiteSpace(joint.Name)))
            throw new InvalidDataException("glTF 骨架包含未命名骨骼，无法安全导入。");
        if (sourceJoints.Select(joint => joint.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            sourceJoints.Count)
        {
            throw new InvalidDataException("glTF 骨架包含重名骨骼，无法安全映射到 RMV2。");
        }

        var jointSet = sourceJoints.ToHashSet();
        var orderedJoints = OrderParentsBeforeChildren(sourceJoints, jointSet);
        var boneIndexes = orderedJoints
            .Select((joint, index) => (joint, index))
            .ToDictionary(item => item.joint, item => item.index);
        var frame = new AnimationFile.Frame();
        var bones = new AnimationFile.BoneInfo[orderedJoints.Count];

        for (var boneIndex = 0; boneIndex < orderedJoints.Count; boneIndex++)
        {
            var joint = orderedJoints[boneIndex];
            var parent = FindJointParent(joint, jointSet);
            var localMatrix = joint.WorldMatrix;
            if (parent != null)
            {
                if (!Matrix4x4.Invert(parent.WorldMatrix, out var inverseParent))
                    throw new InvalidDataException($"glTF 骨骼“{parent.Name}”的绑定变换不可逆。");

                localMatrix *= inverseParent;
            }

            if (!Matrix4x4.Decompose(localMatrix, out var scale, out var rotation, out var translation))
                throw new InvalidDataException($"glTF 骨骼“{joint.Name}”的绑定变换无法分解。");
            if (!IsUnitScale(scale))
                throw new InvalidDataException($"glTF 骨骼“{joint.Name}”包含缩放；ANIM 骨架无法安全保存骨骼缩放。");

            rotation = Quaternion.Normalize(rotation);
            bones[boneIndex] = new AnimationFile.BoneInfo
            {
                Id = boneIndex,
                Name = joint.Name,
                ParentId = parent == null
                    ? AnimationFile.BoneIndexNoParent
                    : boneIndexes[parent],
            };
            frame.Transforms.Add(new RmvVector3(
                mirrorMesh ? -translation.X : translation.X,
                translation.Y,
                translation.Z));
            frame.Quaternion.Add(new RmvVector4(
                rotation.X,
                mirrorMesh ? -rotation.Y : rotation.Y,
                mirrorMesh ? -rotation.Z : rotation.Z,
                rotation.W));
        }

        var part = new AnimationFile.AnimationPart();
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            part.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
            part.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(boneIndex));
        }
        part.DynamicFrames.Add(frame);
        part.DynamicFrames.Add(CloneFrame(frame));

        return new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                FrameRate = 20,
                SkeletonName = skeletonName,
                AnimationTotalPlayTimeInSec = 0.1f,
            },
            Bones = bones,
            AnimationParts = [part],
        };
    }

    private static List<Node> OrderParentsBeforeChildren(
        IReadOnlyList<Node> joints,
        HashSet<Node> jointSet)
    {
        var ordered = new List<Node>(joints.Count);
        var visitStates = new Dictionary<Node, byte>();

        void Visit(Node joint)
        {
            if (visitStates.TryGetValue(joint, out var state))
            {
                if (state == 2)
                    return;
                if (state == 1)
                    throw new InvalidDataException("glTF 骨架包含循环父级关系。");
            }

            visitStates[joint] = 1;
            var parent = FindJointParent(joint, jointSet);
            if (parent != null)
                Visit(parent);
            visitStates[joint] = 2;
            ordered.Add(joint);
        }

        foreach (var joint in joints)
            Visit(joint);

        return ordered;
    }

    private static Node? FindJointParent(Node joint, HashSet<Node> jointSet)
    {
        var parent = joint.VisualParent;
        while (parent != null && !jointSet.Contains(parent))
            parent = parent.VisualParent;

        return parent;
    }

    private static bool IsUnitScale(Vector3 scale) =>
        Math.Abs(scale.X - 1) <= 0.0001f &&
        Math.Abs(scale.Y - 1) <= 0.0001f &&
        Math.Abs(scale.Z - 1) <= 0.0001f;

    private static AnimationFile.Frame CloneFrame(AnimationFile.Frame frame) => new()
    {
        Transforms = frame.Transforms.ToList(),
        Quaternion = frame.Quaternion.ToList(),
    };
}
