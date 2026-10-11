using AnimationEditor.MountAnimationCreator.ViewModels;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace AnimationEditor.MountAnimationCreator.Services
{
    class MountAnimationGeneratorService
    {
        private readonly AnimationSettingsViewModel _settings;
        private readonly int _mountVertexId;
        private readonly int _riderBoneIndex;
        private readonly GameSkeleton _riderSkeleton;
        private readonly GameSkeleton _mountSkeleton;
        private readonly MeshAnimationHelper _mountVertexPositionResolver;

        public MountAnimationGeneratorService(AnimationSettingsViewModel settings, Rmv2MeshNode mountMesh,
            int mountVertexId, int riderBoneIndex, SceneObject rider, SceneObject mount)
        {
            _settings = settings;
            _mountVertexId = mountVertexId;
            _riderBoneIndex = riderBoneIndex;
            _riderSkeleton = rider.Skeleton;
            _mountSkeleton = mount.Skeleton;
            _mountVertexPositionResolver = new MeshAnimationHelper(mountMesh, Matrix.CreateScale((float)settings.Scale.Value));
        }

        public AnimationClip GenerateMountAnimation(AnimationClip mountAnimation, AnimationClip riderAnimation, bool synchronize = false, bool isDeath = false)
        {
            if (mountAnimation.DynamicFrames.Count == 0 || riderAnimation.DynamicFrames.Count == 0 ||
                mountAnimation.Duration <= TimeSpan.Zero || riderAnimation.Duration <= TimeSpan.Zero)
                throw new InvalidOperationException(LocalizationManager.Instance.Get("MountAnim.InvalidAnimation"));
            if (!SettingsAreValid(_settings))
                throw new InvalidOperationException(LocalizationManager.Instance.Get("MountAnim.InvalidSettings"));

            var translationOffset = new Vector3((float)_settings.Translation.X.Value, (float)_settings.Translation.Y.Value, (float)_settings.Translation.Z.Value);
            var rotationOffset = Quaternion.CreateFromYawPitchRoll(
                MathHelper.ToRadians((float)_settings.Rotation.X.Value),
                MathHelper.ToRadians((float)_settings.Rotation.Y.Value),
                MathHelper.ToRadians((float)_settings.Rotation.Z.Value));
            var result = riderAnimation.Clone();
            var loops = (int)_settings.LoopCounter.Value;
            // Game clips include the end sample; adjacent loops share that sample.
            for (var loop = 1; loop < loops; loop++)
                result.DynamicFrames.AddRange(riderAnimation.DynamicFrames.Skip(1).Select(frame => frame.Clone()));
            result.Duration = TimeSpan.FromTicks(checked(riderAnimation.Duration.Ticks * loops));
            if ((_settings.FitAnimation || synchronize) && (result.DynamicFrames.Count != mountAnimation.DynamicFrames.Count || result.Duration != mountAnimation.Duration))
            {
                var source = result.DynamicFrames;
                result.DynamicFrames = Enumerable.Range(0, mountAnimation.DynamicFrames.Count).Select(index =>
                {
                    var position = mountAnimation.DynamicFrames.Count == 1 ? 0 : (double)index * (source.Count - 1) / (mountAnimation.DynamicFrames.Count - 1);
                    var first = (int)Math.Floor(position);
                    var next = Math.Min(first + 1, source.Count - 1);
                    var fraction = (float)(position - first);
                    return new AnimationClip.KeyFrame
                    {
                        Position = source[first].Position.Select((value, bone) => Vector3.Lerp(value, source[next].Position[bone], fraction)).ToList(),
                        Rotation = source[first].Rotation.Select((value, bone) => Quaternion.Normalize(Quaternion.Slerp(value, source[next].Rotation[bone], fraction))).ToList(),
                        Scale = source[first].Scale.Select((value, bone) => Vector3.Lerp(value, source[next].Scale[bone], fraction)).ToList()
                    };
                }).ToList();
                result.Duration = mountAnimation.Duration;
            }

            var parent = _riderSkeleton.GetParentBoneIndex(_riderBoneIndex);
            var siblings = _riderSkeleton.GetDirectChildBones(parent);
            var riderMotionRoot = _riderSkeleton.GetBoneIndexByName("animroot");
            var mountMotionRoot = _mountSkeleton.GetBoneIndexByName("animroot");
            // Secondary chains must not lift the entire body to keep cloth or ornaments above ground.
            var bodyBones = isDeath ? _riderSkeleton.GetAllChildBones(_riderBoneIndex).Prepend(_riderBoneIndex)
                .Where(bone => bone == _riderBoneIndex || !IsSecondaryBone(_riderSkeleton.BoneNames[bone])).ToArray() : [];
            var groundClearance = isDeath ? Math.Max(0, bodyBones.Min(bone => _riderSkeleton.GetWorldTransform(bone).Translation.Y)) : 0;
            var riderStart = isDeath ? AnimationSampler.Sample(0, 0, _riderSkeleton, result).GetSkeletonAnimatedWorld(_riderSkeleton, _riderBoneIndex).Translation : Vector3.Zero;
            for (var index = 0; index < result.DynamicFrames.Count; index++)
            {
                var sampleTime = result.DynamicFrames.Count == 1 ? 0 : result.Duration.TotalSeconds * index / (result.DynamicFrames.Count - 1);
                var mountPositionInFrames = Math.Min(sampleTime / mountAnimation.Duration.TotalSeconds, 1) * (mountAnimation.DynamicFrames.Count - 1);
                var mountFrameIndex = (int)Math.Floor(mountPositionInFrames);
                var mountFrame = AnimationSampler.Sample(mountFrameIndex, (float)(mountPositionInFrames - mountFrameIndex), _mountSkeleton, mountAnimation);
                var attachment = _mountVertexPositionResolver.GetVertexTransformWorld(mountFrame, _mountVertexId);
                if (!attachment.Decompose(out _, out var mountRotation, out var mountPosition))
                    throw new InvalidOperationException(LocalizationManager.Instance.Get("MountAnim.InvalidVertex"));

                var originalFrame = AnimationSampler.Sample(index, 0, _riderSkeleton, result);
                var originalWorld = originalFrame.GetSkeletonAnimatedWorld(_riderSkeleton, _riderBoneIndex);
                originalWorld.Decompose(out _, out var originalRotation, out _);
                // Rider clips already contain their lean and fall; applying the carrier's rotation again doubles it.
                var riderRotation = _settings.KeepRiderRotation ? originalRotation : mountRotation;
                var heading = 0f;
                if (riderMotionRoot >= 0 && mountMotionRoot >= 0)
                {
                    var sourceRoot = originalFrame.GetSkeletonAnimatedWorld(_riderSkeleton, riderMotionRoot);
                    var carrierRoot = mountFrame.GetSkeletonAnimatedWorld(_mountSkeleton, mountMotionRoot);
                    heading = MathF.Atan2(carrierRoot.M31, carrierRoot.M33) - MathF.Atan2(sourceRoot.M31, sourceRoot.M33);
                    if (_settings.KeepRiderRotation)
                    {
                        var oriented = Matrix.CreateFromQuaternion(riderRotation) * Matrix.CreateRotationY(heading);
                        oriented.Decompose(out _, out riderRotation, out _);
                    }
                }
                // Death root motion includes leaving the seat, which must survive carrier-root synchronization.
                var departure = isDeath ? Vector3.TransformNormal(originalWorld.Translation - riderStart,
                    Matrix.CreateRotationY(heading) * Matrix.CreateFromQuaternion(rotationOffset)) : Vector3.Zero;
                var targetWorld = Matrix.CreateFromQuaternion(Quaternion.Normalize(riderRotation * rotationOffset)) *
                    Matrix.CreateTranslation(mountPosition + translationOffset + departure);
                var delta = Matrix.Invert(originalWorld) * targetWorld;
                if (isDeath)
                {
                    // The rider can leave the seat on ground contact without losing its authored fall.
                    var ground = mountMotionRoot < 0 ? 0 : mountFrame.GetSkeletonAnimatedWorld(_mountSkeleton, mountMotionRoot).Translation.Y * (float)_settings.Scale.Value;
                    var lowest = bodyBones.Min(bone => (originalFrame.GetSkeletonAnimatedWorld(_riderSkeleton, bone) * delta).Translation.Y);
                    var lift = Math.Max(0, ground + groundClearance - lowest);
                    targetWorld.Translation += Vector3.Up * lift;
                    delta = Matrix.Invert(originalWorld) * targetWorld;
                }

                // Keep root motion on animroot and express attachment transforms in their actual parent space.
                if ((_settings.IsRootNodeAnimation || synchronize) && riderMotionRoot >= 0 && mountMotionRoot >= 0 && riderMotionRoot != _riderBoneIndex)
                {
                    var root = mountFrame.GetSkeletonAnimatedWorld(_mountSkeleton, mountMotionRoot);
                    root.Decompose(out _, out var rotation, out var position);
                    result.DynamicFrames[index].Position[riderMotionRoot] = position * (float)_settings.Scale.Value;
                    result.DynamicFrames[index].Rotation[riderMotionRoot] = rotation;
                }

                var updatedFrame = AnimationSampler.Sample(index, 0, _riderSkeleton, result);
                var inverseParent = parent < 0 ? Matrix.Identity : Matrix.Invert(updatedFrame.GetSkeletonAnimatedWorld(_riderSkeleton, parent));
                foreach (var bone in siblings)
                {
                    if (bone == riderMotionRoot && bone != _riderBoneIndex)
                        continue;
                    var world = bone == _riderBoneIndex ? targetWorld : originalFrame.GetSkeletonAnimatedWorld(_riderSkeleton, bone) * delta;
                    var local = world * inverseParent;
                    local.Decompose(out var scale, out var rotation, out var position);
                    result.DynamicFrames[index].Position[bone] = position;
                    result.DynamicFrames[index].Rotation[bone] = Quaternion.Normalize(rotation);
                    result.DynamicFrames[index].Scale[bone] = scale;
                }
            }
            return result;
        }

        internal static bool SettingsAreValid(AnimationSettingsViewModel settings)
        {
            var values = new[] { settings.Scale.Value, settings.Translation.X.Value, settings.Translation.Y.Value,
                settings.Translation.Z.Value, settings.Rotation.X.Value, settings.Rotation.Y.Value, settings.Rotation.Z.Value };
            return values.All(x => double.IsFinite(x) && float.IsFinite((float)x)) && settings.Scale.Value > 0 &&
                double.IsFinite(settings.LoopCounter.Value) && settings.LoopCounter.Value >= 1 &&
                settings.LoopCounter.Value < int.MaxValue && settings.LoopCounter.Value == Math.Truncate(settings.LoopCounter.Value);
        }

        internal GameSkeleton GetRiderSkeleton() => _riderSkeleton;
        internal GameSkeleton GetMountSkeleton() => _mountSkeleton;

        private static bool IsSecondaryBone(string name) =>
            name.StartsWith("skirt_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("cape_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("head_strap_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("spine_tentacle_", StringComparison.OrdinalIgnoreCase);

        internal static bool IsMatchedCombatAnimation(string riderSlot)
        {
            const string prefix = "RIDER_COMBAT_";
            return riderSlot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(riderSlot[prefix.Length..], out _);
        }
    }
}
