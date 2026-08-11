using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Ui.Editors.BoneMapping;

namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    public partial class BoneManager : ObservableObject
    {
        record SkeletonInfo(string Name, AnimationFile Data);

        private readonly IStandardDialogs _standardDialogs;
        private readonly IAbstractFormFactory<BoneMappingWindow> _boneMappingWindowFactory;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;

        private ISkeletonBoneHighlighter? _skeletonBoneHighlighter;
        private RemappedAnimatedBoneConfiguration? _activeConfig;
        private SkeletonInfo? _sourceSkeleton;
        private SkeletonInfo? _targetSkeleton;

        [ObservableProperty] SkeletonBoneNode_new? _selectedBone;
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _bones = [];
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _flatBoneList = [];
        [ObservableProperty] BoneAutoMappingSummary? _lastAutoMappingSummary;

        partial void OnBonesChanged(ObservableCollection<SkeletonBoneNode_new> value)
        {
            FlatBoneList = FlattenBones(value);
        }

        static ObservableCollection<SkeletonBoneNode_new> FlattenBones(ObservableCollection<SkeletonBoneNode_new> bones)
        {
            var result = new ObservableCollection<SkeletonBoneNode_new>();
            foreach (var bone in bones)
                FlattenBoneRecursive(bone, result);
            return result;
        }

        static void FlattenBoneRecursive(SkeletonBoneNode_new bone, ObservableCollection<SkeletonBoneNode_new> result)
        {
            result.Add(bone);
            foreach (var child in bone.Children)
                FlattenBoneRecursive(child, result);
        }

        public BoneManager(
            IStandardDialogs standardDialogs, 
            IAbstractFormFactory<BoneMappingWindow> boneMappingWindowFactory,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper)
        {
            _standardDialogs = standardDialogs;
            _boneMappingWindowFactory = boneMappingWindowFactory;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
        }

        partial void OnSelectedBoneChanged(SkeletonBoneNode_new? value)
        {
            if (_skeletonBoneHighlighter == null)
                return;
                
            if(value == null)
            {
                _skeletonBoneHighlighter.SelectSourceSkeletonBone(-1);
                _skeletonBoneHighlighter.SelectTargetSkeletonBone(-1);
            }
            else
            {
                _skeletonBoneHighlighter.SelectSourceSkeletonBone(value.BoneIndex);
                if(value.HasMapping)
                    _skeletonBoneHighlighter.SelectTargetSkeletonBone(value.MappedIndex);
                else
                    _skeletonBoneHighlighter.SelectTargetSkeletonBone(-1);
            }
        }

        public void SetSceneNodes(SceneObject source, SceneObject target, SceneObject generated)
        {
            _skeletonBoneHighlighter = new SkeletonBoneHighlighter(source, generated);
        }

        public void UpdateTargetSkeleton(string? skeletonName) 
        {
            if (skeletonName == _targetSkeleton?.Name)
                return;

            if (skeletonName == null)
            {
                _targetSkeleton = null;
            }
            else
            {
                var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
                _targetSkeleton = new SkeletonInfo(skeletonName, skeleton);
            }

            Invalidate();
        }

        public void UpdateSourceSkeleton(string? skeletonName) 
        {
            if (skeletonName == _sourceSkeleton?.Name)
                return;

            if (skeletonName == null)
            {
                _sourceSkeleton = null;
            }
            else
            {
                var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(skeletonName);
                _sourceSkeleton = new SkeletonInfo(skeletonName, skeleton);
            }

            Invalidate();
        }

        void Invalidate()
        {
            Bones.Clear();
            if (_targetSkeleton != null)
                Bones = SkeletonBoneNodeHelper.Build(_targetSkeleton.Data);

            SelectedBone = Bones.FirstOrDefault();
            _activeConfig = null;
            LastAutoMappingSummary = null;
            AutoMapBonesCommand.NotifyCanExecuteChanged();
        }

        public void ApplyDefaultMapping()
        {
            AutoMapBones();
        }

        private bool CanAutoMapBones()
        {
            return _sourceSkeleton != null && _targetSkeleton != null;
        }

        [RelayCommand(CanExecute = nameof(CanAutoMapBones))]
        public void AutoMapBones()
        {
            CreateMappingConfig();
            var existingMappings = FlatBoneList
                .Where(bone => bone.HasMapping)
                .ToDictionary(bone => bone.BoneIndex, bone => bone.MappedIndex);

            var summary = HighConfidenceBoneMapper.CreateSummary(
                _sourceSkeleton!.Data,
                _targetSkeleton!.Data,
                existingMappings);
            ApplyConfirmedMappings(summary, existingMappings);
            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig!);
            ApplySummaryStatus(summary);
            LastAutoMappingSummary = summary;
        }

        private void ApplyConfirmedMappings(
            BoneAutoMappingSummary summary,
            IReadOnlyDictionary<int, int> existingMappings)
        {
            foreach (var item in summary.Items)
            {
                if (item.Status != BoneAutoMappingStatus.Confirmed ||
                    !item.SourceBoneIndex.HasValue ||
                    existingMappings.ContainsKey(item.TargetBoneIndex))
                {
                    continue;
                }

                var mapping = SkeletonBoneNodeHelper.GetNodeFromId(item.TargetBoneIndex, _activeConfig!.MeshBones);
                if (mapping == null)
                    continue;

                mapping.MappedBoneIndex.Value = item.SourceBoneIndex.Value;
                mapping.MappedBoneName.Value = item.SourceBoneName!;
            }
        }

        private void ApplySummaryStatus(BoneAutoMappingSummary summary)
        {
            foreach (var item in summary.Items)
            {
                var bone = SkeletonBoneNodeHelper.GetNodeFromId(item.TargetBoneIndex, Bones);
                if (bone == null)
                    continue;

                bone.AutoMappingStatus = item.Status;
                bone.AutoMappingStatusText = item.Status switch
                {
                    BoneAutoMappingStatus.Confirmed => LocalizationManager.Instance.GetFormat(
                        "AnimReTarget.AutoMapBoneStatus.Confirmed",
                        item.SourceBoneName ?? ""),
                    BoneAutoMappingStatus.ReviewRequired => LocalizationManager.Instance.GetFormat(
                        "AnimReTarget.AutoMapBoneStatus.ReviewRequired",
                        item.Candidates.Count),
                    _ => LocalizationManager.Instance.Get("AnimReTarget.AutoMapSummary.Unmatched")
                };
            }
        }

        [RelayCommand] void ShowBoneMappingWindow()
        {
            if (_targetSkeleton == null || _sourceSkeleton == null)
            {
                _standardDialogs.ShowDialogBox("Source or target skeleton not selected", "Error");
                return;
            }

            CreateMappingConfig();

            // Make this possible to use without being a modal!
            var handle = _boneMappingWindowFactory.Create();
            handle.ViewModel.Initialize(_activeConfig);
            var result = handle.ShowDialog();

            if ((result.HasValue && result.Value == true) == false)
                return;

            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig);
        }

        void CreateMappingConfig()
        {

            if (_activeConfig == null)
            {
                _activeConfig = new RemappedAnimatedBoneConfiguration
                {
                    MeshSkeletonName = _targetSkeleton.Name,
                    MeshBones = AnimatedBoneHelper.CreateFromSkeleton(_targetSkeleton.Data),

                    ParnetModelSkeletonName = _sourceSkeleton.Name,
                    ParentModelBones = AnimatedBoneHelper.CreateFromSkeleton(_sourceSkeleton.Data),

                    SkeletonBoneHighlighter = _skeletonBoneHighlighter
                };
            }
        }

        [RelayCommand] void ResetSelectedBone()
        {
            _standardDialogs.ShowDialogBox("Button pressed");
        }

        [RelayCommand] void ClearRelativeSelectedBone()
        {
            if (SelectedBone != null)
                SelectedBone.SelectedRelativeBone = null;
        }


    }
}
