using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchLayerMode
{
    Override,
    Additive,
}

public enum AnimationWorkbenchAdditiveReferencePose
{
    AnimationBFirstFrame,
    TargetSkeletonRestPose,
}

public sealed class AnimationWorkbenchBoneMask
{
    public AnimationWorkbenchBoneMask(
        string skeletonName,
        IReadOnlyCollection<string> boneNames)
    {
        SkeletonName = skeletonName;
        BoneNames = boneNames.ToArray();
    }

    public string SkeletonName { get; }

    public IReadOnlyList<string> BoneNames { get; }
}

public sealed record AnimationWorkbenchBoneMaskResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    AnimationWorkbenchBoneMask? Mask,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed record AnimationWorkbenchBoneHierarchyItem(
    int Index,
    string Name,
    int ParentIndex);

public sealed record AnimationWorkbenchBoneHierarchy(
    string SkeletonName,
    string Fingerprint,
    IReadOnlyList<AnimationWorkbenchBoneHierarchyItem> Bones);

public sealed record AnimationWorkbenchLayerRequest(
    AnimationWorkbenchBoneMask Mask,
    AnimationWorkbenchLayerMode Mode,
    double Weight,
    AnimationWorkbenchAdditiveReferencePose ReferencePose);

public sealed record AnimationWorkbenchLayerImpact(
    AnimationWorkbenchLayerMode Mode,
    AnimationWorkbenchAdditiveReferencePose ReferencePose,
    double Weight,
    int MaskedBoneCount,
    int OutputFrameCount,
    TimeSpan OutputDuration,
    bool AnimationBWasResampled);

public sealed record AnimationWorkbenchLayerResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    AnimationWorkbenchLayerImpact? Impact,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed partial class AnimationWorkbenchDocument
{
    private SourceSnapshot? _layerPreviewResult;
    private AnimationWorkbenchLayerImpact? _layerPreviewImpact;
    private long _layerPreviewVersion;

    internal long ActiveLayerPreviewVersion =>
        _layerPreviewResult == null ? 0 : _layerPreviewVersion;

