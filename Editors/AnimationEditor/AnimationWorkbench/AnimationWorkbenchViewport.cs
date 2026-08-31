using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public interface IAnimationWorkbenchViewport :
    IAnimationWorkbenchPreviewHost
{
    IWpfGame GameWorld { get; }

    AnimationPlayerViewModel Player { get; }
}

public sealed class AnimationWorkbenchViewport :
    IAnimationWorkbenchViewport
{
    private readonly SceneObjectViewModelBuilder _sceneObjectBuilder;
    private readonly SceneObjectEditor _sceneObjectEditor;
    private SceneObjectViewModel? _previewAsset;
    private PreviewSession? _activeSession;
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
        asset.ShowSkeleton.Value = true;
        Player.IsEnabled.Value = true;

        var session = new PreviewSession(
            this,
            asset,
            cancellationToken);
        _activeSession = session;
        return session;
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
