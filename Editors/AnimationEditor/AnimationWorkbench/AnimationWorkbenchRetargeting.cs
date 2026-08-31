using System.IO;
using System.Collections.ObjectModel;
using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.Settings;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchRetargetConfidence
{
    None,
    Low,
    Medium,
    High,
    Manual,
}

public sealed record AnimationWorkbenchRetargetBoneMapping(
    int TargetBoneIndex,
    string TargetBoneName,
    int? SourceBoneIndex,
    string? SourceBoneName,
    AnimationWorkbenchRetargetConfidence Confidence,
    bool IsCoreBone,
    bool ApplyTranslation);

public sealed record AnimationWorkbenchRetargetMappingResult(
    bool Succeeded,
    IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> Mappings,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed record AnimationWorkbenchRetargetRequest(
    AnimationWorkbenchSourceSlot Source,
    IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> Mappings);

public sealed record AnimationWorkbenchRetargetResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> Mappings,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed record AnimationWorkbenchRetargetProfileResult(
    bool Succeeded,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed record AnimationWorkbenchRetargetSourceBone(
    int Index,
    string Name);

public sealed record AnimationWorkbenchRetargetSourceOption(
    int? Index,
    string Name);

public sealed partial class AnimationWorkbenchDocument
{
    private SourceSnapshot? _retargetPreviewResult;
    private SourceSnapshot? _retargetedAnimationA;
    private SourceSnapshot? _retargetedAnimationB;
    private AnimationWorkbenchSourceSlot? _retargetPreviewSource;
    private IReadOnlyList<AnimationWorkbenchRetargetBoneMapping>
        _retargetPreviewMappings =
            Array.Empty<AnimationWorkbenchRetargetBoneMapping>();
    private IReadOnlyList<AnimationWorkbenchDiagnostic>
        _retargetPreviewDiagnostics =
            Array.Empty<AnimationWorkbenchDiagnostic>();
    private long _retargetPreviewVersion;

    internal long ActiveRetargetPreviewVersion =>
        _retargetPreviewResult == null ? 0 : _retargetPreviewVersion;

    public AnimationWorkbenchRetargetMappingResult CreateRetargetMapping(
        AnimationWorkbenchSourceSlot sourceSlot)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);

        var source = sourceSlot == AnimationWorkbenchSourceSlot.AnimationA
            ? _animationA
            : _animationB;
        if (source == null)
        {
            return new AnimationWorkbenchRetargetMappingResult(
                false,
                Array.Empty<AnimationWorkbenchRetargetBoneMapping>(),
                [new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode.RetargetSourceMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    sourceSlot)]);
        }

        if (_targetSkeleton == null)
        {
            return new AnimationWorkbenchRetargetMappingResult(
                false,
                Array.Empty<AnimationWorkbenchRetargetBoneMapping>(),
                [new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    sourceSlot)]);
        }

        var mappingResult = HumanoidBoneMapper.CreateMappings(
            CreateMappingSkeleton(source.Skeleton),
            CreateMappingSkeleton(_targetSkeleton));
        if (RetargetSkeletonsAreEquivalent(
                source.Skeleton,
                _targetSkeleton))
        {
            return new AnimationWorkbenchRetargetMappingResult(
                true,
                Enumerable.Range(0, _targetSkeleton.BoneCount)
                    .Select(index =>
                        new AnimationWorkbenchRetargetBoneMapping(
                            index,
                            _targetSkeleton.BoneNames[index],
                            index,
                            source.Skeleton.BoneNames[index],
                            AnimationWorkbenchRetargetConfidence.High,
                            mappingResult.CoreTargetBoneIndices.Contains(index),
                            mappingResult.TranslationTargetBoneIndices.Contains(
                                index)))
                    .ToArray(),
                Array.Empty<AnimationWorkbenchDiagnostic>());
        }
        var mappingsByTarget = mappingResult.Mappings.ToDictionary(
            item => item.TargetBoneIndex);
        var mappings = Enumerable.Range(0, _targetSkeleton.BoneCount)
            .Select(targetBoneIndex =>
            {
                if (!mappingsByTarget.TryGetValue(
                        targetBoneIndex,
                        out var mapping))
                {
                    return new AnimationWorkbenchRetargetBoneMapping(
                        targetBoneIndex,
                        _targetSkeleton.BoneNames[targetBoneIndex],
                        null,
                        null,
                        AnimationWorkbenchRetargetConfidence.None,
                        mappingResult.CoreTargetBoneIndices.Contains(
                            targetBoneIndex),
                        false);
                }

                return new AnimationWorkbenchRetargetBoneMapping(
                    targetBoneIndex,
                    _targetSkeleton.BoneNames[targetBoneIndex],
                    mapping.SourceBoneIndex,
                    source.Skeleton.BoneNames[mapping.SourceBoneIndex],
                    ConvertConfidence(mapping.Confidence),
                    mapping.IsCoreBone,
                    mappingResult.TranslationTargetBoneIndices.Contains(
                        targetBoneIndex));
            })
            .ToArray();
        var diagnostics = mappings
            .Where(item => item.SourceBoneIndex == null && !item.IsCoreBone)
            .Select(item => new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.RetargetNonCoreBoneUnmapped,
                AnimationWorkbenchDiagnosticSeverity.Warning,
                sourceSlot,
                BoneName: item.TargetBoneName))
            .ToArray();

        return new AnimationWorkbenchRetargetMappingResult(
            true,
            mappings,
            diagnostics);
    }

    public IReadOnlyList<AnimationWorkbenchRetargetSourceBone>
        GetRetargetSourceBones(AnimationWorkbenchSourceSlot sourceSlot)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        var source = sourceSlot == AnimationWorkbenchSourceSlot.AnimationA
            ? _animationA
            : _animationB;
        if (source == null)
            return Array.Empty<AnimationWorkbenchRetargetSourceBone>();

        return Enumerable.Range(0, source.Skeleton.BoneCount)
            .Select(index => new AnimationWorkbenchRetargetSourceBone(
                index,
                source.Skeleton.BoneNames[index]))
            .ToArray();
    }

    internal AnimationWorkbenchRetargetMappingResult LoadRetargetProfile(
        AnimationWorkbenchSourceSlot sourceSlot,
        CharacterRetargetProfileStore profileStore)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        var automatic = CreateRetargetMapping(sourceSlot);
        var source = sourceSlot == AnimationWorkbenchSourceSlot.AnimationA
            ? _animationA
            : _animationB;
        if (!automatic.Succeeded || source == null || _targetSkeleton == null)
            return automatic;

        var loaded = profileStore.Load(
            source.Skeleton.SkeletonName,
            _targetSkeleton.SkeletonName,
            CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                source.Skeleton),
            CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                _targetSkeleton));
        if (loaded.Status != CharacterRetargetProfileLoadStatus.Loaded)
        {
            return new AnimationWorkbenchRetargetMappingResult(
                false,
                automatic.Mappings,
                [new AnimationWorkbenchDiagnostic(
                    loaded.Status ==
                        CharacterRetargetProfileLoadStatus.ReadFailed
                            ? AnimationWorkbenchDiagnosticCode
                                .RetargetProfileReadFailed
                            : AnimationWorkbenchDiagnosticCode
                                .RetargetProfileNotFound,
                    loaded.Status ==
                        CharacterRetargetProfileLoadStatus.ReadFailed
                            ? AnimationWorkbenchDiagnosticSeverity.Error
                            : AnimationWorkbenchDiagnosticSeverity.Warning,
                    sourceSlot)]);
        }

        profileStore.TryLoadSettings(
            source.Skeleton.SkeletonName,
            _targetSkeleton.SkeletonName,
            CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                source.Skeleton),
            CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                _targetSkeleton),
            out var settings);

        var restored = automatic.Mappings
            .Select(mapping => loaded.Mappings.TryGetValue(
                    mapping.TargetBoneIndex,
                    out var sourceBoneIndex)
                ? mapping with
                {
                    SourceBoneIndex = sourceBoneIndex,
                    SourceBoneName = sourceBoneIndex >= 0 &&
                        sourceBoneIndex < source.Skeleton.BoneCount
                            ? source.Skeleton.BoneNames[sourceBoneIndex]
                            : null,
                    Confidence =
                        AnimationWorkbenchRetargetConfidence.Manual,
                    ApplyTranslation = settings.TryGetValue(
                            mapping.TargetBoneIndex,
                            out var boneSettings)
                        ? boneSettings.ApplyTranslation
                        : mapping.ApplyTranslation,
                }
                : mapping with
                {
                    SourceBoneIndex = null,
                    SourceBoneName = null,
                    Confidence = AnimationWorkbenchRetargetConfidence.None,
                })
            .ToArray();
        return new AnimationWorkbenchRetargetMappingResult(
            true,
            restored,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    internal AnimationWorkbenchRetargetProfileResult SaveRetargetProfile(
        AnimationWorkbenchSourceSlot sourceSlot,
        IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> mappings,
        CharacterRetargetProfileStore profileStore)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(profileStore);
        var source = sourceSlot == AnimationWorkbenchSourceSlot.AnimationA
            ? _animationA
            : _animationB;
        if (source == null || _targetSkeleton == null)
        {
            return new AnimationWorkbenchRetargetProfileResult(
                false,
                [new AnimationWorkbenchDiagnostic(
                    source == null
                        ? AnimationWorkbenchDiagnosticCode
                            .RetargetSourceMissing
                        : AnimationWorkbenchDiagnosticCode
                            .TargetSkeletonMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    sourceSlot)]);
        }

        var saved = profileStore.Save(
            source.Skeleton.SkeletonName,
            _targetSkeleton.SkeletonName,
            CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                source.Skeleton),
            CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                _targetSkeleton),
            mappings
                .Where(item => item.SourceBoneIndex.HasValue)
                .ToDictionary(
                    item => item.TargetBoneIndex,
                    item => item.SourceBoneIndex!.Value),
            mappings.ToDictionary(
                item => item.TargetBoneIndex,
                item => new CharacterRetargetBoneSettings(
                    BoneLengthMultiplier: 1,
                    RotationOffsetX: 0,
                    RotationOffsetY: 0,
                    RotationOffsetZ: 0,
                    TranslationOffsetX: 0,
                    TranslationOffsetY: 0,
                    TranslationOffsetZ: 0,
                    ForceSnapToWorld: false,
                    FreezeTranslation: false,
                    FreezeRotation: false,
                    FreezeRotationZ: false,
                    ApplyTranslation: item.ApplyTranslation,
                    ApplyRotation: true,
                    RelativeTargetBoneIndex: null)));
        return new AnimationWorkbenchRetargetProfileResult(
            saved,
            saved
                ? Array.Empty<AnimationWorkbenchDiagnostic>()
                : [new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode
                        .RetargetProfileWriteFailed,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    sourceSlot)]);
    }

    public AnimationWorkbenchRetargetResult PreviewRetarget(
        AnimationWorkbenchRetargetRequest request)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mappings);

        var activePreview = GetActiveEditPreviewDiagnostic(
            EditPreviewKind.Retarget);
        if (activePreview != null)
        {
            return CreateRetargetFailure(
                request.Mappings,
                new AnimationWorkbenchDiagnostic(
                    activePreview.Value,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    request.Source));
        }

        var source = request.Source == AnimationWorkbenchSourceSlot.AnimationA
            ? _animationA
            : _animationB;
        if (source == null)
        {
            return CreateRetargetFailure(
                request.Mappings,
                new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode.RetargetSourceMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    request.Source));
        }
        if (_targetSkeleton == null)
        {
            return CreateRetargetFailure(
                request.Mappings,
                new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    request.Source));
        }

        var diagnostics = ValidateRetargetMappings(
            source,
            _targetSkeleton,
            request);
        if (diagnostics.Any(item =>
                item.Severity == AnimationWorkbenchDiagnosticSeverity.Error))
        {
            return new AnimationWorkbenchRetargetResult(
                false,
                CreateState(),
                request.Mappings.ToArray(),
                diagnostics);
        }

        AnimationClip animation;
        try
        {
            if (RetargetSkeletonsAreEquivalent(
                    source.Skeleton,
                    _targetSkeleton))
            {
                animation = source.Animation.Clone();
            }
            else
            {
                var targetBones = BuildRetargetBoneTree(_targetSkeleton);
                foreach (var mapping in request.Mappings.Where(item =>
                             item.SourceBoneIndex.HasValue))
                {
                    var bone = SkeletonBoneNodeHelper.GetNodeFromId(
                        mapping.TargetBoneIndex,
                        targetBones);
                    if (bone == null)
                        continue;
                    bone.HasMapping = true;
                    bone.MappedIndex = mapping.SourceBoneIndex!.Value;
                    bone.ApplyTranslation = mapping.ApplyTranslation;
                }

                var service = new AnimationRemapperService(
                    new AnimationGenerationSettings
                    {
                        ApplyRelativeScale = false,
                    },
                    targetBones);
                animation = service.ReMapAnimation(
                    source.Skeleton,
                    _targetSkeleton,
                    source.Animation.Clone());
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidDataException)
        {
            return CreateRetargetFailure(
                request.Mappings,
                new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode
                        .RetargetResultTransformInvalid,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    request.Source));
        }

        if (!AnimationTransformsAreValid(
                animation,
                _targetSkeleton.BoneCount))
        {
            return CreateRetargetFailure(
                request.Mappings,
                new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode
                        .RetargetResultTransformInvalid,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    request.Source));
        }

        _retargetPreviewResult = source.WithAnimationAndSkeleton(
            animation,
            _targetSkeleton);
        _retargetPreviewSource = request.Source;
        _retargetPreviewMappings = request.Mappings.ToArray();
        _retargetPreviewDiagnostics = diagnostics.ToArray();
        _retargetPreviewVersion = _retargetPreviewVersion == long.MaxValue
            ? 1
            : _retargetPreviewVersion + 1;
        RefreshSelectedResultPreview();
        return new AnimationWorkbenchRetargetResult(
            true,
            CreateState(),
            request.Mappings.ToArray(),
            diagnostics);
    }

    public AnimationWorkbenchRetargetResult CommitRetargetPreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_retargetPreviewResult == null ||
            !_retargetPreviewSource.HasValue)
        {
            return CreateRetargetFailure(
                Array.Empty<AnimationWorkbenchRetargetBoneMapping>(),
                new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode.RetargetPreviewMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error));
        }

        var source = _retargetPreviewSource.Value;
        var prepared = _retargetPreviewResult.Clone();
        var mappings = _retargetPreviewMappings.ToArray();
        var diagnostics = _retargetPreviewDiagnostics.ToArray();
        ClearRetargetPreview();
        if (source == AnimationWorkbenchSourceSlot.AnimationA)
        {
            _retargetedAnimationA = prepared;
            _result = prepared.Clone();
            ResetDocumentHistory();
        }
        else
        {
            _retargetedAnimationB = prepared;
        }
        RefreshSelectedResultPreview();
        return new AnimationWorkbenchRetargetResult(
            true,
            CreateState(),
            mappings,
            diagnostics);
    }

    public AnimationWorkbenchRetargetResult CancelRetargetPreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_retargetPreviewResult == null)
        {
            return CreateRetargetFailure(
                Array.Empty<AnimationWorkbenchRetargetBoneMapping>(),
                new AnimationWorkbenchDiagnostic(
                    AnimationWorkbenchDiagnosticCode.RetargetPreviewMissing,
                    AnimationWorkbenchDiagnosticSeverity.Error));
        }

        var mappings = _retargetPreviewMappings.ToArray();
        ClearRetargetPreview();
        RefreshSelectedResultPreview();
        return new AnimationWorkbenchRetargetResult(
            true,
            CreateState(),
            mappings,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    private IReadOnlyList<AnimationWorkbenchDiagnostic>
        ValidateRetargetMappings(
            SourceSnapshot source,
            GameWorld.Core.Animation.GameSkeleton targetSkeleton,
            AnimationWorkbenchRetargetRequest request)
    {
        var diagnostics = new List<AnimationWorkbenchDiagnostic>();
        if (!AnimationTransformsAreValid(
                source.Animation,
                source.Skeleton.BoneCount))
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .RetargetSourceTransformInvalid,
                AnimationWorkbenchDiagnosticSeverity.Error,
                request.Source));
        }

        if (!SkeletonBindPoseIsCompatible(source.Skeleton) ||
            !SkeletonBindPoseIsCompatible(targetSkeleton))
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .RetargetBindPoseIncompatible,
                AnimationWorkbenchDiagnosticSeverity.Error,
                request.Source));
        }

        foreach (var duplicate in request.Mappings
                     .GroupBy(item => item.TargetBoneIndex)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .RetargetMappingTargetDuplicate,
                AnimationWorkbenchDiagnosticSeverity.Error,
                request.Source,
                BoneName: duplicate.Key >= 0 &&
                    duplicate.Key < targetSkeleton.BoneCount
                        ? targetSkeleton.BoneNames[duplicate.Key]
                        : null));
        }

        var validMappings = request.Mappings
            .Where(item => item.TargetBoneIndex >= 0 &&
                           item.TargetBoneIndex < targetSkeleton.BoneCount)
            .GroupBy(item => item.TargetBoneIndex)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        foreach (var mapping in request.Mappings.Where(item =>
                     item.TargetBoneIndex < 0 ||
                     item.TargetBoneIndex >= targetSkeleton.BoneCount ||
                     item.SourceBoneIndex is < 0 ||
                     item.SourceBoneIndex >= source.Skeleton.BoneCount))
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .RetargetMappingIndexInvalid,
                AnimationWorkbenchDiagnosticSeverity.Error,
                request.Source));
        }

        var coreIndices = HumanoidBoneMapper.CreateMappings(
                CreateMappingSkeleton(source.Skeleton),
                CreateMappingSkeleton(targetSkeleton))
            .CoreTargetBoneIndices;
        for (var targetIndex = 0;
             targetIndex < targetSkeleton.BoneCount;
             targetIndex++)
        {
            validMappings.TryGetValue(targetIndex, out var mapping);
            var isMapped = mapping?.SourceBoneIndex is >= 0 &&
                mapping.SourceBoneIndex < source.Skeleton.BoneCount;
            if (isMapped)
                continue;

            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                coreIndices.Contains(targetIndex)
                    ? AnimationWorkbenchDiagnosticCode
                        .RetargetCoreBoneUnmapped
                    : AnimationWorkbenchDiagnosticCode
                        .RetargetNonCoreBoneUnmapped,
                coreIndices.Contains(targetIndex)
                    ? AnimationWorkbenchDiagnosticSeverity.Error
                    : AnimationWorkbenchDiagnosticSeverity.Warning,
                request.Source,
                BoneName: targetSkeleton.BoneNames[targetIndex]));
        }

        if (diagnostics.Any(item =>
                item.Severity == AnimationWorkbenchDiagnosticSeverity.Error))
        {
            return diagnostics;
        }

        foreach (var mapping in validMappings.Values.Where(item =>
                     item.SourceBoneIndex.HasValue))
        {
            var targetParent = targetSkeleton.GetParentBoneIndex(
                mapping.TargetBoneIndex);
            while (targetParent >= 0 &&
                   (!validMappings.TryGetValue(
                        targetParent,
                        out var parentMapping) ||
                    !parentMapping.SourceBoneIndex.HasValue))
            {
                targetParent = targetSkeleton.GetParentBoneIndex(targetParent);
            }
            if (targetParent < 0)
                continue;

            var mappedParent = validMappings[targetParent].SourceBoneIndex!.Value;
            if (IsAncestorOrSelf(
                    source.Skeleton,
                    mappedParent,
                    mapping.SourceBoneIndex!.Value))
            {
                continue;
            }

            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.RetargetParentConflict,
                AnimationWorkbenchDiagnosticSeverity.Error,
                request.Source,
                BoneName: targetSkeleton.BoneNames[
                    mapping.TargetBoneIndex]));
        }

        return diagnostics;
    }

    private static bool IsAncestorOrSelf(
        GameWorld.Core.Animation.GameSkeleton skeleton,
        int ancestorIndex,
        int boneIndex)
    {
        var current = boneIndex;
        while (current >= 0)
        {
            if (current == ancestorIndex)
                return true;
            current = skeleton.GetParentBoneIndex(current);
        }
        return false;
    }

    private static bool SkeletonBindPoseIsCompatible(
        GameWorld.Core.Animation.GameSkeleton skeleton)
    {
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var local = Matrix.CreateScale(skeleton.Scale[boneIndex]) *
                Matrix.CreateFromQuaternion(skeleton.Rotation[boneIndex]) *
                Matrix.CreateTranslation(skeleton.Translation[boneIndex]);
            var world = skeleton.GetWorldTransform(boneIndex);
            if (!MatrixIsFinite(local) ||
                !MatrixIsFinite(world) ||
                MathF.Abs(local.Determinant()) <= 0.000001f ||
                MathF.Abs(world.Determinant()) <= 0.000001f)
            {
                return false;
            }
        }
        return true;
    }

    private static bool MatrixIsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static bool AnimationTransformsAreValid(
        AnimationClip animation,
        int expectedBoneCount) =>
        animation.DynamicFrames.Count != 0 &&
        animation.DynamicFrames.All(frame =>
            frame.Position.Count == expectedBoneCount &&
            frame.Rotation.Count == expectedBoneCount &&
            frame.Scale.Count == expectedBoneCount &&
            frame.Position.All(value =>
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z)) &&
            frame.Rotation.All(value =>
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z) &&
                float.IsFinite(value.W) &&
                value.LengthSquared() > float.Epsilon) &&
            frame.Scale.All(value =>
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z) &&
                MathF.Abs(value.X) > float.Epsilon &&
                MathF.Abs(value.Y) > float.Epsilon &&
                MathF.Abs(value.Z) > float.Epsilon));

    private static ObservableCollection<SkeletonBoneNode_new>
        BuildRetargetBoneTree(GameSkeleton skeleton)
    {
        var nodes = Enumerable.Range(0, skeleton.BoneCount)
            .ToDictionary(
                index => index,
                index => new SkeletonBoneNode_new(
                    skeleton.BoneNames[index],
                    index,
                    skeleton.GetParentBoneIndex(index)));
        var roots = new ObservableCollection<SkeletonBoneNode_new>();
        foreach (var node in nodes.Values.OrderBy(item => item.BoneIndex))
        {
            if (node.ParnetBoneIndex < 0)
                roots.Add(node);
            else
                nodes[node.ParnetBoneIndex].Children.Add(node);
        }
        return roots;
    }

    private AnimationWorkbenchRetargetResult CreateRetargetFailure(
        IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> mappings,
        params AnimationWorkbenchDiagnostic[] diagnostics) => new(
            false,
            CreateState(),
            mappings.ToArray(),
            diagnostics);

    private void ClearRetargetPreview()
    {
        _retargetPreviewResult = null;
        _retargetPreviewSource = null;
        _retargetPreviewMappings =
            Array.Empty<AnimationWorkbenchRetargetBoneMapping>();
        _retargetPreviewDiagnostics =
            Array.Empty<AnimationWorkbenchDiagnostic>();
    }

    private static AnimationFile CreateMappingSkeleton(
        GameWorld.Core.Animation.GameSkeleton skeleton) => new()
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = skeleton.SkeletonName,
            },
            Bones = Enumerable.Range(0, skeleton.BoneCount)
                .Select(index => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = skeleton.BoneNames[index],
                    ParentId = skeleton.GetParentBoneIndex(index),
                })
                .ToArray(),
        };

    private static AnimationWorkbenchRetargetConfidence ConvertConfidence(
        HumanoidBoneMappingConfidence confidence) => confidence switch
        {
            HumanoidBoneMappingConfidence.Low =>
                AnimationWorkbenchRetargetConfidence.Low,
            HumanoidBoneMappingConfidence.Medium =>
                AnimationWorkbenchRetargetConfidence.Medium,
            HumanoidBoneMappingConfidence.High =>
                AnimationWorkbenchRetargetConfidence.High,
            _ => throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                null),
        };

    private static bool RetargetSkeletonsAreEquivalent(
        GameSkeleton source,
        GameSkeleton target)
    {
        if (source.BoneCount != target.BoneCount)
            return false;
        for (var boneIndex = 0;
             boneIndex < source.BoneCount;
             boneIndex++)
        {
            if (!string.Equals(
                    source.BoneNames[boneIndex],
                    target.BoneNames[boneIndex],
                    StringComparison.OrdinalIgnoreCase) ||
                source.GetParentBoneIndex(boneIndex) !=
                    target.GetParentBoneIndex(boneIndex) ||
                Vector3.Distance(
                    source.Translation[boneIndex],
                    target.Translation[boneIndex]) > 0.00001f ||
                MathF.Abs(source.Scale[boneIndex] -
                          target.Scale[boneIndex]) > 0.00001f)
            {
                return false;
            }

            var sourceRotation = Quaternion.Normalize(
                source.Rotation[boneIndex]);
            var targetRotation = Quaternion.Normalize(
                target.Rotation[boneIndex]);
            if (1 - MathF.Abs(Quaternion.Dot(
                    sourceRotation,
                    targetRotation)) > 0.00001f)
            {
                return false;
            }
        }
        return true;
    }
}
