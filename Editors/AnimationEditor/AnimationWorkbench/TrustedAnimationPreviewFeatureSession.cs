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

    TrustedAnimationPreviewViewportResult Load(
        PackFile model,
        PackFile skeleton);

    void Clear();

    void SetModelVisible(bool isVisible);

    void SetSkeletonVisible(bool isVisible);

    void FocusModel();

    void ShowFront();

    void ResetCamera();
}

public sealed class TrustedAnimationPreviewFeatureSession(
    ITrustedAnimationPreviewViewport viewport,
    IPackFileService packFileService)
{
    public TrustedAnimationPreviewState State { get; private set; } =
        TrustedAnimationPreviewState.Empty;

    public event EventHandler? StateChanged;

    public void LoadModel(PackFile model)
    {
        ArgumentNullException.ThrowIfNull(model);
        viewport.Clear();

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
        var skeleton = packFileService.FindFile(skeletonPath);
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
                throw new InvalidDataException("Skeleton contains no bones.");
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

    private void SetState(TrustedAnimationPreviewState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
