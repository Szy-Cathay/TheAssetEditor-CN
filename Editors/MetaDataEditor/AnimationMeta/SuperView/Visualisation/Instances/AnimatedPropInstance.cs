using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    public class AnimatedPropInstance : IMetaDataInstance, ISpatialMetaDataPreview
    {
        private readonly SceneNode _node;
        private readonly MetaDataTimeRange _activeTimeRange;
        private readonly ISkeletonProvider? _skeleton;
        private readonly int _boneId = -1;
        private readonly Func<Vector3> _positionProvider = () => Vector3.Zero;
        private readonly Func<Quaternion> _orientationProvider =
            () => Quaternion.Identity;
        private readonly Action<bool>? _selectionChanged;
        private bool _isEnabled = true;
        private bool _isSelected;
        private bool _showForEntireAnimation;
        private float _currentTimeSeconds;

        public AnimationPlayer Player { get; private set; }
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
                var attachment = _skeleton != null &&
                    _boneId >= 0 &&
                    _boneId < _skeleton.Skeleton.BoneCount
                        ? _skeleton.Skeleton.GetAnimatedWorldTranform(_boneId)
                        : Matrix.Identity;
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

        public AnimatedPropInstance(SceneNode node, AnimationPlayer player, float startTime, float endTime)
        {
            _node = node;
            Player = player;
            _activeTimeRange = new MetaDataTimeRange(
                startTime,
                endTime,
                MetaDataZeroRangeBehavior.WholeAnimation);
        }

        public AnimatedPropInstance(
            SceneNode node,
            AnimationPlayer player,
            float startTime,
            float endTime,
            ParsedMetadataAttribute source,
            bool isSelected,
            Action<bool> selectionChanged)
            : this(node, player, startTime, endTime)
        {
            Source = source;
            _selectionChanged = selectionChanged;
            _isSelected = isSelected;
            _selectionChanged(isSelected);
        }

        public AnimatedPropInstance(
            SceneNode node,
            AnimationPlayer player,
            ISkeletonProvider skeleton,
            int boneId,
            Func<Vector3> positionProvider,
            Func<Quaternion> orientationProvider,
            float startTime,
            float endTime,
            ParsedMetadataAttribute source,
            bool isSelected,
            Action<bool> selectionChanged)
            : this(node, player, startTime, endTime)
        {
            _skeleton = skeleton;
            _boneId = boneId;
            _positionProvider = positionProvider;
            _orientationProvider = orientationProvider;
            Source = source;
            _selectionChanged = selectionChanged;
            _isSelected = isSelected;
            _selectionChanged(isSelected);
        }

        public void Update(float currentTime)
        {
            _currentTimeSeconds = currentTime;
            if (Player.IsEnabled && Player.IsPlaying == false)
                Player.SeekToTimeSeconds(GetPlayerTime(currentTime));
            ApplyVisibility();
        }

        private float GetPlayerTime(float mainAnimationTime)
        {
            var animationDuration = Player.Duration;
            if (!Player.LoopAnimation || animationDuration <= TimeSpan.Zero)
                return mainAnimationTime;

            return mainAnimationTime % (float)animationDuration.TotalSeconds;
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
            _node.Parent.RemoveObject(_node);
            Player.MarkedForRemoval = true;
        }
    }
}
