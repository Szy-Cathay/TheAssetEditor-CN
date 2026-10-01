using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using static Shared.GameFormats.Animation.AnimationFile;


namespace GameWorld.Core.Animation
{
    public class AnimationClip
    {
        private const int StaticMappingOffset = 10000;

        public class KeyFrame
        {
            public List<Vector3> Position { get; set; } = new List<Vector3>();
            public List<Quaternion> Rotation { get; set; } = new List<Quaternion>();
            public List<Vector3> Scale { get; set; } = new List<Vector3>();

            public override string ToString()
            {
                return $"PosCount = {Position.Count}, RotCount = {Rotation.Count}, ScaleCount = {Scale.Count}";
            }

            public KeyFrame Clone()
            {
                return new KeyFrame()
                {
                    Position = new List<Vector3>(Position),
                    Rotation = new List<Quaternion>(Rotation),
                    Scale = new List<Vector3>(Scale)
                };
            }

            public int GetBoneCountFromFrame()
            {
                if (Position.Count == Rotation.Count && Rotation.Count == Scale.Count)
                    return Position.Count;
                throw new Exception($"Not all attribues have the same count P: {Position.Count} R:{Rotation.Count} S:{Scale.Count}");
            }
        }

        public List<KeyFrame> DynamicFrames = new List<KeyFrame>();

        public TimeSpan Duration { get; set; }

        public AnimationTimebase? Timebase
        {
            get
            {
                if (DynamicFrames.Count == 0 || Duration <= TimeSpan.Zero)
                    return null;

                return new AnimationTimebase(
                    DynamicFrames.Count,
                    Duration);
            }
        }

        public int AnimationBoneCount
        {
            get
            {
                var dynamicBones = 0;
                if (DynamicFrames.Count != 0)
                    return DynamicFrames[0].Position.Count;
                return dynamicBones;
            }
        }


        public AnimationClip() { }

        public AnimationClip(AnimationFile file, GameSkeleton skeleton)
        {
            var targetBoneIndices = file.Bones != null && file.Bones.Length != 0 &&
                                    file.Bones.All(bone => !string.IsNullOrEmpty(bone.Name))
                ? file.Bones.Select(bone => skeleton.GetBoneIndexByName(bone.Name)).ToArray()
                : null;
            if (targetBoneIndices != null && file.Bones.Where((bone, index) => targetBoneIndices[index] >= 0)
                .Any(bone => bone.ParentId >= 0
                    ? targetBoneIndices[bone.ParentId] < 0 || targetBoneIndices[bone.ParentId] !=
                        skeleton.GetParentBoneIndex(targetBoneIndices[bone.Id])
                    : skeleton.GetParentBoneIndex(targetBoneIndices[bone.Id]) >= 0))
            {
                DynamicFrames.AddRange(CreateFramesWithDifferentHierarchy(file, skeleton, targetBoneIndices));
            }
            else
            {
                foreach (var animationPart in file.AnimationParts)
                {
                    var frames = CreateKeyFramesFromAnimationPart(animationPart, skeleton, targetBoneIndices);
                    DynamicFrames.AddRange(frames);
                }
            }

            Duration = TimeSpan.FromSeconds(
                file.Header.AnimationTotalPlayTimeInSec);
        }

