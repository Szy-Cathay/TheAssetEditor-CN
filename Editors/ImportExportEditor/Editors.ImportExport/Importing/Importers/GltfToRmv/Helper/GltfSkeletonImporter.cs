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

        return Build(skin, skeletonName, mirrorMesh, bakeAncestorTransform: false);
    }

    internal static AnimationFile BuildExternal(
        ModelRoot modelRoot,
        string inputFile,
        string? skeletonName,
        bool mirrorMesh)
    {
        ArgumentNullException.ThrowIfNull(modelRoot);
        var skins = modelRoot.LogicalSkins.ToList();
        if (skins.Count == 0)
            throw new InvalidDataException("glTF 不包含可导入的蒙皮骨架。");

        var logicalSkeletons = skins
            .GroupBy(CreateSkeletonSignature, StringComparer.Ordinal)
            .ToList();
        if (logicalSkeletons.Count != 1)
        {
            throw new InvalidDataException(
                "glTF 包含多套逻辑骨架；不同关节集合或父子层级不能自动合并。");
        }

        foreach (var equivalentSkin in logicalSkeletons[0].Skip(1))
            ValidateExternalSkin(equivalentSkin);

        var skin = logicalSkeletons[0].First();
        var resolvedName = string.IsNullOrWhiteSpace(skeletonName)
            ? GetDefaultSkeletonName(skin, inputFile)
            : skeletonName.Trim();
        if (resolvedName is "." or ".." ||
            !string.Equals(Path.GetFileName(resolvedName), resolvedName, StringComparison.Ordinal) ||
            resolvedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                "新骨架名称不能包含路径或无效的文件名字符。");
        }

        return Build(skin, resolvedName, mirrorMesh, bakeAncestorTransform: true);
    }

    private static void ValidateExternalSkin(Skin skin)
    {
        var joints = Enumerable.Range(0, skin.JointsCount)
            .Select(index => skin.GetJoint(index).Joint)
            .ToList();
        var jointSet = joints.ToHashSet();
        foreach (var joint in joints)
            ValidateBoneLocalTransform(joint, FindJointParent(joint, jointSet));

        GetBindWorldMatrices(skin);
    }

    private static AnimationFile Build(
        Skin skin,
        string skeletonName,
        bool mirrorMesh,
        bool bakeAncestorTransform)
    {
        if (string.IsNullOrWhiteSpace(skeletonName))
            throw new InvalidDataException("glTF 缺少骨架名称，无法创建游戏骨架。");
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
        var bindWorldMatrices = bakeAncestorTransform
            ? GetBindWorldMatrices(skin)
            : null;
        var scaleFreeBindWorldMatrices = bindWorldMatrices?
            .ToDictionary(item => item.Key, item => RemoveScale(item.Key, item.Value));
        var frame = new AnimationFile.Frame();
        var bones = new AnimationFile.BoneInfo[orderedJoints.Count];

        for (var boneIndex = 0; boneIndex < orderedJoints.Count; boneIndex++)
        {
            var joint = orderedJoints[boneIndex];
            var parent = FindJointParent(joint, jointSet);
            Matrix4x4 localMatrix;
            if (bakeAncestorTransform)
            {
                ValidateBoneLocalTransform(joint, parent);
                localMatrix = scaleFreeBindWorldMatrices![joint];
                if (parent != null)
                {
                    if (!Matrix4x4.Invert(
                            scaleFreeBindWorldMatrices[parent],
                            out var inverseParent))
                    {
                        throw new InvalidDataException(
                            $"glTF 骨骼“{parent.Name}”的绑定变换不可逆。");
                    }

                    localMatrix *= inverseParent;
                }
            }
            else
            {
                localMatrix = joint.WorldMatrix;
                if (parent != null)
                {
                    if (!Matrix4x4.Invert(parent.WorldMatrix, out var inverseParent))
                        throw new InvalidDataException($"glTF 骨骼“{parent.Name}”的绑定变换不可逆。");

                    localMatrix *= inverseParent;
                }
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

    private static Dictionary<Node, Matrix4x4> GetBindWorldMatrices(Skin skin)
    {
        var result = new Dictionary<Node, Matrix4x4>();
        for (var jointIndex = 0; jointIndex < skin.JointsCount; jointIndex++)
        {
            var joint = skin.GetJoint(jointIndex);
            if (!Matrix4x4.Invert(joint.InverseBindMatrix, out var bindWorldMatrix))
            {
                throw new InvalidDataException(
                    $"glTF 骨骼“{joint.Joint.Name}”的逆绑定矩阵不可逆。");
            }
            if (HasShear(bindWorldMatrix))
            {
                throw new InvalidDataException(
                    $"glTF 骨骼“{joint.Joint.Name}”的绑定变换包含剪切，ANIM 骨架无法安全保存。");
            }
            if (!Matrix4x4.Decompose(bindWorldMatrix, out _, out _, out _))
            {
                throw new InvalidDataException(
                    $"glTF 骨骼“{joint.Joint.Name}”的绑定变换无法分解。");
            }

            result[joint.Joint] = bindWorldMatrix;
        }

        return result;
    }

    private static Matrix4x4 RemoveScale(Node joint, Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out _, out var rotation, out var translation))
            throw new InvalidDataException($"glTF 骨骼“{joint.Name}”的绑定变换无法分解。");

        return Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static void ValidateBoneLocalTransform(Node joint, Node? parentJoint)
    {
        var localMatrix = joint.WorldMatrix;
        var visualParent = parentJoint ?? joint.VisualParent;
        if (visualParent != null)
        {
            if (!Matrix4x4.Invert(visualParent.WorldMatrix, out var inverseParent))
            {
                throw new InvalidDataException(
                    $"glTF 骨骼“{joint.Name}”的父级变换不可逆。");
            }

            localMatrix *= inverseParent;
        }

        if (HasShear(localMatrix))
        {
            throw new InvalidDataException(
                $"glTF 骨骼“{joint.Name}”包含剪切；ANIM 骨架无法安全保存。");
        }
        if (!Matrix4x4.Invert(localMatrix, out _))
            throw new InvalidDataException($"glTF 骨骼“{joint.Name}”的局部绑定变换不可逆。");
        if (!Matrix4x4.Decompose(localMatrix, out var scale, out _, out _))
            throw new InvalidDataException($"glTF 骨骼“{joint.Name}”的局部绑定变换无法分解。");
        if (!IsUnitScale(scale))
        {
            throw new InvalidDataException(
                $"glTF 骨骼“{joint.Name}”包含缩放；ANIM 骨架无法安全保存骨骼缩放。");
        }
    }

    private static bool HasShear(Matrix4x4 matrix)
    {
        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23);
        var z = new Vector3(matrix.M31, matrix.M32, matrix.M33);
        if (x.LengthSquared() <= 0 || y.LengthSquared() <= 0 || z.LengthSquared() <= 0)
            return false;

        x = Vector3.Normalize(x);
        y = Vector3.Normalize(y);
        z = Vector3.Normalize(z);
        return Math.Abs(Vector3.Dot(x, y)) > 0.0001f ||
               Math.Abs(Vector3.Dot(x, z)) > 0.0001f ||
               Math.Abs(Vector3.Dot(y, z)) > 0.0001f;
    }

    private static string CreateSkeletonSignature(Skin skin)
    {
        if (skin.JointsCount == 0)
            throw new InvalidDataException("glTF 包含没有关节的蒙皮，无法创建游戏骨架。");

        var joints = Enumerable.Range(0, skin.JointsCount)
            .Select(index => skin.GetJoint(index).Joint)
            .ToList();
        if (joints.Any(joint => string.IsNullOrWhiteSpace(joint.Name)))
            throw new InvalidDataException("glTF 骨架包含未命名骨骼，无法安全导入。");
        if (joints.Select(joint => joint.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            joints.Count)
        {
            throw new InvalidDataException("glTF 骨架包含忽略大小写后重名的骨骼，无法安全导入。");
        }

        var jointSet = joints.ToHashSet();
        OrderParentsBeforeChildren(joints, jointSet);
        return string.Join(
            "|",
            joints
                .Select(joint =>
                {
                    var parent = FindJointParent(joint, jointSet);
                    return $"{joint.Name.ToLowerInvariant()}>{parent?.Name.ToLowerInvariant() ?? "<root>"}";
                })
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string GetDefaultSkeletonName(Skin skin, string inputFile)
    {
        if (!string.IsNullOrWhiteSpace(skin.Name))
            return skin.Name;
        if (!string.IsNullOrWhiteSpace(skin.Skeleton?.Name))
            return skin.Skeleton.Name;

        return Path.GetFileNameWithoutExtension(inputFile);
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
