using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    public class PropInstance : IMetaDataInstance, ISpatialMetaDataPreview
    {
        private readonly SceneNode _node;
        private readonly ISkeletonProvider? _skeleton;
        private readonly int _boneId;
        private readonly Func<Vector3> _positionProvider;
        private readonly Func<Quaternion> _orientationProvider;
        private readonly MetaDataTimeRange _activeTimeRange;
        private readonly ILogger _logger = Logging.Create<PropInstance>();
        private readonly Action<bool>? _selectionChanged;
        private bool _canFollowBone = true;
        private bool _isEnabled = true;
        private bool _isSelected;
        private bool _showForEntireAnimation;
        private float _currentTimeSeconds;

        public AnimationPlayer Player { get; }
        public ParsedMetadataAttribute Source { get; private set; } = null!;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                ApplyVisibility();
            }
        }
        public Matrix ReferenceWorldTransform
        {
            get
            {
                var attachment = Matrix.Identity;
                if (_skeleton != null &&
                    _boneId >= 0 &&
                    _boneId < _skeleton.Skeleton.BoneCount &&
                    _canFollowBone)
                {
                    attachment = _skeleton.Skeleton
                        .GetAnimatedWorldTranform(_boneId);
                }

                return attachment * GetParentWorldTransform();
            }
        }
        public Matrix WorldTransform =>
            Matrix.CreateFromQuaternion(_orientationProvider()) *
            Matrix.CreateTranslation(_positionProvider()) *
            ReferenceWorldTransform;
        public Vector3 FocusPosition => WorldTransform.Translation;
        public int? HighlightedBoneIndex => _boneId >= 0 ? _boneId : null;
        public bool ShowForEntireAnimation
        {
            get => _showForEntireAnimation;
            set
            {
                _showForEntireAnimation = value;
                ApplyVisibility();
            }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                _selectionChanged?.Invoke(value);
            }
        }

        public PropInstance(
            SceneNode node,
            AnimationPlayer player,
            ISkeletonProvider? skeleton,
            int boneId,
            Vector3 position,
            Quaternion orientation,
            float startTime,
            float endTime)
        {
            _node = node;
            Player = player;
            _skeleton = skeleton;
            _boneId = boneId;
            _positionProvider = () => position;
            _orientationProvider = () => orientation;
            _activeTimeRange = new MetaDataTimeRange(
                startTime,
                endTime,
                MetaDataZeroRangeBehavior.WholeAnimation);
        }

        public PropInstance(
            SceneNode node,
            AnimationPlayer player,
            ISkeletonProvider? skeleton,
            int boneId,
            Func<Vector3> positionProvider,
            Func<Quaternion> orientationProvider,
            float startTime,
            float endTime,
            ParsedMetadataAttribute source,
            bool isSelected,
            Action<bool> selectionChanged)
        {
            _node = node;
            Player = player;
            _skeleton = skeleton;
            _boneId = boneId;
            _positionProvider = positionProvider;
            _orientationProvider = orientationProvider;
            _activeTimeRange = new MetaDataTimeRange(
                startTime,
                endTime,
                MetaDataZeroRangeBehavior.WholeAnimation);
            Source = source;
            _selectionChanged = selectionChanged;
            _isSelected = isSelected;
            _selectionChanged(isSelected);
        }

        public PropInstance(
            SceneNode node,
            AnimationPlayer player,
            ISkeletonProvider? skeleton,
            int boneId,
            Vector3 position,
            Quaternion orientation,
            float startTime,
            float endTime,
            ParsedMetadataAttribute source,
            bool isSelected,
            Action<bool> selectionChanged)
            : this(
                node,
                player,
                skeleton,
                boneId,
                position,
                orientation,
                startTime,
                endTime)
        {
            Source = source;
            _selectionChanged = selectionChanged;
            _isSelected = isSelected;
            _selectionChanged(isSelected);
        }

        public void Update(float currentTime)
        {
            _currentTimeSeconds = currentTime;
            ApplyVisibility();

            var transform = Matrix.CreateFromQuaternion(
                    _orientationProvider()) *
                Matrix.CreateTranslation(_positionProvider());
            if (_skeleton != null && _boneId != -1 && _canFollowBone)
            {
                try
                {
                    transform *= _skeleton.Skeleton
                        .GetAnimatedWorldTranform(_boneId);
                }
                catch (Exception e)
                {
                    _canFollowBone = false;
                    _logger.Here().Warning(
                        $"Unable to attach prop preview to bone {_boneId}: {e.Message}");
                }
            }

            _node.ModelMatrix = transform;
        }

        private void ApplyVisibility() =>
            _node.IsVisible = _isEnabled &&
                (_showForEntireAnimation ||
                    _activeTimeRange.Contains(_currentTimeSeconds));

        private Matrix GetParentWorldTransform()
        {
            if (_node.SceneManager == null || _node.Parent == null)
                return Matrix.Identity;

            return _node.SceneManager.GetWorldPosition(_node.Parent);
        }

        public void CleanUp()
        {
            if (_node.Parent != null)
                _node.Parent.RemoveObject(_node);
            Player.MarkedForRemoval = true;
        }
    }
}