        private List<KeyFrame> CreateFramesWithDifferentHierarchy(AnimationFile file, GameSkeleton skeleton, int[] targetBoneIndices)
        {
            var sourceSkeleton = GameSkeleton.CreateFromAnimationFile(file, new AnimationPlayer());
            for (var index = 0; index < file.Bones.Length; index++)
            {
                if (targetBoneIndices[index] < 0)
                    continue;
                var parent = file.Bones[index].ParentId;
                var parentBindWorld = parent < 0
                    ? Matrix.Identity
                    : targetBoneIndices[parent] >= 0
                        ? skeleton.GetWorldTransform(targetBoneIndices[parent])
                        : sourceSkeleton.GetWorldTransform(parent);
                var bindLocal = skeleton.GetWorldTransform(targetBoneIndices[index]) * Matrix.Invert(parentBindWorld);
                bindLocal.Decompose(out _, out var rotation, out var position);
                sourceSkeleton.Translation[index] = position;
                sourceSkeleton.Rotation[index] = Quaternion.Normalize(rotation);
            }
            sourceSkeleton.RebuildSkeletonMatrix();
            var sourceIndices = Enumerable.Repeat(-1, skeleton.BoneCount).ToArray();
            for (var index = 0; index < targetBoneIndices.Length; index++)
            {
                if (targetBoneIndices[index] >= 0)
                    sourceIndices[targetBoneIndices[index]] = index;
            }

            var result = new List<KeyFrame>();
            foreach (var part in file.AnimationParts)
            {
                foreach (var sourceFrame in CreateKeyFramesFromAnimationPart(part, sourceSkeleton, null))
                {
                    var targetFrame = new KeyFrame
                    {
                        Position = new List<Vector3>(skeleton.Translation),
                        Rotation = new List<Quaternion>(skeleton.Rotation),
                        Scale = Enumerable.Repeat(Vector3.One, skeleton.BoneCount).ToList(),
                    };
                    var sourceWorlds = new Matrix?[sourceSkeleton.BoneCount];
                    var targetWorlds = new Matrix?[skeleton.BoneCount];
                    Matrix SourceWorld(int index)
                    {
                        if (sourceWorlds[index] is Matrix cached)
                            return cached;
                        var world = Matrix.CreateFromQuaternion(Quaternion.Normalize(sourceFrame.Rotation[index])) *
                            Matrix.CreateTranslation(sourceFrame.Position[index]);
                        var parent = sourceSkeleton.GetParentBoneIndex(index);
                        if (parent >= 0)
                            world *= SourceWorld(parent);
                        sourceWorlds[index] = world;
                        return world;
                    }
                    Matrix TargetWorld(int index)
                    {
                        if (targetWorlds[index] is Matrix cached)
                            return cached;
                        var parent = skeleton.GetParentBoneIndex(index);
                        var parentWorld = parent < 0 ? Matrix.Identity : TargetWorld(parent);
                        if (sourceIndices[index] >= 0)
                        {
                            // Bone names identify tracks; the file hierarchy defines their original space.
                            var local = SourceWorld(sourceIndices[index]) * Matrix.Invert(parentWorld);
                            local.Decompose(out _, out var rotation, out var position);
                            targetFrame.Position[index] = position;
                            targetFrame.Rotation[index] = Quaternion.Normalize(rotation);
                        }
                        var world = Matrix.CreateFromQuaternion(targetFrame.Rotation[index]) *
                            Matrix.CreateTranslation(targetFrame.Position[index]) * parentWorld;
                        targetWorlds[index] = world;
                        return world;
                    }
                    for (var index = 0; index < skeleton.BoneCount; index++)
                        TargetWorld(index);
                    result.Add(targetFrame);
                }
            }
            return result;
        }


        List<KeyFrame> CreateKeyFramesFromAnimationPart(AnimationPart animationPart, GameSkeleton skeleton, int[]? targetBoneIndices)
        {
            var newDynamicFrames = new List<KeyFrame>();

            var animationSkeletonBoneCount = animationPart.RotationMappings.Count;
            var frameCount = animationPart.DynamicFrames.Count;

            if (frameCount == 0 && animationPart.StaticFrame != null)
                frameCount = 1; // Poses

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var newKeyframe = new KeyFrame
                {
                    Position = new List<Vector3>(skeleton.Translation),
                    Rotation = new List<Quaternion>(skeleton.Rotation),
                    Scale = Enumerable.Repeat(Vector3.One, skeleton.BoneCount).ToList(),
                };

                for (var animationSkeletonBoneIndex = 0; animationSkeletonBoneIndex < animationSkeletonBoneCount; animationSkeletonBoneIndex++)
                {
                    var targetBoneIndex = targetBoneIndices == null
                        ? animationSkeletonBoneIndex
                        : animationSkeletonBoneIndex < targetBoneIndices.Length
                            ? targetBoneIndices[animationSkeletonBoneIndex]
                            : -1;
                    if (targetBoneIndex >= 0 && targetBoneIndex < skeleton.BoneCount)
                    {
                        var translationLookup = animationPart.TranslationMappings[animationSkeletonBoneIndex];
                        if (translationLookup.IsDynamic)
                            newKeyframe.Position[targetBoneIndex] = animationPart.DynamicFrames[frameIndex].Transforms[translationLookup.Id].ToVector3();
                        else if (translationLookup.IsStatic)
                            newKeyframe.Position[targetBoneIndex] = animationPart.StaticFrame.Transforms[translationLookup.Id].ToVector3();

                        var rotationLookup = animationPart.RotationMappings[animationSkeletonBoneIndex];
                        if (rotationLookup.IsDynamic)
                            newKeyframe.Rotation[targetBoneIndex] = animationPart.DynamicFrames[frameIndex].Quaternion[rotationLookup.Id].ToQuaternion();
                        else if (rotationLookup.IsStatic)
                            newKeyframe.Rotation[targetBoneIndex] = animationPart.StaticFrame.Quaternion[rotationLookup.Id].ToQuaternion();
                    }
                }

                newDynamicFrames.Add(newKeyframe);
            }

