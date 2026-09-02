using System.IO;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed record TrustedAnimationPreviewResourceState(
    string Path,
    string Source,
    bool IsResolved,
    string Diagnostic)
{
    public static TrustedAnimationPreviewResourceState Empty { get; } =
        new(string.Empty, string.Empty, false, string.Empty);
}

public sealed record TrustedAnimationPreviewState(
    TrustedAnimationPreviewResourceState Model,
    TrustedAnimationPreviewResourceState Skeleton,
    TrustedAnimationPreviewResourceState Animation,
    bool IsReady,
    int MeshCount)
{
    public static TrustedAnimationPreviewState Empty { get; } = new(
        TrustedAnimationPreviewResourceState.Empty,
        TrustedAnimationPreviewResourceState.Empty,
        TrustedAnimationPreviewResourceState.Empty,
        false,
        0);
}

public sealed record TrustedAnimationPreviewViewportResult(
    bool IsSuccess,
    int MeshCount,
    TrustedAnimationPreviewResourceKind FailureResource,
    string Diagnostic)
{
    public static TrustedAnimationPreviewViewportResult Success(
        int meshCount) => new(
            true,
            meshCount,
            TrustedAnimationPreviewResourceKind.None,
            string.Empty);

    public static TrustedAnimationPreviewViewportResult Failure(
        TrustedAnimationPreviewResourceKind resource,
        string diagnostic) => new(false, 0, resource, diagnostic);
}

public sealed record TrustedAnimationPlaybackState(
    bool HasAnimation,
    bool IsPlaying,
    int CurrentFrame,
    int FrameCount,
    double CurrentTimeSeconds,
    double DurationSeconds,
    double FramesPerSecond,
    bool IsLooping = true,
    bool HasStaticFrame = false,
    bool IsStaticPose = false,
    int PartCount = 1)
{
    public static TrustedAnimationPlaybackState Empty { get; } = new(
        false,
        false,
        0,
        0,
        0,
        0,
        0,
        true,
        false,
        false,
        0);
}

public enum TrustedAnimationPreviewResourceKind
{
    None,
    Model,
    Skeleton,
    Animation,
}

public interface ITrustedAnimationPreviewViewport : IDisposable
{
    IWpfGame GameWorld { get; }

    TrustedAnimationPlaybackState PlaybackState { get; }

    event EventHandler? PlaybackChanged;

    TrustedAnimationPreviewViewportResult Load(
        PackFile model,
        PackFile skeleton);

    TrustedAnimationPreviewViewportResult LoadAnimation(
        AnimationFile animation,
        string path);

    void Clear();

    void SetModelVisible(bool isVisible);

    void SetSkeletonVisible(bool isVisible);

    void FocusModel();

    void ShowFront();

    void ResetCamera();

    void Play();

    void Pause();

    void PreviousFrame();

    void NextFrame();

    void SetLooping(bool isLooping);

    void Seek(double timeSeconds);
}

