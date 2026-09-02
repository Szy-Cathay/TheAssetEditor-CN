using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using System.IO;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed class TrustedAnimationPreviewViewport :
    ITrustedAnimationPreviewViewport
{
    private readonly SceneObjectViewModelBuilder _sceneObjectBuilder;
    private readonly SceneObjectEditor _sceneObjectEditor;
    private readonly FocusSelectableObjectService _focusService;
    private readonly ArcBallCamera _camera;
    private readonly GridComponent _grid;
    private SceneObjectViewModel? _previewAsset;
    private bool _showModel = true;
    private bool _showSkeleton = true;
    private float _defaultLargestDimension;
    private bool _disposed;

    public TrustedAnimationPreviewViewport(IEditorHostParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.ComponentInserter.Execute(parameters.CoreComponents);
        GameWorld = parameters.GameWorld;
        _sceneObjectBuilder = parameters.SceneObjectViewModelBuilder;
        _sceneObjectEditor = parameters.SceneObjectEditor;
        _focusService = parameters.FocusSelectableObjectService;
        _camera = parameters.CoreComponents.Components
            .OfType<ArcBallCamera>()
            .Single();
        _grid = parameters.CoreComponents.Components
            .OfType<GridComponent>()
            .Single();
        _grid.SetVisibilityOverride(true);
    }

    public IWpfGame GameWorld { get; }

    public TrustedAnimationPlaybackState PlaybackState
    {
        get
        {
            var player = _previewAsset?.Data.Player;
            if (player?.AnimationClip == null)
                return TrustedAnimationPlaybackState.Empty;
            return new TrustedAnimationPlaybackState(
                true,
                player.IsPlaying,
                player.CurrentFrame,
                player.FrameCount,
                player.CurrentTime.TotalSeconds,
                player.Duration.TotalSeconds,
                player.FramesPerSecond);
        }
    }

    public event EventHandler? PlaybackChanged;

    public TrustedAnimationPreviewViewportResult Load(
        PackFile model,
        PackFile skeleton)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(skeleton);
        Clear();

        try
        {
            _previewAsset ??= _sceneObjectBuilder.CreateAsset(
                "trusted-animation-preview",
                createByDefault: true,
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.TrustedPreview.Viewport"),
                Color.LightSkyBlue,
                null!);
            _previewAsset.Data.Player.OnFrameChanged -= OnFrameChanged;
            _previewAsset.Data.Player.OnFrameChanged += OnFrameChanged;
            var asset = _previewAsset.Data;
            _sceneObjectEditor.SetMesh(
                asset,
                model,
                updateSkeleton: false);
            if (asset.ModelNode == null)
            {
                return FailAndClear(
                    TrustedAnimationPreviewResourceKind.Model,
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.ModelGeometryInvalid"));
            }
        }
        catch (Exception exception)
        {
            return FailAndClear(
                TrustedAnimationPreviewResourceKind.Model,
                exception.Message);
        }

        try
        {
            var asset = _previewAsset.Data;
            _sceneObjectEditor.SetSkeleton(asset, skeleton);
            if (asset.Skeleton == null)
            {
                return FailAndClear(
                    TrustedAnimationPreviewResourceKind.Skeleton,
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.SkeletonUnreadable"));
            }
        }
        catch (Exception exception)
        {
            return FailAndClear(
                TrustedAnimationPreviewResourceKind.Skeleton,
                exception.Message);
        }

        var loadedAsset = _previewAsset.Data;
        loadedAsset.ShowMesh.Value = _showModel;
        loadedAsset.ShowSkeleton.Value = _showSkeleton;
        var meshes = SceneNodeHelper
            .GetChildrenOfType<Rmv2MeshNode>(loadedAsset.ModelNode);
        if (meshes.Count == 0)
        {
            return FailAndClear(
                TrustedAnimationPreviewResourceKind.Model,
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.TrustedPreview.ModelGeometryInvalid"));
        }

        try
        {
            if (!HasCompatibleSkeleton(
                    loadedAsset,
                    meshes,
                    out var diagnostic))
            {
                return FailAndClear(
                    TrustedAnimationPreviewResourceKind.Skeleton,
                    diagnostic);
            }
        }
        catch (Exception exception)
        {
            return FailAndClear(
                TrustedAnimationPreviewResourceKind.Skeleton,
                exception.Message);
        }

        try
        {
            if (meshes.Any(mesh => !HasFinitePose(mesh)))
            {
                return FailAndClear(
                    TrustedAnimationPreviewResourceKind.Model,
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.ModelPoseInvalid"));
            }
        }
        catch (Exception exception)
        {
            return FailAndClear(
                TrustedAnimationPreviewResourceKind.Model,
                exception.Message);
        }

        _defaultLargestDimension = meshes.Max(GetLargestPoseDimension);
        FocusModel();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
        return TrustedAnimationPreviewViewportResult.Success(
            meshes.Count);
    }

    public TrustedAnimationPreviewViewportResult LoadAnimation(
        AnimationFile animation,
        string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(animation);
        var asset = _previewAsset?.Data;
        if (asset?.Skeleton == null || asset.ModelNode == null)
        {
            return TrustedAnimationPreviewViewportResult.Failure(
                TrustedAnimationPreviewResourceKind.Animation,
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.AnimationPicker.ModelRequired"));
        }

        var skeleton = asset.Skeleton;
        try
        {
            var clip = new AnimationClip(animation, skeleton);
            if (!TrustedAnimationCompatibility.HasFiniteClip(
                    clip,
                    skeleton.BoneCount,
                    out var conflict))
            {
                throw new InvalidDataException(conflict);
            }

            _sceneObjectEditor.SetAnimationClip(asset, clip, path);
            if (!ReferenceEquals(asset.Skeleton, skeleton) ||
                !ReferenceEquals(
                    asset.SkeletonSceneNode.Skeleton,
                    skeleton))
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.AnimationSkeletonChanged"));
            }

            asset.Player.IsEnabled = true;
            asset.Player.Pause();
            asset.Player.SeekToTimeSeconds(0);
            if (asset.AnimationClip == null)
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.AnimationPlayerRejected"));
            }

            var meshes = SceneNodeHelper
                .GetChildrenOfType<Rmv2MeshNode>(asset.ModelNode);
            if (meshes.Any(mesh =>
                    !ReferenceEquals(mesh.AnimationPlayer, asset.Player) ||
                    !HasFinitePose(mesh) ||
                    IsAbnormalGrowth(mesh)))
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.AnimationPoseUnsafe"));
            }

            PlaybackChanged?.Invoke(this, EventArgs.Empty);
            return TrustedAnimationPreviewViewportResult.Success(
                meshes.Count);
        }
        catch (Exception exception)
        {
            ClearAnimation();
            return TrustedAnimationPreviewViewportResult.Failure(
                TrustedAnimationPreviewResourceKind.Animation,
                string.Format(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.AnimationViewportFailed"),
                    exception.Message));
        }
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_previewAsset == null)
            return;

        var asset = _previewAsset.Data;
        if (asset.ModelNode != null)
            asset.ParentNode.RemoveObject(asset.ModelNode);
        asset.ModelNode = null!;
        asset.MeshName.Value = string.Empty;
        _sceneObjectEditor.SetSkeleton(asset, null!);
        _defaultLargestDimension = 0;
        asset.TriggerMeshChanged();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetModelVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _showModel = isVisible;
        if (_previewAsset != null)
            _previewAsset.Data.ShowMesh.Value = isVisible;
    }

    public void SetSkeletonVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _showSkeleton = isVisible;
        if (_previewAsset != null)
            _previewAsset.Data.ShowSkeleton.Value = isVisible;
    }

    public void FocusModel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var modelNode = _previewAsset?.Data.ModelNode;
        if (modelNode == null)
            return;

        var meshes = SceneNodeHelper
            .GetChildrenOfType<Rmv2MeshNode>(modelNode)
            .Cast<ISelectable>()
            .ToList();
        _focusService.FocusObjects(meshes);
    }

    public void ShowFront()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _camera.CurrentProjectionType = ProjectionType.Perspective;
        _camera.Yaw = 0;
        _camera.Pitch = 0;
        FocusModel();
    }

    public void ResetCamera()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _camera.CurrentProjectionType = ProjectionType.Perspective;
        _camera.Yaw = 0.8f;
        _camera.Pitch = 0.32f;
        _focusService.ResetCamera();
        FocusModel();
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var player = _previewAsset?.Data.Player;
        if (player?.AnimationClip == null)
            return;
        player.Play();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var player = _previewAsset?.Data.Player;
        if (player?.AnimationClip == null)
            return;
        player.Pause();
        player.Refresh();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(double timeSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(timeSeconds))
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        var asset = _previewAsset?.Data;
        if (asset?.Player.AnimationClip == null)
            return;
        asset.Player.SeekToTimeSeconds((float)timeSeconds);
        var meshes = SceneNodeHelper
            .GetChildrenOfType<Rmv2MeshNode>(asset.ModelNode);
        if (meshes.Any(mesh => !HasFinitePose(mesh) ||
                               IsAbnormalGrowth(mesh)))
        {
            throw new InvalidDataException(
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.TrustedPreview.AnimationSeekUnsafe"));
        }
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_previewAsset != null)
        {
            var asset = _previewAsset.Data;
            asset.Player.OnFrameChanged -= OnFrameChanged;
            asset.Player.MarkedForRemoval = true;
            asset.ParentNode.Parent?.RemoveObject(asset.ParentNode);
            GameWorld.RemoveComponent(asset);
            _previewAsset = null;
        }
        _grid.SetVisibilityOverride(null);
        _disposed = true;
    }

    private void ClearAnimation()
    {
        var asset = _previewAsset?.Data;
        if (asset == null)
            return;
        _sceneObjectEditor.SetAnimationClip(asset, null, string.Empty);
        asset.Player.Stop();
        PlaybackChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFrameChanged(int _) =>
        PlaybackChanged?.Invoke(this, EventArgs.Empty);

    private TrustedAnimationPreviewViewportResult FailAndClear(
        TrustedAnimationPreviewResourceKind resource,
        string technicalDetail)
    {
        Clear();
        var diagnostic = string.Format(
            LocalizationManager.Instance.Get(
                "AnimationWorkbench.TrustedPreview.ViewportLoadFailedDetails"),
            technicalDetail);
        return TrustedAnimationPreviewViewportResult.Failure(
            resource,
            diagnostic);
    }

    private static bool HasCompatibleSkeleton(
        SceneObject asset,
        IReadOnlyList<Rmv2MeshNode> meshes,
        out string diagnostic)
    {
        var skeleton = asset.Skeleton;
        if (skeleton == null || skeleton.BoneCount == 0)
        {
            diagnostic = LocalizationManager.Instance.Get(
                "AnimationWorkbench.TrustedPreview.SkeletonUnreadable");
            return false;
        }

        var modelSkeletonNames = meshes
            .Select(mesh => mesh.Geometry.SkeletonName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modelSkeletonNames.Length != 1 ||
            !string.Equals(
                modelSkeletonNames[0],
                skeleton.SkeletonName,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = string.Format(
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.TrustedPreview.ModelSkeletonMismatch"),
                string.Join(", ", modelSkeletonNames),
                skeleton.SkeletonName);
            return false;
        }

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsFinite(skeleton.GetAnimatedWorldTranform(boneIndex)))
            {
                diagnostic = string.Format(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.SkeletonTransformInvalid"),
                    boneIndex);
                return false;
            }
        }

        foreach (var mesh in meshes)
        {
            var weightCount = mesh.Geometry.WeightCount;
            foreach (var vertex in mesh.Geometry.VertexArray)
            {
                var weights = vertex.GetBoneWeights();
                var indices = vertex.GetBoneIndexs();
                for (var influence = 0; influence < weightCount; influence++)
                {
                    if (weights[influence] <= 0.0001f)
                        continue;
                    if (indices[influence] < 0 ||
                        indices[influence] >= skeleton.BoneCount)
                    {
                        diagnostic = string.Format(
                            LocalizationManager.Instance.Get(
                                "AnimationWorkbench.TrustedPreview.SkinningIndexInvalid"),
                            indices[influence],
                            skeleton.BoneCount);
                        return false;
                    }
                }
            }
        }

        diagnostic = string.Empty;
        return true;
    }

    private static bool HasFinitePose(Rmv2MeshNode mesh)
    {
        var pose = MeshPoseSnapshot.Capture(mesh);
        var bounds = pose.GetConservativeAnimatedBounds();
        var size = bounds.Max - bounds.Min;
        var largestDimension = Math.Max(
            size.X,
            Math.Max(size.Y, size.Z));
        return IsFinite(pose.WorldTransform) &&
            IsFinite(bounds.Min) &&
            IsFinite(bounds.Max) &&
            size.X >= 0 &&
            size.Y >= 0 &&
            size.Z >= 0 &&
            largestDimension > 0 &&
            largestDimension < 1_000_000;
    }

    private bool IsAbnormalGrowth(Rmv2MeshNode mesh)
    {
        if (_defaultLargestDimension <= 0)
            return true;
        var current = GetLargestPoseDimension(mesh);
        return !float.IsFinite(current) ||
            current <= 0 ||
            current > _defaultLargestDimension * 100;
    }

    private static float GetLargestPoseDimension(Rmv2MeshNode mesh)
    {
        var bounds = MeshPoseSnapshot.Capture(mesh)
            .GetConservativeAnimatedBounds();
        var size = bounds.Max - bounds.Min;
        return Math.Max(size.X, Math.Max(size.Y, size.Z));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

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
}