            return newDynamicFrames;
        }

        public AnimationFile ConvertToFileFormat(GameSkeleton skeleton)
        {
            return ConvertToFileFormat(skeleton, 7);
        }

        public AnimationFile ConvertToFileFormat(
            GameSkeleton skeleton,
            uint version,
            uint unknownValueV8 = 0,
            IReadOnlyList<string>? flagVariables = null)
        {
            if (version is not 7 and not 8)
                throw new ArgumentOutOfRangeException(nameof(version));

            var output = new AnimationFile();

            output.Header.FrameRate = (float)(Timebase?.FramesPerSecond ?? 20);

            output.Header.Version = version;
            output.Header.AnimationTotalPlayTimeInSec =
                (float)Duration.TotalSeconds;
            output.Header.SkeletonName = skeleton.SkeletonName;
            output.Header.UnknownValue_v8 = unknownValueV8;
            output.Header.FlagVariables = flagVariables?.ToList() ?? [];
            output.Header.FlagCount = (uint)output.Header.FlagVariables.Count;

            output.Bones = new BoneInfo[skeleton.BoneCount];
            for (var i = 0; i < skeleton.BoneCount; i++)
            {
                output.Bones[i] = new BoneInfo()
                {
                    Id = i,
                    Name = skeleton.BoneNames[i],
                    ParentId = skeleton.GetParentBoneIndex(i)
                };
            }

            var frames = new List<Frame>();
            for (var i = 0; i < DynamicFrames.Count; i++)
                frames.Add(CreateFrameFromKeyFrame(i, skeleton));

            output.AnimationParts.Add(version == 8
                ? CreateVersionEightPart(frames, skeleton.BoneCount)
                : CreateVersionSevenPart(frames, skeleton.BoneCount));

            return output;
        }

        private static AnimationPart CreateVersionSevenPart(
            IReadOnlyList<Frame> frames,
            int boneCount)
        {
            var part = new AnimationPart();
            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                part.RotationMappings.Add(new AnimationBoneMapping(boneIndex));
                part.TranslationMappings.Add(new AnimationBoneMapping(boneIndex));
            }

