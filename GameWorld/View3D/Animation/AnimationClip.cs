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
            foreach (var animationPart in file.AnimationParts)
            {
                var frames = CreateKeyFramesFromAnimationPart(animationPart, skeleton);
                DynamicFrames.AddRange(frames);
            }

            Duration = TimeSpan.FromSeconds(
                file.Header.AnimationTotalPlayTimeInSec);
        }


        List<KeyFrame> CreateKeyFramesFromAnimationPart(AnimationPart animationPart, GameSkeleton skeleton)
        {
            var newDynamicFrames = new List<KeyFrame>();

            var animationSkeletonBoneCount = animationPart.RotationMappings.Count;
            var frameCount = animationPart.DynamicFrames.Count;

            if (frameCount == 0 && animationPart.StaticFrame != null)
                frameCount = 1; // Poses

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var newKeyframe = new KeyFrame();

                for (var animationSkeletonBoneIndex = 0; animationSkeletonBoneIndex < animationSkeletonBoneCount; animationSkeletonBoneIndex++)
                {
                    // We can apply animations to a skeleton where the skeleton of the animation is different then the skeleton we are applying it to
                    // If that is the case we just discard the information.
                    var isBoneIndexValid = animationSkeletonBoneIndex < skeleton.BoneCount;
                    if (isBoneIndexValid)
                    {
                        var translationLookup = animationPart.TranslationMappings[animationSkeletonBoneIndex];
                        if (translationLookup.IsDynamic)
                            newKeyframe.Position.Add(animationPart.DynamicFrames[frameIndex].Transforms[translationLookup.Id].ToVector3());
                        else if (translationLookup.IsStatic)
                            newKeyframe.Position.Add(animationPart.StaticFrame.Transforms[translationLookup.Id].ToVector3());
                        else
                            newKeyframe.Position.Add(skeleton.Translation[animationSkeletonBoneIndex]);

                        var rotationLookup = animationPart.RotationMappings[animationSkeletonBoneIndex];
                        if (rotationLookup.IsDynamic)
                            newKeyframe.Rotation.Add(animationPart.DynamicFrames[frameIndex].Quaternion[rotationLookup.Id].ToQuaternion());
                        else if (rotationLookup.IsStatic)
                            newKeyframe.Rotation.Add(animationPart.StaticFrame.Quaternion[rotationLookup.Id].ToQuaternion());
                        else
                            newKeyframe.Rotation.Add(skeleton.Rotation[animationSkeletonBoneIndex]);

                        newKeyframe.Scale.Add(Vector3.One);
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
                        10000 + part.StaticFrame!.Transforms.Count));
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
                        10000 + part.StaticFrame!.Quaternion.Count));
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
