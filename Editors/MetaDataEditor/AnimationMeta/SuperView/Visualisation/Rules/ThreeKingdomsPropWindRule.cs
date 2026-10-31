using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules;

internal sealed class ThreeKingdomsWindAngleState
{
    public ushort Heading { get; private set; }
    public ushort Correction { get; private set; }

    public ThreeKingdomsWindAngleState(ushort heading = 0, ushort correction = 0)
    {
        Heading = heading;
        Correction = correction;
    }

    // Three Kingdoms 0x2668a50: 16-bit angles, including the positive half-turn tie.
    public float Update(bool enabled, Vector3 wind, Vector3 forward, float dragFactor)
    {
        if (enabled)
        {
            var difference = SignedAngle(unchecked((ushort)(AngleUnits(wind.X, wind.Z) - Heading)));
            var step = NativeRound(difference * dragFactor);
            Heading = unchecked((ushort)(Heading + step));
            Correction = unchecked((ushort)(Heading - AngleUnits(forward.X, forward.Z)));
        }
        return SignedAngle(Correction) * 0.0000958738019107841f;
    }

    private static int AngleUnits(float x, float z) =>
        NativeRound(MathF.Atan2(x, z) * 10430.3779296875f);

    private static int NativeRound(float value) => (int)(value > 0 ? value + 0.5f : value - 0.5f);
    private static int SignedAngle(ushort value) => value > 32768 ? value - 65536 : value;
}

internal sealed class ThreeKingdomsPropWindRule : ILocalSpaceAnimationRule
{
    private const float WindFactor = 1.3f;
    private const float MinimumForce = 0.2f;
    private const float DragBlendDuration = 1.5f;
    private const float AnimationBlendDuration = 2.9f;
    private readonly GameSkeleton _skeleton;
    private readonly AnimationClip _primary;
    private readonly AnimationClip? _alternate;
    private readonly int _windBone;
    private readonly bool _blendAnimation;
    private readonly Func<Vector2> _wind;
    private readonly Func<Matrix> _attachment;
    private readonly Func<AnimationPlaybackTime> _clock;
    private readonly bool _loop;
    private readonly float _startTime;
    private readonly Vector3[] _positions;
    private readonly Quaternion[] _rotations;
    private readonly Vector3[] _scales;
    private ThreeKingdomsWindAngleState _angle = new();
    private AnimationPlaybackTime _lastClock;
    private Vector2 _lastWind;
    private Matrix _lastAttachment;
    private bool _hasPose;
    private bool _usingAlternate;
    private AnimationClip? _lastClip;
    private float _blendElapsed;

    internal bool UsesAlternate => _usingAlternate;
    internal ushort WindHeading => _angle.Heading;

    public ThreeKingdomsPropWindRule(GameSkeleton skeleton, AnimationClip primary,
        AnimationClip? alternate, int windBone, bool blendAnimation,
        Func<Vector2> wind, Func<Matrix> attachment, Func<AnimationPlaybackTime> clock,
        bool loop = true, float startTime = 0)
    {
        _skeleton = skeleton;
        _primary = primary;
        _alternate = alternate;
        _windBone = windBone;
        _blendAnimation = blendAnimation;
        _wind = wind;
        _attachment = attachment;
        _clock = clock;
        _loop = loop;
        _startTime = startTime;
        _positions = new Vector3[skeleton.BoneCount];
        _rotations = new Quaternion[skeleton.BoneCount];
        _scales = new Vector3[skeleton.BoneCount];
    }

