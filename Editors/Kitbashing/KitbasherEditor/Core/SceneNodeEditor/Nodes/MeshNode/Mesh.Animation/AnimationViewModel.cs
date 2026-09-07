using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Shared.Core.Services;
using Shared.Ui.Common;
using Shared.Ui.Editors.BoneMapping;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2
{
    public partial class AnimationViewModel : ObservableObject, IDisposable
    {
        private readonly KitbasherRootScene _kitbasherRootScene;
        private readonly ISkeletonAnimationLookUpHelper _animLookUp;
        private readonly SceneNodePropertyEditor _propertyEditor;
        Rmv2MeshNode? _meshNode;
        private bool _isFilteringAttachments;

        [ObservableProperty] public partial string SkeletonName { get; set; } = string.Empty;
        [ObservableProperty] public partial List<AnimatedBone> AnimatedBones { get; set; } = [];
        [ObservableProperty] public partial FilterCollection<AnimatedBone> AttachableBones { get; set; } = new(null);
        [ObservableProperty] public partial int AnimationMatrixOverride { get; set; } = -1;

        public AnimationViewModel(
            KitbasherRootScene kitbasherRootScene,
            ISkeletonAnimationLookUpHelper animLookUp,
            SceneNodePropertyEditor propertyEditor)
        {
            _kitbasherRootScene = kitbasherRootScene;
            _animLookUp = animLookUp;
            _propertyEditor = propertyEditor;
        }

        public void Initialize(Rmv2MeshNode meshNode)
        {
            _meshNode = meshNode;
            AnimatedBones = [];

            AnimationMatrixOverride = _meshNode.AnimationMatrixOverride;
            SkeletonName = _meshNode.Geometry.SkeletonName;

            var skeletonFile = _animLookUp.GetSkeletonFileFromName(SkeletonName);
            var bones = _meshNode.Geometry.GetUniqeBlendIndices();

            if (skeletonFile == null)
                SkeletonName += GetText("Kitbash.Sidebar.MissingFromPacks");

            // Make sure the bones are valid, mapping can cause issues!
            if (bones.Count != 0 && skeletonFile != null)
            {
                var activeBonesMin = bones.Min(x => x);
                var activeBonesMax = bones.Max(x => x);
                var skeletonBonesMax = skeletonFile.Bones.Max(x => x.Id);

                var hasValidBoneMapping = activeBonesMin >= 0 && skeletonBonesMax >= activeBonesMax;
                if (!hasValidBoneMapping)
                    MessageBox.Show(LocalizationManager.Instance.Get("Msg.InvalidBonesInMesh"));

                var boneList = AnimatedBoneHelper.CreateFlatSkeletonList(skeletonFile);

                if (hasValidBoneMapping)
                {
                    AnimatedBones = boneList
                        .OrderBy(x => x.BoneIndex.Value)
                        .ToList();
                }
            }

            var existingSkeletonMeshNode = _meshNode.GetParentModel();
            var meshNodes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(existingSkeletonMeshNode);
            var existingSkeltonName = meshNodes.First().Geometry.SkeletonName;

            var existingSkeletonFile = string.Equals(
                    existingSkeltonName,
                    _meshNode.Geometry.SkeletonName,
                    StringComparison.OrdinalIgnoreCase)
                ? skeletonFile
                : _animLookUp.GetSkeletonFileFromName(existingSkeltonName);
            AttachableBones.UpdatePossibleValues(
                existingSkeletonFile == null ? [] : AnimatedBoneHelper.CreateFlatSkeletonList(existingSkeletonFile),
                new AnimatedBone(-1, GetText("Kitbash.Sidebar.None")));

            AttachableBones.SearchFilterExtended = FilterAttachments;
            AttachableBones.SelectedItem = FindAttachment(_meshNode.AttachmentPointName);
            AttachableBones.SelectedItemChanged += ModelBoneList_SelectedItemChanged;
        }

        private void ModelBoneList_SelectedItemChanged(AnimatedBone newValue)
        {
            if (_isFilteringAttachments || newValue == null || _meshNode?.GetParentModel() is not MainEditableNode)
                return;

            var oldState = new AttachmentState(
                _meshNode.AttachmentPointName,
                _meshNode.AttachmentBoneResolver,
                FindAttachment(_meshNode.AttachmentPointName));
            var newState = newValue != null && newValue.BoneIndex.Value != -1
                ? new AttachmentState(
                    newValue.Name.Value,
                    new SkeletonBoneAnimationResolver(_kitbasherRootScene, newValue.BoneIndex.Value),
                    newValue)
                : new AttachmentState(null, null, newValue);

            _propertyEditor.Update(
                oldState,
                newState,
                state =>
                {
                    _meshNode.AttachmentPointName = state.Name;
                    _meshNode.AttachmentBoneResolver = state.Resolver;
                },
                state => AttachableBones.SelectedItem = state.SelectedBone);
        }

        private void FilterAttachments(FilterCollection<AnimatedBone> collection, Regex regex)
        {
            if (_isFilteringAttachments)
                return;

            var showAll = string.Equals(collection.Filter, collection.SelectedItem?.Name.Value, StringComparison.Ordinal);
            var matches = collection.PossibleValues
                .Where(bone => showAll || regex.IsMatch(bone.Name.Value))
                .ToList();
            if (collection.Values.SequenceEqual(matches))
                return;

            // Replacing combo items also updates its text and selection synchronously.
            _isFilteringAttachments = true;
            try
            {
                collection.Values = new ObservableCollection<AnimatedBone>(matches);
            }
            finally
            {
                _isFilteringAttachments = false;
            }
        }

        private AnimatedBone? FindAttachment(string? name) =>
            AttachableBones.PossibleValues.FirstOrDefault(bone =>
                string.IsNullOrEmpty(name) ? bone.BoneIndex.Value == -1 : bone.Name.Value == name);

        partial void OnAnimationMatrixOverrideChanged(int value)
        {
            if (_meshNode == null)
                return;

            _propertyEditor.Update(
                _meshNode.AnimationMatrixOverride,
                value,
                newValue => _meshNode.AnimationMatrixOverride = newValue,
                newValue => AnimationMatrixOverride = newValue);
        }

        public void Dispose()
        {
            AttachableBones.SelectedItemChanged -= ModelBoneList_SelectedItemChanged;
        }

        private static string GetText(string key) =>
            LocalizationManager.Instance?.Get(key) ?? key;

        private sealed record AttachmentState(
            string? Name,
            SkeletonBoneAnimationResolver? Resolver,
            AnimatedBone? SelectedBone);
    }
}
