using System.IO;
using System.Numerics;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using SharpGLTF.Schema2;
using static Shared.GameFormats.Animation.AnimationFile;
using Xna = Microsoft.Xna.Framework;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

public record AnimationBuilderSettings(
    ModelRoot ModelRoot,
    string SkeletonName,
    float KeysPerSecond,
    PackFileContainer PackFileContainer,
    string PackPath,
    bool AutoDetectKeysPerSecond = true);

public class AnimationBuilder
{
    public static AnimationFile Build(
        AnimationBuilderSettings settings,
        AnimationFile skeletonAnimFile,
        Animation animation)
    {
        if (!float.IsFinite(settings.KeysPerSecond) || settings.KeysPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings.KeysPerSecond), "动画采样率必须大于 0。");
        if (skeletonAnimFile.Bones.Length == 0)
            throw new InvalidDataException("目标游戏骨架不包含骨骼。");
        if (skeletonAnimFile.AnimationParts.Count == 0 ||
            skeletonAnimFile.AnimationParts[0].DynamicFrames.Count == 0)
        {
            throw new InvalidDataException("目标游戏骨架缺少绑定姿势，无法补全 glTF 的部分动画通道。");
        }

        var keysPerSecond = settings.AutoDetectKeysPerSecond
            ? DetectSamplingRate(settings.ModelRoot, animation) ?? settings.KeysPerSecond
            : settings.KeysPerSecond;
        var keyInterval = 1.0f / keysPerSecond;
        var intervalCount = Math.Max(
            0,
            (int)Math.Round(animation.Duration * keysPerSecond, MidpointRounding.AwayFromZero));
        var keyCount = intervalCount + 1;

        var newAnimFile = new AnimationFile
        {
            Header = new AnimationHeader
            {
                Version = 7,
                FrameRate = keysPerSecond,
                SkeletonName = settings.SkeletonName,
                AnimationTotalPlayTimeInSec = keyCount * keyInterval,
            },
            Bones = skeletonAnimFile.Bones,
            AnimationParts = [new AnimationPart()],
        };

        var part = newAnimFile.AnimationParts[0];
        part.DynamicFrames = [];
        DoQuantization(skeletonAnimFile, part);
        var skinRetargeter = SkinAnimationRetargeter.TryCreate(
            settings.ModelRoot,
            skeletonAnimFile);

        for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
        {
            var keyTime = keyIndex * keyInterval;
            var frame = new Frame();
            FillFrame(
                settings.ModelRoot,
                animation,
                skeletonAnimFile,
                skinRetargeter,
                keyTime,
                frame);
            part.DynamicFrames.Add(frame);
        }