    public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float time)
    {
        if (boneId != 0)
            return;

        var clock = _clock();
        var wind = _wind();
        var attachment = _attachment();
        if (!_hasPose || clock != _lastClock || wind != _lastWind || attachment != _lastAttachment)
            SamplePose(clock, wind, attachment);

        for (var index = 0; index < frame.BoneTransforms.Count; index++)
        {
            var bone = frame.BoneTransforms[index];
            bone.Translation = _positions[index];
            bone.Rotation = _rotations[index];
            bone.Scale = _scales[index];
        }
        frame.BoneTransforms[0].ComputeWorldMatrixFromComponents();
    }

    private void SamplePose(AnimationPlaybackTime clock, Vector2 wind, Matrix attachment)
    {
        var reset = !_hasPose || clock.Revision != _lastClock.Revision ||
            clock.ElapsedSeconds < _lastClock.ElapsedSeconds;
        var delta = reset ? 0 : (float)Math.Max(0, clock.ElapsedSeconds - _lastClock.ElapsedSeconds);
        if (reset)
        {
            _angle = new();
            _blendElapsed = 0;
            _hasPose = false;
        }

        var force = new Vector3(wind.X * WindFactor, 0, wind.Y * WindFactor);
        var enabled = _windBone >= 0 && float.IsFinite(force.X) && float.IsFinite(force.Z) &&
            force.LengthSquared() >= MinimumForce * MinimumForce;
        var useAlternate = !enabled && _alternate != null;
        var clip = useAlternate ? _alternate! : _primary;
        var duration = (float)clip.Duration.TotalSeconds;
        var sampleTime = duration <= 0 ? 0 : _loop
            ? (float)(clock.ElapsedSeconds % duration)
            : Math.Clamp(clock.ClipTimeSeconds - _startTime, 0, duration);
        var sampled = AnimationSampler.Sample(duration > 0 ? sampleTime / duration : 0,
            _skeleton, clip, freezeFrame: false);

        // Native animated attachment mode 2 restores the bind root before the wind modifier.
        var root = sampled.BoneTransforms[0];
        root.Translation = _skeleton.Translation[0];
        root.Rotation = _skeleton.Rotation[0];
        root.Scale = new Vector3(_skeleton.Scale[0]);

        if (_windBone >= 0)
        {
            var parentRotation = Quaternion.Identity;
            for (var parent = _skeleton.GetParentBoneIndex(_windBone); parent >= 0;
                parent = _skeleton.GetParentBoneIndex(parent))
                parentRotation = sampled.BoneTransforms[parent].Rotation * parentRotation;
            var bone = sampled.BoneTransforms[_windBone];
            var modelRotation = parentRotation * bone.Rotation;
            var forward = Vector3.TransformNormal(Vector3.Transform(Vector3.UnitY, modelRotation), attachment);
            var angle = _angle.Update(enabled, force, forward, Math.Clamp(delta / DragBlendDuration, 0, 1));
            bone.Rotation = ApplyModelSpaceRotation(bone.Rotation, parentRotation, angle);
        }

        // The native owner advances state+0x18 before the pose consumer (0x2426894).
        _blendElapsed = Math.Min(AnimationBlendDuration, _blendElapsed + delta);
        if (_hasPose && !ReferenceEquals(clip, _lastClip) && _blendElapsed > AnimationBlendDuration * 0.5f)
            _blendElapsed = Math.Max(AnimationBlendDuration - _blendElapsed, delta);
        var weight = !_hasPose || !_blendAnimation ? 1 : BlendIncrement(_blendElapsed, delta);
        for (var index = 0; index < _skeleton.BoneCount; index++)
        {
            var bone = sampled.BoneTransforms[index];
            _positions[index] = Vector3.Lerp(_positions[index], bone.Translation, weight);
            _rotations[index] = weight >= 1 ? bone.Rotation : Quaternion.Slerp(_rotations[index], bone.Rotation, weight);
            _scales[index] = Vector3.Lerp(_scales[index], bone.Scale, weight);
        }
        _usingAlternate = useAlternate;
        _lastClip = clip;
        _lastClock = clock;
        _lastWind = wind;
        _lastAttachment = attachment;
        _hasPose = true;
    }

    internal static float BlendIncrement(float elapsed, float delta)
    {
        if (AnimationBlendDuration - elapsed <= 0.001f)
            return 1;
        if (delta <= 0)
            return 0;
        var current = elapsed / AnimationBlendDuration;
        var previous = (elapsed - delta) / AnimationBlendDuration;
        var a = (3 - 2 * previous) * (previous * previous);
        var b = (3 - 2 * current) * (current * current);
        return Math.Clamp((b - a) / (1 - a), 0, 1);
    }

    internal static Quaternion ApplyModelSpaceRotation(Quaternion local, Quaternion parent, float angle)
    {
        // Native 0x7394e0 applies the delta in prop model space, then restores local space.
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -angle);
        var result = Quaternion.Inverse(parent) * (turn * (parent * local));
        result.Normalize();
        return result;
    }

    internal static Matrix CreateAttachmentTransform(GameSkeleton skeleton, Vector3 position,
        Quaternion orientation, Matrix characterAttachment)
    {
        // Native mode 2 normalizes authored translation and rotation separately (0x2670e0f).
        var inverseRoot = Quaternion.Inverse(skeleton.Rotation[0]);
        var translation = Vector3.Transform(position - skeleton.Translation[0], inverseRoot);
        var rotation = orientation * inverseRoot;
        rotation.Normalize();
        return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation) * characterAttachment;
    }
}