public sealed class TrustedAnimationPreviewFeatureSession(
    ITrustedAnimationPreviewViewport viewport,
    IPackFileService packFileService)
{
    public TrustedAnimationPreviewState State { get; private set; } =
        TrustedAnimationPreviewState.Empty;

    public event EventHandler? StateChanged;

    public TrustedAnimationSkeletonIdentity? SkeletonIdentity
    {
        get;
        private set;
    }

    public void LoadModel(PackFile model)
    {
        ArgumentNullException.ThrowIfNull(model);
        viewport.Clear();
        SkeletonIdentity = null;

        var modelState = CreateResourceState(model);
        if (!model.Name.EndsWith(
                ".rigid_model_v2",
                StringComparison.OrdinalIgnoreCase))
        {
            SetFailure(
                modelState with
                {
                    Diagnostic = Localize(
                        "AnimationWorkbench.TrustedPreview.ModelTypeInvalid"),
                });
            return;
        }

        string skeletonName;
        try
        {
            var header = ModelFactory.Create().LoadOnlyHeaders(
                model.DataSource.ReadData()).Header;
            if (!string.Equals(
                    header.FileType,
                    "RMV2",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }
            skeletonName = header.SkeletonName.Trim();
        }
        catch (Exception exception)
        {
            SetFailure(
                modelState with
                {
                    Diagnostic = string.Format(
                        Localize(
                            "AnimationWorkbench.TrustedPreview.ModelUnreadableDetails"),
                        exception.Message),
                });
            return;
        }

        if (string.IsNullOrWhiteSpace(skeletonName))
        {
            SetFailure(
                modelState with
                {
                    Diagnostic = Localize(
                        "AnimationWorkbench.TrustedPreview.SkeletonUndeclared"),
                });
            return;
        }

        var skeletonPath =
            $"animations\\skeletons\\{skeletonName}.anim";
        var skeleton = TrustedAnimationEffectiveResources.Find(
                packFileService,
                skeletonPath)?.File ??
            packFileService.FindFile(skeletonPath);
        if (skeleton == null)
        {
            SetState(new TrustedAnimationPreviewState(
                modelState,
                new TrustedAnimationPreviewResourceState(
                    skeletonPath,
                    string.Empty,
                    false,
                    Localize(
                        "AnimationWorkbench.TrustedPreview.SkeletonMissing")),
                TrustedAnimationPreviewResourceState.Empty,
                false,
                0));
            return;
        }

        var skeletonState = CreateResourceState(skeleton);
        try
        {
            var skeletonFile = AnimationFile.Create(skeleton);
            var actualSkeletonName = skeletonFile.Header.SkeletonName.Trim();
            if (!string.Equals(
                    actualSkeletonName,
                    skeletonName,
                    StringComparison.OrdinalIgnoreCase))
            {
                SetState(new TrustedAnimationPreviewState(
                    modelState,
                    skeletonState with
                    {
                        Diagnostic = string.Format(
                            Localize(
                                "AnimationWorkbench.TrustedPreview.SkeletonIdentityMismatch"),
                            skeletonName,
                            actualSkeletonName),
                    },
                    TrustedAnimationPreviewResourceState.Empty,
                    false,
                    0));
                return;
            }

            if (skeletonFile.Bones.Length == 0)
            {
                throw new InvalidDataException(
                    Localize(
                        "AnimationWorkbench.TrustedPreview.SkeletonEmpty"));
            }

            SkeletonIdentity = TrustedAnimationSkeletonIdentity.Create(
                skeleton,
                skeletonState.Path,
                skeletonState.Source);
        }
        catch (Exception exception)
        {
            SetState(new TrustedAnimationPreviewState(
                modelState,
                skeletonState with
                {
                    Diagnostic = string.Format(
                        Localize(
                            "AnimationWorkbench.TrustedPreview.SkeletonUnreadableDetails"),
                        exception.Message),
                },
                TrustedAnimationPreviewResourceState.Empty,
                false,
                0));
            return;
        }

        var viewportResult = viewport.Load(model, skeleton);
        if (!viewportResult.IsSuccess)
        {
            var failedModelState = viewportResult.FailureResource ==
                TrustedAnimationPreviewResourceKind.Model
                    ? modelState with
                    {
                        Diagnostic = viewportResult.Diagnostic,
                    }
                    : modelState;
            var failedSkeletonState = viewportResult.FailureResource ==
                TrustedAnimationPreviewResourceKind.Skeleton
                    ? skeletonState with
                    {
                        Diagnostic = viewportResult.Diagnostic,
                    }
                    : skeletonState;
            SetState(new TrustedAnimationPreviewState(
                failedModelState,
                failedSkeletonState,
                TrustedAnimationPreviewResourceState.Empty,
                false,
                0));
            return;
        }

        SetState(new TrustedAnimationPreviewState(
            modelState,
            skeletonState,
            TrustedAnimationPreviewResourceState.Empty,
            true,
            viewportResult.MeshCount));
    }

    public void LoadAnimation(
        TrustedAnimationCandidate candidate,
        AnimationFile animation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(animation);
        var requiredSkeleton = SkeletonIdentity;
        if (!State.IsReady || requiredSkeleton == null)
        {
            SetAnimationFailure(
                candidate,
                Localize(
                    "AnimationWorkbench.AnimationPicker.ModelRequired"));
            return;
        }

        try
        {
            var effectiveSkeleton = TrustedAnimationEffectiveResources.Find(
                packFileService,
                requiredSkeleton.Path);
            if (effectiveSkeleton == null)
            {
                SetAnimationFailure(
                    candidate,
                    string.Format(
                        Localize(
                            "AnimationWorkbench.TrustedPreview.AnimationBindingMissing"),
                        requiredSkeleton.Path,
                        requiredSkeleton.Source));
                return;
            }

            var currentIdentity = TrustedAnimationSkeletonIdentity.Create(
                effectiveSkeleton.File,
                effectiveSkeleton.Path,
                effectiveSkeleton.Container.Name);
            if (!requiredSkeleton.Matches(currentIdentity))
            {
                SetAnimationFailure(
                    candidate,
                    string.Format(
                        Localize(
                            "AnimationWorkbench.TrustedPreview.AnimationBindingChanged"),
                        requiredSkeleton.Name,
                        requiredSkeleton.Path,
                        requiredSkeleton.Source,
                        currentIdentity.Source));
                return;
            }

            if (animation.Header.Version is not 7 and not 8)
            {
                SetAnimationFailure(
                    candidate,
                    string.Format(
                        Localize(
                            "AnimationWorkbench.TrustedPreview.AnimationVersionUnsupported"),
                        animation.Header.Version,
                        candidate.Path,
                        candidate.SourcePack));
                return;
            }

            if (!TrustedAnimationCompatibility.HasExactBoneIdentity(
                    animation,
                    requiredSkeleton,
                    out _))
            {
                SetAnimationFailure(
                    candidate,
                    CreateCompatibilityDiagnostic(
                        candidate,
                        animation,
                        requiredSkeleton));
                return;
            }

            var viewportResult = viewport.LoadAnimation(
                animation,
                candidate.Path);
            if (!viewportResult.IsSuccess)
            {
                SetAnimationFailure(candidate, viewportResult.Diagnostic);
                return;
            }

            SetState(State with
            {
                Animation = new TrustedAnimationPreviewResourceState(
                    candidate.Path,
                    candidate.SourcePack,
                    true,
                    string.Empty),
            });
        }
        catch (Exception exception)
        {
            SetAnimationFailure(
                candidate,
                string.Format(
                    Localize(
                        "AnimationWorkbench.TrustedPreview.AnimationUnreadableDetails"),
                    candidate.Path,
                    candidate.SourcePack,
                    exception.Message));
        }
    }

    public void ReportAnimationFailure(
        TrustedAnimationCandidate candidate,
        string technicalDetail)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SetAnimationFailure(
            candidate,
            string.Format(
                Localize(
                    "AnimationWorkbench.TrustedPreview.AnimationUnreadableDetails"),
                candidate.Path,
                candidate.SourcePack,
                technicalDetail));
    }

    private TrustedAnimationPreviewResourceState CreateResourceState(
        PackFile file)
    {
        var owner = packFileService.GetPackFileContainer(file);
        return new TrustedAnimationPreviewResourceState(
            packFileService.GetFullPath(file),
            owner?.Name ?? Localize(
                "AnimationWorkbench.TrustedPreview.SourceUnknown"),
            true,
            string.Empty);
    }

    private void SetFailure(
        TrustedAnimationPreviewResourceState modelState) =>
        SetState(new TrustedAnimationPreviewState(
            modelState,
            TrustedAnimationPreviewResourceState.Empty,
            TrustedAnimationPreviewResourceState.Empty,
            false,
            0));

    private void SetAnimationFailure(
        TrustedAnimationCandidate candidate,
        string diagnostic) =>
        SetState(State with
        {
            Animation = new TrustedAnimationPreviewResourceState(
                candidate.Path,
                candidate.SourcePack,
                false,
                diagnostic),
        });

    private static string CreateCompatibilityDiagnostic(
        TrustedAnimationCandidate candidate,
        AnimationFile animation,
        TrustedAnimationSkeletonIdentity skeleton)
    {
        var actualSkeleton = animation.Header.SkeletonName.Trim();
        if (!string.Equals(
                actualSkeleton,
                skeleton.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Format(
                Localize(
                    "AnimationWorkbench.TrustedPreview.AnimationSkeletonConflict"),
                candidate.Path,
                candidate.SourcePack,
                actualSkeleton,
                skeleton.Name,
                skeleton.Path,
                skeleton.Source);
        }

        var compareCount = Math.Min(
            animation.Bones.Length,
            skeleton.Bones.Count);
        for (var boneIndex = 0; boneIndex < compareCount; boneIndex++)
        {
            var actual = animation.Bones[boneIndex];
            var expected = skeleton.Bones[boneIndex];
            if (string.Equals(
                    actual.Name,
                    expected.Name,
                    StringComparison.Ordinal) &&
                actual.ParentId == expected.ParentIndex)
            {
                continue;
            }

            return string.Format(
                Localize(
                    "AnimationWorkbench.TrustedPreview.AnimationBoneConflict"),
                candidate.Path,
                candidate.SourcePack,
                boneIndex,
                actual.Name,
                actual.ParentId,
                expected.Name,
                expected.ParentIndex,
                skeleton.Path,
                skeleton.Source);
        }

        return string.Format(
            Localize(
                "AnimationWorkbench.TrustedPreview.AnimationBoneCountConflict"),
            candidate.Path,
            candidate.SourcePack,
            animation.Bones.Length,
            skeleton.Bones.Count,
            skeleton.Path,
            skeleton.Source);
    }

    private void SetState(TrustedAnimationPreviewState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
