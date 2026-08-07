using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    public sealed class CombatMetaDataInstance :
        IMetaDataInstance,
        ICombatMetaDataPreview
    {
        private readonly SceneNode _node;
        private readonly Action<bool> _selectionChanged;
        private readonly MetaDataTimeRange? _activeTimeRange;
        private readonly Func<Vector3> _localFocusPositionProvider;
        private readonly Func<Matrix> _referenceLocalTransform;
        private readonly Func<Matrix> _nodeLocalTransformProvider;
        private readonly Action? _refreshVisual;
        private readonly int? _highlightedBoneIndex;
        private bool _isEnabled = true;
        private bool _isSelected;
        private bool _showForEntireAnimation;
        private float _currentTimeSeconds;

        public ParsedMetadataAttribute Source { get; }
        public CombatMetaDataPreviewCategory Category { get; }
        public Vector3 FocusPosition => WorldTransform.Translation;
        public Matrix ReferenceWorldTransform =>
            _referenceLocalTransform() * GetParentWorldTransform();
        public Matrix WorldTransform =>
            Matrix.CreateTranslation(_localFocusPositionProvider()) *
            ReferenceWorldTransform;
        public int? HighlightedBoneIndex => _highlightedBoneIndex;
        public AnimationPlayer Player { get; }
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
                _isSelected = value;
                _selectionChanged(value);
                ApplyVisibility();
            }
        }
        public bool ShowForEntireAnimation
        {
            get => _showForEntireAnimation;
            set
            {
                _showForEntireAnimation = value;
                ApplyVisibility();
            }
        }

        public CombatMetaDataInstance(
            ParsedMetadataAttribute source,
            CombatMetaDataPreviewCategory category,
            Vector3 focusPosition,
            SceneNode node,
            bool isSelected,
            Action<bool> selectionChanged,
            AnimationPlayer player,
            MetaDataTimeRange? activeTimeRange = null,
            Func<Matrix>? referenceLocalTransform = null,
            int? highlightedBoneIndex = null,
            Func<Matrix>? nodeLocalTransformProvider = null,
            Action? refreshVisual = null)
            : this(
                source,
                category,
                () => focusPosition,
                node,
                isSelected,
                selectionChanged,
                player,
                activeTimeRange,
                referenceLocalTransform,
                highlightedBoneIndex,
                nodeLocalTransformProvider,
                refreshVisual)
        {
        }

        public CombatMetaDataInstance(
            ParsedMetadataAttribute source,
            CombatMetaDataPreviewCategory category,
            Func<Vector3> focusPositionProvider,
            SceneNode node,
            bool isSelected,
            Action<bool> selectionChanged,
            AnimationPlayer player,
            MetaDataTimeRange? activeTimeRange = null,
            Func<Matrix>? referenceLocalTransform = null,
            int? highlightedBoneIndex = null,
            Func<Matrix>? nodeLocalTransformProvider = null,
            Action? refreshVisual = null)
        {
            Source = source;
            Category = category;
            _localFocusPositionProvider = focusPositionProvider;
            _node = node;
            _selectionChanged = selectionChanged;
            Player = player;
            _activeTimeRange = activeTimeRange;
            _referenceLocalTransform = referenceLocalTransform ??
                (() => Matrix.Identity);
            _nodeLocalTransformProvider = nodeLocalTransformProvider ??
                (() => Matrix.CreateTranslation(
                    _localFocusPositionProvider()));
            _refreshVisual = refreshVisual;
            _highlightedBoneIndex = highlightedBoneIndex;
            _isSelected = isSelected;
            _selectionChanged(isSelected);
            ApplyVisibility();
        }

        public void Update(float currentTime)
        {
            _currentTimeSeconds = currentTime;
            _refreshVisual?.Invoke();
            _node.ModelMatrix = _nodeLocalTransformProvider() *
                _referenceLocalTransform();
            ApplyVisibility();
        }

        public void CleanUp()
        {
            if (_node.Parent != null)
                _node.Parent.RemoveObject(_node);
        }

        private void ApplyVisibility()
        {
            _node.IsVisible = _isEnabled &&
                (_showForEntireAnimation ||
                    _activeTimeRange is not MetaDataTimeRange activeTimeRange ||
                    activeTimeRange.Contains(
                        _currentTimeSeconds,
                        GetFrameDurationSeconds()));
        }

        private float GetFrameDurationSeconds()
        {
            var microsecondsPerFrame =
                Player.AnimationClip?.MicrosecondsPerFrame ?? 0;
            return microsecondsPerFrame > 0
                ? microsecondsPerFrame / 1_000_000f
                : 0;
        }

        private Matrix GetParentWorldTransform()
        {
            if (_node.SceneManager == null || _node.Parent == null)
                return Matrix.Identity;

            return _node.SceneManager.GetWorldPosition(_node.Parent);
        }
    }
}