        return newAnimFile;
    }

    private static float? DetectSamplingRate(ModelRoot modelRoot, Animation animation)
    {
        IReadOnlyList<float>? mostDetailedKeyTimes = null;

        void Consider<T>(IAnimationSampler<T> sampler)
        {
            var keyTimes = sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE
                ? sampler.GetCubicKeys().Select(key => key.Key).ToArray()
                : sampler.GetLinearKeys().Select(key => key.Key).ToArray();
            if (mostDetailedKeyTimes == null || keyTimes.Length > mostDetailedKeyTimes.Count)
                mostDetailedKeyTimes = keyTimes;
        }

        foreach (var node in modelRoot.LogicalNodes)
        {
            var rotationChannel = animation.FindRotationChannel(node);
            if (rotationChannel != null)
                Consider(rotationChannel.GetRotationSampler());

            var translationChannel = animation.FindTranslationChannel(node);
            if (translationChannel != null)
                Consider(translationChannel.GetTranslationSampler());

            var scaleChannel = animation.FindScaleChannel(node);
            if (scaleChannel != null)
                Consider(scaleChannel.GetScaleSampler());
        }

        if (mostDetailedKeyTimes == null || mostDetailedKeyTimes.Count < 3)
            return null;

        var keyInterval = mostDetailedKeyTimes[1] - mostDetailedKeyTimes[0];
        if (!float.IsFinite(keyInterval) || keyInterval <= 0)
            return null;

        var intervalTolerance = Math.Max(0.00001f, keyInterval * 0.001f);
        for (var keyIndex = 2; keyIndex < mostDetailedKeyTimes.Count; keyIndex++)
        {
            var interval = mostDetailedKeyTimes[keyIndex] - mostDetailedKeyTimes[keyIndex - 1];
            if (Math.Abs(interval - keyInterval) > intervalTolerance)
                return null;
        }

        var detectedRate = 1.0f / keyInterval;
        if (!float.IsFinite(detectedRate) || detectedRate < 1.0f || detectedRate > 240.0f)
            return null;

        var roundedRate = MathF.Round(detectedRate);
        return Math.Abs(detectedRate - roundedRate) <= 0.01f
            ? roundedRate
            : detectedRate;
    }

    private static void DoQuantization(AnimationFile skeletonAnimFile, AnimationPart part)
    {
        for (var boneIndex = 0; boneIndex < skeletonAnimFile.Bones.Length; boneIndex++)
        {
            part.TranslationMappings.Add(new AnimationBoneMapping(boneIndex));
            part.RotationMappings.Add(new AnimationBoneMapping(boneIndex));
        }
    }

    private static void FillFrame(
        ModelRoot modelRoot,
        Animation animation,
        AnimationFile skeletonAnimFile,
        SkinAnimationRetargeter? skinRetargeter,
        float currentKeyTime,
        Frame frame)
    {
        if (skinRetargeter != null)
        {
            skinRetargeter.FillFrame(animation, currentKeyTime, frame);
            return;
        }

        var translations = new RmvVector3[skeletonAnimFile.Bones.Length];
        var quaternions = new RmvVector4[skeletonAnimFile.Bones.Length];
        var bindPose = skeletonAnimFile.AnimationParts[0].DynamicFrames[0];

        for (var boneIndex = 0; boneIndex < skeletonAnimFile.Bones.Length; boneIndex++)
        {
            var bone = skeletonAnimFile.Bones[boneIndex];
            var fallbackTranslation = bindPose.Transforms[boneIndex].ToVector3();
            var fallbackRotation = bindPose.Quaternion[boneIndex].ToQuaternion();
            var translation = GltfAnimationTrackSampler.SampleTranslation(
                modelRoot,
                animation,
                bone.Name,
                currentKeyTime,
                fallbackTranslation);
            var quaternion = GltfAnimationTrackSampler.SampleQuaternion(
                modelRoot,
                animation,
                bone.Name,
                currentKeyTime,
                fallbackRotation);

            translations[boneIndex] = new RmvVector3(translation);
            quaternions[boneIndex] = new RmvVector4(
                quaternion.X,
                quaternion.Y,
                quaternion.Z,
                quaternion.W);
        }

        frame.Transforms = translations.ToList();
        frame.Quaternion = quaternions.ToList();
    }

    private sealed class SkinAnimationRetargeter
    {
        private sealed record JointBinding(
            Node Joint,
            Matrix4x4 InverseBindMatrix);

        private readonly AnimationFile _skeleton;
        private readonly JointBinding?[] _jointBindings;
        private readonly Matrix4x4[] _targetBindLocal;
        private readonly Matrix4x4[] _targetBindWorld;

        private SkinAnimationRetargeter(
            AnimationFile skeleton,
            Skin skin)
        {
            _skeleton = skeleton;
            _jointBindings = new JointBinding?[skeleton.Bones.Length];
            _targetBindLocal = new Matrix4x4[skeleton.Bones.Length];
            _targetBindWorld = new Matrix4x4[skeleton.Bones.Length];

            var boneIndexesByName = skeleton.Bones
                .Select((bone, index) => (bone.Name, index))
                .ToDictionary(
                    item => item.Name,
                    item => item.index,
                    StringComparer.OrdinalIgnoreCase);
            for (var jointIndex = 0; jointIndex < skin.JointsCount; jointIndex++)
            {
                var joint = skin.GetJoint(jointIndex);
                if (boneIndexesByName.TryGetValue(joint.Joint.Name, out var boneIndex))
                {
                    _jointBindings[boneIndex] = new JointBinding(
                        joint.Joint,
                        joint.InverseBindMatrix);
                }
            }

            BuildTargetBindMatrices();
        }

        public static SkinAnimationRetargeter? TryCreate(
            ModelRoot modelRoot,
            AnimationFile skeleton)
        {
            var boneNames = new HashSet<string>(
                skeleton.Bones.Select(bone => bone.Name),
                StringComparer.OrdinalIgnoreCase);
            Skin? bestSkin = null;
            var bestMatchCount = 0;
            foreach (var skin in modelRoot.LogicalSkins)
            {
                var matchCount = Enumerable.Range(0, skin.JointsCount)
                    .Count(index => boneNames.Contains(skin.GetJoint(index).Joint.Name));
                if (matchCount > bestMatchCount)
                {
                    bestSkin = skin;
                    bestMatchCount = matchCount;
                }
            }

            return bestSkin == null
                ? null
                : new SkinAnimationRetargeter(skeleton, bestSkin);
        }

        public void FillFrame(
            Animation animation,
            float time,
            Frame frame)
        {
            var desiredWorld = new Matrix4x4[_skeleton.Bones.Length];
            var visitStates = new byte[_skeleton.Bones.Length];

            Matrix4x4 BuildDesiredWorld(int boneIndex)
            {
                if (visitStates[boneIndex] == 2)
                    return desiredWorld[boneIndex];
                if (visitStates[boneIndex] == 1)
                    throw new InvalidDataException(
                        $"目标游戏骨架在骨骼 {boneIndex} 处包含循环父级关系。");

                visitStates[boneIndex] = 1;
                var parentIndex = GetValidatedParentIndex(boneIndex);
                var parentWorld = parentIndex < 0
                    ? Matrix4x4.Identity
                    : BuildDesiredWorld(parentIndex);
                var binding = _jointBindings[boneIndex];
                desiredWorld[boneIndex] = binding == null
                    ? _targetBindLocal[boneIndex] * parentWorld
                    : _targetBindWorld[boneIndex] *
                      binding.InverseBindMatrix *
                      binding.Joint.GetWorldMatrix(animation, time);
                visitStates[boneIndex] = 2;
                return desiredWorld[boneIndex];
            }

            for (var boneIndex = 0; boneIndex < _skeleton.Bones.Length; boneIndex++)
                BuildDesiredWorld(boneIndex);

            frame.Transforms = new List<RmvVector3>(_skeleton.Bones.Length);
            frame.Quaternion = new List<RmvVector4>(_skeleton.Bones.Length);
            for (var boneIndex = 0; boneIndex < _skeleton.Bones.Length; boneIndex++)
            {
                var localTransform = desiredWorld[boneIndex];
                var parentIndex = GetValidatedParentIndex(boneIndex);
                if (parentIndex >= 0)
                {
                    if (!Matrix4x4.Invert(desiredWorld[parentIndex], out var inverseParent))
                    {
                        throw new InvalidDataException(
                            $"glTF 动画在骨骼“{_skeleton.Bones[parentIndex].Name}”处包含不可逆变换。");
                    }

                    localTransform *= inverseParent;
                }

                if (!Matrix4x4.Decompose(
                        localTransform,
                        out _,
                        out var rotation,
                        out var translation))
                {
                    throw new InvalidDataException(
                        $"glTF 动画在骨骼“{_skeleton.Bones[boneIndex].Name}”处无法分解为平移和旋转。");
                }

                rotation = Quaternion.Normalize(rotation);
                var gameTranslation = new Xna.Vector3(
                    -translation.X,
                    translation.Y,
                    translation.Z);
                var gameRotation = new Xna.Quaternion(
                    rotation.X,
                    -rotation.Y,
                    -rotation.Z,
                    rotation.W);
                frame.Transforms.Add(new RmvVector3(gameTranslation));
                frame.Quaternion.Add(new RmvVector4(
                    gameRotation.X,
                    gameRotation.Y,
                    gameRotation.Z,
                    gameRotation.W));
            }
        }

        private void BuildTargetBindMatrices()
        {
            var bindFrame = _skeleton.AnimationParts[0].DynamicFrames[0];
            for (var boneIndex = 0; boneIndex < _skeleton.Bones.Length; boneIndex++)
            {
                var gameTranslation = bindFrame.Transforms[boneIndex].ToVector3();
                var gameRotation = bindFrame.Quaternion[boneIndex].ToQuaternion();
                _targetBindLocal[boneIndex] =
                    Matrix4x4.CreateFromQuaternion(new Quaternion(
                        gameRotation.X,
                        -gameRotation.Y,
                        -gameRotation.Z,
                        gameRotation.W)) *
                    Matrix4x4.CreateTranslation(
                        -gameTranslation.X,
                        gameTranslation.Y,
                        gameTranslation.Z);
            }

            var visitStates = new byte[_skeleton.Bones.Length];
            Matrix4x4 BuildWorld(int boneIndex)
            {
                if (visitStates[boneIndex] == 2)
                    return _targetBindWorld[boneIndex];
                if (visitStates[boneIndex] == 1)
                    throw new InvalidDataException(
                        $"目标游戏骨架在骨骼 {boneIndex} 处包含循环父级关系。");

                visitStates[boneIndex] = 1;
                var parentIndex = GetValidatedParentIndex(boneIndex);
                _targetBindWorld[boneIndex] = parentIndex < 0
                    ? _targetBindLocal[boneIndex]
                    : _targetBindLocal[boneIndex] * BuildWorld(parentIndex);
                visitStates[boneIndex] = 2;
                return _targetBindWorld[boneIndex];
            }

            for (var boneIndex = 0; boneIndex < _skeleton.Bones.Length; boneIndex++)
                BuildWorld(boneIndex);
        }

        private int GetValidatedParentIndex(int boneIndex)
        {
            var parentIndex = _skeleton.Bones[boneIndex].ParentId;
            if (parentIndex < -1 || parentIndex >= _skeleton.Bones.Length)
            {
                throw new InvalidDataException(
                    $"目标游戏骨架的骨骼 {boneIndex} 包含无效父级索引 {parentIndex}。");
            }
            if (parentIndex == boneIndex)
            {
                throw new InvalidDataException(
                    $"目标游戏骨架的骨骼 {boneIndex} 不能以自身为父级。");
            }

            return parentIndex;
        }
    }
}