    public AnimationWorkbenchBoneHierarchy GetBoneHierarchy()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_targetSkeleton == null)
            return new AnimationWorkbenchBoneHierarchy("", "", []);

        return new AnimationWorkbenchBoneHierarchy(
            _targetSkeleton.SkeletonName,
            AnimationWorkbenchBoneMaskStore.ComputeSkeletonFingerprint(
                _targetSkeleton),
            Enumerable.Range(0, _targetSkeleton.BoneCount)
                .Select(index => new AnimationWorkbenchBoneHierarchyItem(
                    index,
                    _targetSkeleton.BoneNames[index],
                    _targetSkeleton.GetParentBoneIndex(index)))
                .ToArray());
    }

    public AnimationWorkbenchBoneMaskResult CreateBoneMask(
        IReadOnlyCollection<string>? rootBoneNames,
        bool includeDescendants)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_targetSkeleton == null)
        {
            return CreateBoneMaskFailure(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing);
        }

        var requested = rootBoneNames?.ToArray() ?? [];
        if (requested.Length == 0)
        {
            return CreateBoneMaskFailure(
                AnimationWorkbenchDiagnosticCode.LayerMaskEmpty);
        }

        var duplicateRequest = requested
            .GroupBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRequest != null)
        {
            return CreateBoneMaskFailure(
                AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                duplicateRequest.Key);
        }

        var selectedIndexes = new SortedSet<int>();
        foreach (var boneName in requested)
        {
            var matches = _targetSkeleton.BoneNames
                .Select((name, index) => (name, index))
                .Where(item => string.Equals(
                    item.name,
                    boneName,
                    StringComparison.Ordinal))
                .Select(item => item.index)
                .ToArray();
            if (matches.Length == 0)
            {
                return CreateBoneMaskFailure(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneMissing,
                    boneName);
            }
            if (matches.Length > 1)
            {
                return CreateBoneMaskFailure(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                    boneName);
            }
            selectedIndexes.Add(matches[0]);
        }

        if (includeDescendants)
        {
            for (var boneIndex = 0;
                 boneIndex < _targetSkeleton.BoneCount;
                 boneIndex++)
            {
                var parentIndex = _targetSkeleton.GetParentBoneIndex(boneIndex);
                while (parentIndex >= 0)
                {
                    if (selectedIndexes.Contains(parentIndex))
                    {
                        selectedIndexes.Add(boneIndex);
                        break;
                    }
                    parentIndex = _targetSkeleton.GetParentBoneIndex(parentIndex);
                }
            }
        }

        var names = selectedIndexes
            .Select(index => _targetSkeleton.BoneNames[index])
            .ToArray();
        var duplicateBone = names
            .GroupBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBone != null)
        {
            return CreateBoneMaskFailure(
                AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                duplicateBone.Key);
        }

        return new AnimationWorkbenchBoneMaskResult(
            true,
            CreateState(),
            new AnimationWorkbenchBoneMask(
                _targetSkeleton.SkeletonName,
                names),
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    public AnimationWorkbenchLayerResult PreviewLayer(
        AnimationWorkbenchLayerRequest request)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mask);

        var activePreview = GetActiveEditPreviewDiagnostic(
            EditPreviewKind.Layer);
        if (activePreview != null)
        {
            return CreateLayerFailure(activePreview.Value);
        }

        var build = BuildLayer(request);
        if (build.Animation == null || build.Impact == null)
        {
            if (_layerPreviewResult != null)
            {
                ClearLayerPreview();
                RefreshSelectedResultPreview();
            }
            return new AnimationWorkbenchLayerResult(
                false,
                CreateState(),
                null,
                build.Diagnostics);
        }

        _layerPreviewResult = _result!.WithPreviewAnimation(build.Animation);
        _layerPreviewImpact = build.Impact;
        PreviewLayerMetaData(build.Impact);
        _layerPreviewVersion = _layerPreviewVersion == long.MaxValue
            ? 1
            : _layerPreviewVersion + 1;
        RefreshSelectedResultPreview();
        return CreateLayerSuccess();
    }

    public AnimationWorkbenchLayerResult CommitLayerPreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_layerPreviewResult == null || _layerPreviewImpact == null)
        {
            return CreateLayerFailure(
                AnimationWorkbenchDiagnosticCode.LayerPreviewMissing);
        }

        var next = _layerPreviewResult.Animation.Clone();
        var impact = _layerPreviewImpact;
        var metaData = CaptureLayerPreviewMetaData();
        ClearLayerPreview();
        CommitResultAnimation(
            next,
            [0, next.DynamicFrames.Count - 1],
            metaData);
        return new AnimationWorkbenchLayerResult(
            true,
            CreateState(),
            impact,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    public AnimationWorkbenchLayerResult CancelLayerPreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_layerPreviewResult == null)
        {
            return CreateLayerFailure(
                AnimationWorkbenchDiagnosticCode.LayerPreviewMissing);
        }

        ClearLayerPreview();
        RefreshSelectedResultPreview();
        return new AnimationWorkbenchLayerResult(
            true,
            CreateState(),
            null,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    private LayerBuildResult BuildLayer(AnimationWorkbenchLayerRequest request)
    {
        if (_animationA == null)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.AnimationAMissing));
        }
        if (_animationB == null)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerAnimationBMissing));
        }
        if (_targetSkeleton == null)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing));
        }
        var preparedAnimationA = _retargetedAnimationA ?? _animationA;
        var preparedAnimationB = _retargetedAnimationB ?? _animationB;
        if (double.IsFinite(request.Weight) == false ||
            request.Weight < 0 ||
            request.Weight > 1)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerWeightInvalid));
        }
        if (Enum.IsDefined(request.Mode) == false)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerModeInvalid));
        }
        if (Enum.IsDefined(request.ReferencePose) == false)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerReferencePoseInvalid));
        }
        if (TryValidateLayerSource(
                preparedAnimationA,
                AnimationWorkbenchSourceSlot.AnimationA,
                out var sourceDiagnostic) == false ||
            TryValidateLayerSource(
                preparedAnimationB,
                AnimationWorkbenchSourceSlot.AnimationB,
                out sourceDiagnostic) == false)
        {
            return LayerBuildResult.Failure(sourceDiagnostic!);
        }
        if (string.Equals(
                request.Mask.SkeletonName,
                _targetSkeleton.SkeletonName,
                StringComparison.Ordinal) == false)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerMaskSkeletonMismatch));
        }
        if (request.Mask.BoneNames.Count == 0)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerMaskEmpty));
        }

        var selectedIndexes = new List<int>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boneName in request.Mask.BoneNames)
        {
            if (seenNames.Add(boneName) == false)
            {
                return LayerBuildResult.Failure(CreateLayerDiagnostic(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                    boneName: boneName));
            }
            var targetMatches = FindBoneIndexes(_targetSkeleton, boneName);
            if (targetMatches.Length == 0)
            {
                return LayerBuildResult.Failure(CreateLayerDiagnostic(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneMissing,
                    boneName: boneName));
            }
            if (targetMatches.Length > 1)
            {
                return LayerBuildResult.Failure(CreateLayerDiagnostic(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                    boneName: boneName));
            }
            var sourceMatches = FindBoneIndexes(
                preparedAnimationB.Skeleton,
                boneName);
            if (sourceMatches.Length == 0)
            {
                return LayerBuildResult.Failure(CreateLayerDiagnostic(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneUnmapped,
                    AnimationWorkbenchSourceSlot.AnimationB,
                    boneName));
            }
            if (sourceMatches.Length > 1)
            {
                return LayerBuildResult.Failure(CreateLayerDiagnostic(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                    AnimationWorkbenchSourceSlot.AnimationB,
                    boneName));
            }
            selectedIndexes.Add(targetMatches[0]);
        }

        if (SkeletonsMatch(
                preparedAnimationA.Skeleton,
                _targetSkeleton) == false ||
            SkeletonsMatch(
                preparedAnimationB.Skeleton,
                _targetSkeleton) == false)
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerSkeletonMismatch));
        }

        var animationA = preparedAnimationA.Animation;
        var animationB = preparedAnimationB.Animation;
        var output = animationA.Clone();
        for (var frameIndex = 0;
             frameIndex < output.DynamicFrames.Count;
             frameIndex++)
        {
            var sampleTime = animationA.Timebase!.GetSampleTime(frameIndex);
            var sourceB = SampleLocalFrame(
                animationB,
                sampleTime,
                _targetSkeleton.BoneCount);
            var outputFrame = output.DynamicFrames[frameIndex];
            foreach (var boneIndex in selectedIndexes)
            {
                ApplyLayerTransform(
                    outputFrame,
                    sourceB,
                    boneIndex,
                    request,
                    _targetSkeleton,
                    animationB.DynamicFrames[0]);
            }
        }

        if (output.DynamicFrames.Any(frame =>
                HasValidFrameTransforms(frame, _targetSkeleton.BoneCount) == false))
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerResultTransformInvalid));
        }
        if (output.DynamicFrames.Any(frame =>
                HasValidWorldTransforms(frame, _targetSkeleton) == false))
        {
            return LayerBuildResult.Failure(CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .LayerResultWorldTransformInvalid));
        }

        var impact = new AnimationWorkbenchLayerImpact(
            request.Mode,
            request.ReferencePose,
            request.Weight,
            selectedIndexes.Count,
            output.DynamicFrames.Count,
            output.Duration,
            RatesDiffer(
                animationA.Timebase!.FramesPerSecond,
                animationB.Timebase!.FramesPerSecond) ||
            animationA.DynamicFrames.Count != animationB.DynamicFrames.Count ||
            animationA.Duration != animationB.Duration);
        return new LayerBuildResult(
            output,
            impact,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    private static void ApplyLayerTransform(
        AnimationClip.KeyFrame output,
        AnimationClip.KeyFrame sourceB,
        int boneIndex,
        AnimationWorkbenchLayerRequest request,
        GameSkeleton skeleton,
        AnimationClip.KeyFrame animationBFirstFrame)
    {
        var weight = (float)request.Weight;
        if (request.Mode == AnimationWorkbenchLayerMode.Override)
        {
            output.Position[boneIndex] = Vector3.Lerp(
                output.Position[boneIndex],
                sourceB.Position[boneIndex],
                weight);
            var rotation = Quaternion.Slerp(
                output.Rotation[boneIndex],
                sourceB.Rotation[boneIndex],
                weight);
            rotation.Normalize();
            output.Rotation[boneIndex] = rotation;
            output.Scale[boneIndex] = Vector3.Lerp(
                output.Scale[boneIndex],
                sourceB.Scale[boneIndex],
                weight);
            return;
        }

        var referencePosition = request.ReferencePose ==
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame
                ? animationBFirstFrame.Position[boneIndex]
                : skeleton.Translation[boneIndex];
        var referenceRotation = request.ReferencePose ==
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame
                ? animationBFirstFrame.Rotation[boneIndex]
                : skeleton.Rotation[boneIndex];
        var referenceScale = request.ReferencePose ==
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame
                ? animationBFirstFrame.Scale[boneIndex]
                : new Vector3(skeleton.Scale[boneIndex]);

        output.Position[boneIndex] +=
            (sourceB.Position[boneIndex] - referencePosition) * weight;
        var referenceMatrix = Matrix.CreateFromQuaternion(referenceRotation);
        var sourceMatrix = Matrix.CreateFromQuaternion(sourceB.Rotation[boneIndex]);
        var delta = Quaternion.CreateFromRotationMatrix(
            Matrix.Invert(referenceMatrix) * sourceMatrix);
        delta.Normalize();
        var weightedDelta = Quaternion.Slerp(
            Quaternion.Identity,
            delta,
            weight);
        weightedDelta.Normalize();
        var resultRotation = Quaternion.CreateFromRotationMatrix(
            Matrix.CreateFromQuaternion(output.Rotation[boneIndex]) *
            Matrix.CreateFromQuaternion(weightedDelta));
        resultRotation.Normalize();
        output.Rotation[boneIndex] = resultRotation;
        output.Scale[boneIndex] +=
            (sourceB.Scale[boneIndex] - referenceScale) * weight;
    }

    private static bool TryValidateLayerSource(
        SourceSnapshot source,
        AnimationWorkbenchSourceSlot slot,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        var animation = source.Animation;
        if (animation.DynamicFrames.Count == 0)
        {
            diagnostic = CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerSourceEmpty,
                slot);
            return false;
        }
        if (animation.Timebase == null)
        {
            diagnostic = CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerSourceDurationInvalid,
                slot);
            return false;
        }
        if (animation.DynamicFrames.Any(frame =>
                HasValidFrameTransforms(frame, source.SkeletonBoneCount) == false))
        {
            diagnostic = CreateLayerDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerSourceTransformInvalid,
                slot);
            return false;
        }
        diagnostic = null;
        return true;
    }

    private static int[] FindBoneIndexes(GameSkeleton skeleton, string name) =>
        skeleton.BoneNames
            .Select((boneName, index) => (boneName, index))
            .Where(item => string.Equals(
                item.boneName,
                name,
                StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();

    private static bool HasValidWorldTransforms(
        AnimationClip.KeyFrame frame,
        GameSkeleton skeleton)
    {
        var worldTransforms = new Matrix[skeleton.BoneCount];
        var visitStates = new byte[skeleton.BoneCount];
        for (var boneIndex = 0;
             boneIndex < skeleton.BoneCount;
             boneIndex++)
        {
            if (TryBuildWorldTransform(
                    boneIndex,
                    frame,
                    skeleton,
                    worldTransforms,
                    visitStates,
                    out _) == false)
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryBuildWorldTransform(
        int boneIndex,
        AnimationClip.KeyFrame frame,
        GameSkeleton skeleton,
        Matrix[] worldTransforms,
        byte[] visitStates,
        out Matrix worldTransform)
    {
        if (visitStates[boneIndex] == 2)
        {
            worldTransform = worldTransforms[boneIndex];
            return true;
        }
        if (visitStates[boneIndex] == 1)
        {
            worldTransform = default;
            return false;
        }

        visitStates[boneIndex] = 1;
        var localTransform =
            Matrix.CreateScale(frame.Scale[boneIndex]) *
            Matrix.CreateFromQuaternion(frame.Rotation[boneIndex]) *
            Matrix.CreateTranslation(frame.Position[boneIndex]);
        if (IsFinite(localTransform) == false)
        {
            worldTransform = default;
            return false;
        }

        var parentIndex = skeleton.GetParentBoneIndex(boneIndex);
        if (parentIndex == -1)
        {
            worldTransform = localTransform;
        }
        else
        {
            if (parentIndex < 0 ||
                parentIndex >= skeleton.BoneCount ||
                TryBuildWorldTransform(
                    parentIndex,
                    frame,
                    skeleton,
                    worldTransforms,
                    visitStates,
                    out var parentTransform) == false)
            {
                worldTransform = default;
                return false;
            }
            worldTransform = localTransform * parentTransform;
        }

        if (IsFinite(worldTransform) == false)
            return false;
        worldTransforms[boneIndex] = worldTransform;
        visitStates[boneIndex] = 2;
        return true;
    }

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) &&
        float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) &&
        float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) &&
        float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) &&
        float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) &&
        float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) &&
        float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) &&
        float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) &&
        float.IsFinite(value.M44);

    private AnimationWorkbenchBoneMaskResult CreateBoneMaskFailure(
        AnimationWorkbenchDiagnosticCode code,
        string? boneName = null) => new(
        false,
        CreateState(),
        null,
        [CreateLayerDiagnostic(code, boneName: boneName)]);

    private AnimationWorkbenchLayerResult CreateLayerSuccess() => new(
        true,
        CreateState(),
        _layerPreviewImpact,
        Array.Empty<AnimationWorkbenchDiagnostic>());

    private AnimationWorkbenchLayerResult CreateLayerFailure(
        AnimationWorkbenchDiagnosticCode code) => new(
        false,
        CreateState(),
        null,
        [CreateLayerDiagnostic(code)]);

    private static AnimationWorkbenchDiagnostic CreateLayerDiagnostic(
        AnimationWorkbenchDiagnosticCode code,
        AnimationWorkbenchSourceSlot? source = null,
        string? boneName = null) => new(
        code,
        AnimationWorkbenchDiagnosticSeverity.Error,
        source,
        BoneName: boneName);

    private void ClearLayerPreview()
    {
        _layerPreviewResult = null;
        _layerPreviewImpact = null;
        ClearLayerMetaDataPreview();
    }

    private sealed record LayerBuildResult(
        AnimationClip? Animation,
        AnimationWorkbenchLayerImpact? Impact,
        IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics)
    {
        public static LayerBuildResult Failure(
            AnimationWorkbenchDiagnostic diagnostic) => new(
            null,
            null,
            [diagnostic]);
    }
}