            part.DynamicFrames.AddRange(frames);
            return part;
        }

        private static AnimationPart CreateVersionEightPart(
            IReadOnlyList<Frame> frames,
            int boneCount)
        {
            if (frames.Count == 0)
                throw new InvalidOperationException("Version 8 animation requires at least one frame.");

            var staticTranslations = new bool[boneCount];
            var staticRotations = new bool[boneCount];
            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                staticTranslations[boneIndex] = frames
                    .Skip(1)
                    .All(frame => NearlyEqual(
                        frames[0].Transforms[boneIndex],
                        frame.Transforms[boneIndex]));
                staticRotations[boneIndex] = frames
                    .Skip(1)
                    .All(frame => NearlyEqual(
                        frames[0].Quaternion[boneIndex],
                        frame.Quaternion[boneIndex]));
            }

            if (frames.Count > 1 &&
                staticTranslations.All(value => value) &&
                staticRotations.All(value => value) &&
                boneCount != 0)
            {
                staticTranslations[0] = false;
            }

            var part = new AnimationPart();
            var hasStaticTracks = staticTranslations.Any(value => value) ||
                                  staticRotations.Any(value => value);
            if (hasStaticTracks)
                part.StaticFrame = new Frame();

            var hasDynamicTracks = staticTranslations.Any(value => !value) ||
                                   staticRotations.Any(value => !value);
            if (hasDynamicTracks)
            {
                for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                    part.DynamicFrames.Add(new Frame());
            }

            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                if (staticTranslations[boneIndex])
                {
                    part.TranslationMappings.Add(new AnimationBoneMapping(
                        StaticMappingOffset + part.StaticFrame!.Transforms.Count));
                    part.StaticFrame.Transforms.Add(
                        frames[0].Transforms[boneIndex]);
                }
                else
                {
                    var mappingId = part.DynamicFrames[0].Transforms.Count;
                    part.TranslationMappings.Add(new AnimationBoneMapping(mappingId));
                    for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                    {
                        part.DynamicFrames[frameIndex].Transforms.Add(
                            frames[frameIndex].Transforms[boneIndex]);
                    }
                }

                if (staticRotations[boneIndex])
                {
                    part.RotationMappings.Add(new AnimationBoneMapping(
                        StaticMappingOffset + part.StaticFrame!.Quaternion.Count));
                    part.StaticFrame.Quaternion.Add(
                        frames[0].Quaternion[boneIndex]);
                }
                else
                {
                    var mappingId = part.DynamicFrames[0].Quaternion.Count;
                    part.RotationMappings.Add(new AnimationBoneMapping(mappingId));
                    for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                    {
                        part.DynamicFrames[frameIndex].Quaternion.Add(
                            frames[frameIndex].Quaternion[boneIndex]);
                    }
                }
            }

            return part;
        }

        private static bool NearlyEqual(RmvVector3 first, RmvVector3 second)
        {
            const float tolerance = 0.000001f;
            return MathF.Abs(first.X - second.X) <= tolerance &&
                   MathF.Abs(first.Y - second.Y) <= tolerance &&
                   MathF.Abs(first.Z - second.Z) <= tolerance;
        }

        private static bool NearlyEqual(RmvVector4 first, RmvVector4 second)
        {
            const float tolerance = 0.000001f;
            var sameSign = MathF.Abs(first.X - second.X) <= tolerance &&
                           MathF.Abs(first.Y - second.Y) <= tolerance &&
                           MathF.Abs(first.Z - second.Z) <= tolerance &&
                           MathF.Abs(first.W - second.W) <= tolerance;
            var oppositeSign = MathF.Abs(first.X + second.X) <= tolerance &&
                               MathF.Abs(first.Y + second.Y) <= tolerance &&
                               MathF.Abs(first.Z + second.Z) <= tolerance &&
                               MathF.Abs(first.W + second.W) <= tolerance;
            return sameSign || oppositeSign;
        }

        private Frame CreateFrameFromKeyFrame(int frameIndex, GameSkeleton skeleton)
        {
            var frame = DynamicFrames[frameIndex];
            var output = new Frame();

            for (var boneIndex = 0; boneIndex < frame.Position.Count(); boneIndex++)
            {
                var scale = GetAccumulatedBoneScale(boneIndex, frameIndex, skeleton);
                var transform = frame.Position[boneIndex] * scale;
                output.Transforms.Add(new RmvVector3(transform));

                var rot = frame.Rotation[boneIndex];
                output.Quaternion.Add(new RmvVector4(rot.X, rot.Y, rot.Z, rot.W));
            }

            return output;
        }

        float GetAccumulatedBoneScale(int boneIndex, int frameIndex, GameSkeleton skeleton)
        {
            var parentIndex = skeleton.GetParentBoneIndex(boneIndex);
            if (parentIndex == -1)
                return DynamicFrames[frameIndex].Scale[boneIndex].X;

            return GetAccumulatedBoneScale(parentIndex, frameIndex, skeleton) * DynamicFrames[frameIndex].Scale[boneIndex].X;
        }

        public AnimationClip Clone()
        {
            var copy = new AnimationClip();
            foreach (var item in DynamicFrames)
                copy.DynamicFrames.Add(item.Clone());
            copy.Duration = Duration;

            return copy;
        }

        public static AnimationClip CreateSkeletonAnimation(GameSkeleton skeleton)
        {
            var clip = new AnimationClip();

            var frame = new KeyFrame();
            for (var i = 0; i < skeleton.BoneCount; i++)
            {
                frame.Position.Add(skeleton.Translation[i]);
                frame.Rotation.Add(skeleton.Rotation[i]);
                frame.Scale.Add(Vector3.One);
            }

            // Skeletons have two identical frames, dont know why
            clip.DynamicFrames.Add(frame.Clone());
            clip.DynamicFrames.Add(frame.Clone());

            clip.Duration = TimeSpan.FromSeconds(0.1);
            return clip;
        }

        public void ScaleAnimation(float scale)
        {
            foreach (var frame in DynamicFrames)
            {
                for (var i = 0; i < AnimationBoneCount; i++)
                    frame.Scale[i] = new Vector3(scale);
            }
        }
    }
}
