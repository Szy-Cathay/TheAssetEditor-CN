using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GameWorld.Core.Animation
{
    public class GameSkeleton
    {
        List<Matrix> _worldTransform { get; set; }
        List<Matrix> _inverseWorldTransform { get; set; }
        List<int> _parentBoneIds { get; set; }

        public List<Vector3> Translation { get; private set; }
        public List<Quaternion> Rotation { get; private set; }
        public List<float> Scale { get; private set; }
        public List<string> BoneNames { get; private set; }
        public int BoneCount { get => BoneNames.Count; }
        public string SkeletonName { get; set; }

        public AnimationPlayer AnimationPlayer { get; private set; }

        public GameSkeleton(AnimationFile skeletonFile, AnimationPlayer animationPlayer)
        {
            var boneCount = skeletonFile.Bones.Count();
            Translation = new List<Vector3>(new Vector3[boneCount]);
            Rotation = new List<Quaternion>(new Quaternion[boneCount]);
            Scale = new List<float>(new float[boneCount]);
            _parentBoneIds = new List<int>(new int[boneCount]);
            BoneNames = new List<string>(new string[boneCount]);

            SkeletonName = skeletonFile.Header.SkeletonName;
            AnimationPlayer = animationPlayer;

            var skeletonAnimFrameIndex = 0;
            for (var i = 0; i < boneCount; i++)
            {
                _parentBoneIds[i] = skeletonFile.Bones[i].ParentId;
                BoneNames[i] = skeletonFile.Bones[i].Name;
                Rotation[i] = new Quaternion(
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Quaternion[i].X,
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Quaternion[i].Y,
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Quaternion[i].Z,
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Quaternion[i].W);

                Translation[i] = new Vector3(
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Transforms[i].X,
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Transforms[i].Y,
                    skeletonFile.AnimationParts[0].DynamicFrames[skeletonAnimFrameIndex].Transforms[i].Z);

                Scale[i] = 1;
            }

            RebuildSkeletonMatrix();
        }

        GameSkeleton() { }

        public static GameSkeleton CreateFromAnimationFile(AnimationFile animFile, AnimationPlayer animationPlayer)
        {
            ArgumentNullException.ThrowIfNull(animFile);
            ArgumentNullException.ThrowIfNull(animationPlayer);

            var bones = animFile.Bones
                ?? throw new InvalidDataException("Animation does not contain a bone table.");
            if (bones.Length > 256)
                throw new InvalidDataException($"Animation contains {bones.Length} bones; the renderer supports at most 256.");

            var skeleton = new GameSkeleton
            {
                SkeletonName = animFile.Header?.SkeletonName ?? "",
                AnimationPlayer = animationPlayer,
                Translation = new List<Vector3>(new Vector3[bones.Length]),
                Rotation = new List<Quaternion>(new Quaternion[bones.Length]),
                Scale = new List<float>(new float[bones.Length]),
                _parentBoneIds = new List<int>(new int[bones.Length]),
                BoneNames = new List<string>(new string[bones.Length]),
            };

            var part = animFile.AnimationParts.FirstOrDefault();
            var firstFrame = part?.DynamicFrames.FirstOrDefault() ?? part?.StaticFrame;

            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i]
                    ?? throw new InvalidDataException($"Animation bone {i} is missing.");
                skeleton._parentBoneIds[i] = bone.ParentId;
                skeleton.BoneNames[i] = bone.Name ?? "";
                skeleton.Translation[i] = Vector3.Zero;
                skeleton.Rotation[i] = Quaternion.Identity;
                skeleton.Scale[i] = 1;

                if (part == null || i >= part.TranslationMappings.Count || i >= part.RotationMappings.Count)
                    continue;

                var translationMapping = part.TranslationMappings[i];
                if (translationMapping.IsDynamic && firstFrame != null &&
                    translationMapping.Id >= 0 && translationMapping.Id < firstFrame.Transforms.Count)
                {
                    skeleton.Translation[i] = ToVector3(firstFrame.Transforms[translationMapping.Id]);
                }
                else if (translationMapping.IsStatic && part.StaticFrame != null &&
                    translationMapping.Id >= 0 && translationMapping.Id < part.StaticFrame.Transforms.Count)
                {
                    skeleton.Translation[i] = ToVector3(part.StaticFrame.Transforms[translationMapping.Id]);
                }

                var rotationMapping = part.RotationMappings[i];
                if (rotationMapping.IsDynamic && firstFrame != null &&
                    rotationMapping.Id >= 0 && rotationMapping.Id < firstFrame.Quaternion.Count)
                {
                    skeleton.Rotation[i] = ToQuaternion(firstFrame.Quaternion[rotationMapping.Id]);
                }
                else if (rotationMapping.IsStatic && part.StaticFrame != null &&
                    rotationMapping.Id >= 0 && rotationMapping.Id < part.StaticFrame.Quaternion.Count)
                {
                    skeleton.Rotation[i] = ToQuaternion(part.StaticFrame.Quaternion[rotationMapping.Id]);
                }
            }

            skeleton.RebuildSkeletonMatrix();
            return skeleton;
        }

        private static Vector3 ToVector3(Shared.GameFormats.RigidModel.Transforms.RmvVector3 value) =>
            new(value.X, value.Y, value.Z);

        private static Quaternion ToQuaternion(Shared.GameFormats.RigidModel.Transforms.RmvVector4 value)
        {
            var quaternion = new Quaternion(value.X, value.Y, value.Z, value.W);
            if (quaternion.LengthSquared() <= float.Epsilon)
                return Quaternion.Identity;

            quaternion.Normalize();
            return quaternion;
        }

        public void RebuildSkeletonMatrix()
        {
            var localTransforms = new Matrix[BoneCount];
            for (var i = 0; i < BoneCount; i++)
            {
                var translationMatrix = Matrix.CreateTranslation(Translation[i]);
                var rotationMatrix = Matrix.CreateFromQuaternion(Rotation[i]);
                var scaleMatrix = Matrix.CreateScale(Scale[i]);
                localTransforms[i] = scaleMatrix * rotationMatrix * translationMatrix;
            }

            var worldTransforms = new Matrix[BoneCount];
            var visitStates = new byte[BoneCount];

            Matrix BuildWorldTransform(int boneIndex)
            {
                if (visitStates[boneIndex] == 2)
                    return worldTransforms[boneIndex];
                if (visitStates[boneIndex] == 1)
                    throw new InvalidDataException($"Skeleton hierarchy contains a cycle at bone {boneIndex}.");

                visitStates[boneIndex] = 1;
                var parentIndex = GetParentBoneIndex(boneIndex);
                if (parentIndex < -1 || parentIndex >= BoneCount)
                    throw new InvalidDataException($"Bone {boneIndex} has invalid parent index {parentIndex}.");
                if (parentIndex == boneIndex)
                    throw new InvalidDataException($"Bone {boneIndex} cannot be its own parent.");

                worldTransforms[boneIndex] = parentIndex == -1
                    ? localTransforms[boneIndex]
                    : localTransforms[boneIndex] * BuildWorldTransform(parentIndex);
                visitStates[boneIndex] = 2;
                return worldTransforms[boneIndex];
            }

            for (var i = 0; i < BoneCount; i++)
                BuildWorldTransform(i);

            _worldTransform = worldTransforms.ToList();
            _inverseWorldTransform = new List<Matrix>(new Matrix[BoneCount]);
            for (var i = 0; i < BoneCount; i++)
            {
                _inverseWorldTransform[i] =
                    Matrix.Invert(_worldTransform[i]);
            }
        }

        public GameSkeleton Clone()
        {
            var clone = new GameSkeleton()
            {
                _worldTransform = _worldTransform.ToList(),
                _inverseWorldTransform =
                    _inverseWorldTransform.ToList(),
                _parentBoneIds = _parentBoneIds.ToList(),
                Translation = Translation.ToList(),
                Rotation = Rotation.ToList(),
                Scale = Scale.ToList(),
                BoneNames = BoneNames.ToList(),
                SkeletonName = SkeletonName,
                AnimationPlayer = AnimationPlayer,
                _frame = _frame
            };

            return clone;
        }


        public void Update()
        {
            if (AnimationPlayer != null)
            {
                var frame = AnimationPlayer.GetCurrentAnimationFrame();
                SetAnimationFrame(frame);
            }
        }

        AnimationFrame _frame;
        public void SetAnimationFrame(AnimationFrame frame)
        {
            _frame = frame;
        }

        public string GetBoneNameByIndex(int index)
        {
            if (index < 0 || index >= BoneCount) return "";

            return BoneNames[index];
        }

        public int GetBoneIndexByName(string name)
        {
            for (var i = 0; i < BoneNames.Count(); i++)
            {
                if (BoneNames[i] == name)
                    return i;
            }

            return -1;
        }

        public Matrix GetWorldTransform(int boneIndex)
        {
            return _worldTransform[boneIndex];
        }

        internal Matrix GetInverseWorldTransform(int boneIndex)
        {
            return _inverseWorldTransform[boneIndex];
        }

        public Matrix GetAnimatedWorldTranform(int boneIndex)
        {
            if (_frame != null)
                return _frame.GetSkeletonAnimatedWorld(this, boneIndex);

            return GetWorldTransform(boneIndex); ;
        }
        public Matrix GetAnimatedTranform(int boneIndex)
        {
            if (_frame != null)
                return _frame.BoneTransforms[boneIndex].WorldTransform;

            return GetWorldTransform(boneIndex); ;
        }

        public int GetParentBoneIndex(int boneIndex)
        {
            return _parentBoneIds[boneIndex];
        }

        public List<int> GetDirectChildBones(int parentBoneIndex)
        {
            var output = new List<int>();
            for (var i = 0; i < _parentBoneIds.Count; i++)
            {
                if (_parentBoneIds[i] == parentBoneIndex)
                    output.Add(i);
            }
            return output;
        }

        public List<int> GetAllChildBones(int parentBoneIndex)
        {
            var output = new List<int>();
            for (var i = 0; i < _parentBoneIds.Count; i++)
            {
                if (_parentBoneIds[i] == parentBoneIndex)
                {
                    output.Add(i);
                    var res = GetAllChildBones(i);
                    output.AddRange(res);
                }
            }
            return output;
        }

        public AnimationFrame ConvertToAnimationFrame()
        {
            var currentFrame = new AnimationFrame();
            for (var i = 0; i < BoneCount; i++)
            {
                currentFrame.BoneTransforms.Add(new AnimationFrame.BoneKeyFrame()
                {
                    Translation = Translation[i],
                    Rotation = Rotation[i],
                    Scale = new Vector3(Scale[i]),
                    BoneIndex = i,
                    ParentBoneIndex = GetParentBoneIndex(i),
                    WorldTransform = _worldTransform[i]
                }); ;
            }

            return currentFrame;
        }

        public AnimInvMatrixFile CreateInvMatrixFile()
        {
            if (HasBoneScale())
                throw new Exception("Skeleton contains scale and can not be saved. Bake first");

            var output = new AnimInvMatrixFile();

            output.Version = 1;
            output.MatrixList = new Matrix[_worldTransform.Count];
            for (var i = 0; i < _worldTransform.Count; i++)
                output.MatrixList[i] = Matrix.Transpose(Matrix.Invert(_worldTransform[i]));

            return output;
        }

        public void CreateChildBone(int parentBoneIndex)
        {
            _parentBoneIds.Add(parentBoneIndex);
            BoneNames.Add("new_bone");
            Translation.Add(Vector3.Zero);
            Rotation.Add(Quaternion.Identity);
            Scale.Add(1);
            RebuildSkeletonMatrix();
        }

        public void DeleteBone(int boneIndex, bool rebuildMatrix = true)
        {
            var boneGraph = SkeletonBoneGraph.Create(this);
            boneGraph.DeleteBone(boneIndex);
            boneGraph.FlattenInto(this);

            if (rebuildMatrix)
                RebuildSkeletonMatrix();
        }

        public bool HasBoneScale()
        {
            foreach (var value in Scale)
            {
                if (value != 1)
                    return true;
            }
            return false;
        }

        public void BakeScaleIntoSkeleton()
        {
            for (var i = 0; i < BoneCount; i++)
            {
                var scale = GetAccumulatedBoneScale(i);
                Translation[i] = Translation[i] * scale;
            }

            for (var i = 0; i < BoneCount; i++)
                Scale[i] = 1;
            RebuildSkeletonMatrix();
        }

        float GetAccumulatedBoneScale(int boneIndex)
        {
            var parentIndex = GetParentBoneIndex(boneIndex);
            if (parentIndex == -1)
                return 1;

            return GetAccumulatedBoneScale(parentIndex) * Scale[boneIndex];
        }

        [DebuggerDisplay("SkeletonBoneGraph = {BoneId}-{UpdatedBoneId}")]
        public class SkeletonBoneGraph
        {
            GameSkeleton _source;
            public List<SkeletonBoneGraph> Children { get; set; } = new List<SkeletonBoneGraph>();
            public int BoneId { get; set; }
            public SkeletonBoneGraph Parent { get; set; }
            public int UpdatedBoneId { get; set; }

            public static SkeletonBoneGraph Create(GameSkeleton skeleton)
            {
                var output = new SkeletonBoneGraph()
                {
                    _source = skeleton,
                    BoneId = 0,
                    Parent = null,
                };
                for (var i = 1; i < skeleton.BoneCount; i++)
                {
                    Console.WriteLine($"Adding bone {i}");
                    var parentBoneId = skeleton.GetParentBoneIndex(i);
                    var parentBone = output.GetBone(parentBoneId);
                    parentBone.Children.Add(new SkeletonBoneGraph() { _source = skeleton, BoneId = i, Parent = parentBone });
                }

                var startId = 0;
                output.UpdateBoneId(ref startId);

                return output;
            }

            public void DeleteBone(int boneIndex)
            {
                var bone = GetBone(boneIndex);
                var parent = bone.Parent;
                parent.Children.Remove(bone);
                var startId = 0;
                UpdateBoneId(ref startId);
            }

            public SkeletonBoneGraph GetBone(int id)
            {
                if (BoneId == id)
                    return this;
                foreach (var child in Children)
                {
                    if (child.BoneId == id)
                        return child;

                    var result = child.GetBone(id);
                    if (result != null)
                        return result;
                }
                return null;
            }

            void UpdateBoneId(ref int currentId)
            {
                UpdatedBoneId = currentId++;
                Console.WriteLine($"Current = {BoneId} new = {UpdatedBoneId}");

                foreach (var child in Children)
                    child.UpdateBoneId(ref currentId);
            }

            public void FlattenInto(GameSkeleton gameSkeleton)
            {
                var names = new List<string>();
                var parentBones = new List<int>();
                var translation = new List<Vector3>();
                var rotations = new List<Quaternion>();
                var scale = new List<float>();
                Flatten(ref names, ref parentBones, ref translation, ref rotations, ref scale);

                gameSkeleton.BoneNames = names;
                gameSkeleton._parentBoneIds = parentBones;
                gameSkeleton.Translation = translation;
                gameSkeleton.Rotation = rotations;
                gameSkeleton.Scale = scale;
            }

            public void Flatten(ref List<string> boneNames, ref List<int> parentBones, ref List<Vector3> translation, ref List<Quaternion> rotations, ref List<float> scale)
            {
                if (Parent == null)
                    parentBones.Add(-1);
                else
                    parentBones.Add(Parent.UpdatedBoneId);
                boneNames.Add(_source.BoneNames[BoneId]);
                translation.Add(_source.Translation[BoneId]);
                rotations.Add(_source.Rotation[BoneId]);
                scale.Add(_source.Scale[BoneId]);

                foreach (var child in Children)
                    child.Flatten(ref boneNames, ref parentBones, ref translation, ref rotations, ref scale);
            }
        }
    }
}
