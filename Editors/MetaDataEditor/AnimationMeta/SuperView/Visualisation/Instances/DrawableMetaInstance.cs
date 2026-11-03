using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    public class DrawableMetaInstance :
        IMetaDataInstance,
        IMetaDataMarkerPreview
    {
        private readonly ILogger _logger = Logging.Create<MetaDataBuilder>();
        private bool _hasError = false;
        private bool _isCleaned;

        private readonly SceneNode _node;
        private readonly MetaDataTimeRange? _activeTimeRange;
        private readonly Action<bool>? _selectionChanged;
        private bool _isEnabled = true;
        private readonly bool _isHitTestEnabled;
        private bool _isHovered;
        private bool _isSelected;
        private bool _showForEntireAnimation;
        private float _currentTimeSeconds;
        private Func<Vector3>? _positionProvider;
        private Func<Quaternion>? _orientationProvider;
        private int? _highlightedBoneIndex;
        public AnimationPlayer Player => null!;
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
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                RefreshSelectionVisual();
            }
        }
        public bool IsHitTestVisible =>
            !_isCleaned && _isHitTestEnabled && _node.IsVisible;
        public bool IsHovered
        {
            get => _isHovered;
            set
            {
                if (_isHovered == value)
                    return;

                _isHovered = value;
                RefreshSelectionVisual();
            }
        }
        public float HitTestRadius => 0.3f;
        public IReadOnlyList<MetaDataMarkerHitTarget> HitTargets =>
        [
            new(FocusPosition, MetaDataMarkerPoint.Default),
        ];
        public bool ShowForEntireAnimation
        {
            get => _showForEntireAnimation;
            set
            {
                _showForEntireAnimation = value;
                ApplyVisibility();
            }
        }
        public Matrix ReferenceWorldTransform
        {
            get
            {
                var attachment = GetReferenceTransform();
                return attachment * GetParentWorldTransform();
            }
        }
        public Matrix WorldTransform => _displayWorldTransform?.Invoke()
            ?? GetLocalTransform() * ReferenceWorldTransform;
        public Vector3 FocusPosition => WorldTransform.Translation;
        public int? HighlightedBoneIndex => _highlightedBoneIndex;

        private SkeletonBoneAnimationResolver? _animationResolver;
        private Func<Matrix>? _referenceTransform;
        private Func<Matrix?>? _displayWorldTransform;

        public DrawableMetaInstance(
            SceneNode node,
            bool isHitTestEnabled = true)
        {
            _node = node;
            _isHitTestEnabled = isHitTestEnabled;
        }

        public DrawableMetaInstance(float startTime, float endTime, SceneNode node)
            : this(node)
        {
            _activeTimeRange = new MetaDataTimeRange(startTime, endTime);
        }

        public DrawableMetaInstance(
            float startTime,
            float endTime,
            SceneNode node,
            ParsedMetadataAttribute source,
            bool isSelected,
            Action<bool> selectionChanged,
            Func<Vector3> positionProvider,
            Func<Quaternion> orientationProvider)
            : this(
                new MetaDataTimeRange(startTime, endTime),
                node,
                source,
                isSelected,
                selectionChanged,
                positionProvider,
                orientationProvider)
        {
        }

        public DrawableMetaInstance(
            MetaDataTimeRange? activeTimeRange,
            SceneNode node,
            ParsedMetadataAttribute source,
            bool isSelected,
            Action<bool> selectionChanged,
            Func<Vector3> positionProvider,
            Func<Quaternion> orientationProvider,
            bool isHitTestEnabled = true)
            : this(node, isHitTestEnabled)
        {
            _activeTimeRange = activeTimeRange;
            Source = source;
            _selectionChanged = selectionChanged;
            _positionProvider = positionProvider;
            _orientationProvider = orientationProvider;
            _isSelected = isSelected;
            _selectionChanged(isSelected);
        }

        public void FollowBone(ISkeletonProvider skeleton, int boneIndex)
        {
            if (boneIndex != -1)
            {
                _animationResolver = new SkeletonBoneAnimationResolver(skeleton, boneIndex);
                _highlightedBoneIndex = boneIndex;
            }
        }

        public void FollowTransform(Func<Matrix> referenceTransform, int? highlightedBoneIndex)
        {
            _referenceTransform = referenceTransform;
            _highlightedBoneIndex = highlightedBoneIndex;
        }

        public void UseDisplayWorldTransform(Func<Matrix?> displayWorldTransform)
        {
            _displayWorldTransform = displayWorldTransform;
            if (_node is SimpleDrawableNode drawable)
                drawable.DisplayWorldTransform = displayWorldTransform;
        }

        private Matrix GetReferenceTransform() => _referenceTransform?.Invoke()
            ?? _animationResolver?.GetWorldTransform() ?? Matrix.Identity;

        public void Update(float currentTime)
        {
            if (_hasError)
                return;

            try
            {
                _currentTimeSeconds = currentTime;
                ApplyVisibility();
                _node.ModelMatrix = GetLocalTransform() *
                    GetReferenceTransform();
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(DrawableMetaInstance)} - {e.Message}");
                _hasError = true;
            }
        }

        private void ApplyVisibility()
        {
            _node.IsVisible = _isEnabled &&
                (_showForEntireAnimation ||
                    _activeTimeRange is not MetaDataTimeRange activeTimeRange ||
                    activeTimeRange.Contains(_currentTimeSeconds));
        }

        public void CleanUp()
        {
            _isCleaned = true;
            _isHovered = false;
            if (_node.Parent != null)
                _node.Parent.RemoveObject(_node);
        }

        private void RefreshSelectionVisual() =>
            _selectionChanged?.Invoke(_isSelected || _isHovered);

        private Matrix GetLocalTransform()
        {
            if (_positionProvider == null || _orientationProvider == null)
                return Matrix.Identity;

            return Matrix.CreateFromQuaternion(_orientationProvider()) *
                Matrix.CreateTranslation(_positionProvider());
        }

        private Matrix GetParentWorldTransform()
        {
            if (_node.SceneManager == null || _node.Parent == null)
                return Matrix.Identity;

            return _node.SceneManager.GetWorldPosition(_node.Parent);
        }
    }
}
