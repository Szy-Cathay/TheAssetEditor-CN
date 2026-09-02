using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed record AnimationWorkbenchPreviewModelState(
    string ModelSkeletonName,
    string TargetSkeletonName,
    bool HasSkeletonMismatch,
    int MeshCount = 0);

public interface IAnimationWorkbenchViewport :
    IAnimationWorkbenchPreviewHost
{
    IWpfGame GameWorld { get; }

    AnimationPlayerViewModel Player { get; }

    AnimationWorkbenchPreviewModelState? CurrentModel { get; }

    AnimationWorkbenchPreviewModelState? LoadModel(PackFile file);

    void ClearModel();

    void SetModelVisible(bool isVisible);

    void SetSkeletonVisible(bool isVisible);
}

public sealed class AnimationWorkbenchViewport :
    IAnimationWorkbenchViewport
{
    private readonly SceneObjectViewModelBuilder _sceneObjectBuilder;
    private readonly SceneObjectEditor _sceneObjectEditor;
    private SceneObjectViewModel? _previewAsset;
    private PreviewSession? _activeSession;
    private bool _showModel = true;
    private bool _showSkeleton = true;
    private bool _disposed;

    public AnimationWorkbenchViewport(IEditorHostParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.ComponentInserter.Execute(parameters.CoreComponents);
        GameWorld = parameters.GameWorld;
        Player = parameters.AnimationPlayerViewModel;
        _sceneObjectBuilder = parameters.SceneObjectViewModelBuilder;
        _sceneObjectEditor = parameters.SceneObjectEditor;
    }

    public IWpfGame GameWorld { get; }

    public AnimationPlayerViewModel Player { get; }

    public AnimationWorkbenchPreviewModelState? CurrentModel { get; private set; }

    public IAnimationWorkbenchPreviewSession Show(
        AnimationWorkbenchPreviewSnapshot preview,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(preview);

        _activeSession?.Dispose();
        _previewAsset ??= _sceneObjectBuilder.CreateAsset(
            "animation-workbench-preview",
            createByDefault: true,
            LocalizationManager.Instance.Get(
                "AnimationWorkbench.Shell.PreviewScene"),
            Color.LightSkyBlue,
            null!);

        var asset = _previewAsset.Data;
        var skeleton = preview.Skeleton.Clone(asset.Player);
        asset.Skeleton = skeleton;
        asset.SkeletonName.Value = skeleton.SkeletonName;
        asset.SkeletonSceneNode.Skeleton = skeleton;
        asset.TriggerSkeletonChanged();
        _sceneObjectEditor.SetAnimationClip(
            asset,
            preview.Animation.Clone(),
            preview.Name);
        asset.ShowMesh.Value = _showModel;
        asset.ShowSkeleton.Value = _showSkeleton;
        CurrentModel = asset.ModelNode == null
            ? null
            : CreateModelState(asset);
        Player.IsEnabled.Value = true;

        var session = new PreviewSession(
            this,
            asset,
            cancellationToken);
        _activeSession = session;
        return session;
    }

    public AnimationWorkbenchPreviewModelState? LoadModel(PackFile file)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(file);
        if (_previewAsset == null)
            return null;

        var asset = _previewAsset.Data;
        var previousModel = asset.ModelNode;
        _sceneObjectEditor.SetMesh(asset, file, updateSkeleton: false);
        if (asset.ModelNode == null ||
            ReferenceEquals(asset.ModelNode, previousModel))
        {
            return null;
        }

        asset.ShowMesh.Value = _showModel;
        asset.ShowSkeleton.Value = _showSkeleton;
        CurrentModel = CreateModelState(asset);
        return CurrentModel;
    }

    public void ClearModel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_previewAsset?.Data.ModelNode == null)
            return;

        var asset = _previewAsset.Data;
        asset.ParentNode.RemoveObject(asset.ModelNode);
        asset.ModelNode = null!;
        asset.MeshName.Value = string.Empty;
        asset.TriggerMeshChanged();
        CurrentModel = null;
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activeSession?.Dispose();
        _activeSession = null;
        if (_previewAsset == null)
            return;

        var asset = _previewAsset.Data;
        asset.Player.MarkedForRemoval = true;
        asset.ParentNode.Parent?.RemoveObject(asset.ParentNode);
        GameWorld.RemoveComponent(asset);
        _previewAsset = null;
        CurrentModel = null;
    }

    private static AnimationWorkbenchPreviewModelState CreateModelState(
        SceneObject asset)
    {
        var meshes = SceneNodeHelper
            .GetChildrenOfType<Rmv2MeshNode>(asset.ModelNode);
        var modelSkeletons = meshes
            .Select(mesh => mesh.Geometry.SkeletonName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targetSkeleton = asset.Skeleton?.SkeletonName ?? string.Empty;
        var hasMismatch = modelSkeletons.Length != 0 &&
            !modelSkeletons.Contains(
                targetSkeleton,
                StringComparer.OrdinalIgnoreCase);
        return new AnimationWorkbenchPreviewModelState(
            string.Join(", ", modelSkeletons),
            targetSkeleton,
            hasMismatch,
            meshes.Count);
    }

    private void EndSession(PreviewSession session, SceneObject asset)
    {
        if (!ReferenceEquals(_activeSession, session))
            return;

        asset.Player.Pause();
        _sceneObjectEditor.SetAnimationClip(asset, null, "");
        _activeSession = null;
    }

    private sealed class PreviewSession :
        IAnimationWorkbenchPreviewSession
    {
        private readonly AnimationWorkbenchViewport _owner;
        private readonly SceneObject _asset;
        private readonly CancellationTokenRegistration _cancellation;
        private bool _disposed;

        public PreviewSession(
            AnimationWorkbenchViewport owner,
            SceneObject asset,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _asset = asset;
            _cancellation = cancellationToken.Register(Dispose);
        }

        public void Seek(TimeSpan position)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _asset.Player.SeekToTimeSeconds(
                (float)position.TotalSeconds);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Dispose();
            _owner.EndSession(this, _asset);
        }
    }
}
