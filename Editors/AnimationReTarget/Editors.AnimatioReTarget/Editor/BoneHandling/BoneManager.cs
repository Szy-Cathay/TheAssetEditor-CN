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
        private const int MaxReviewCandidates = 5;

        record SkeletonInfo(string Name, AnimationFile Data);

        enum MappingApprovalState
        {
            Unavailable,
            PreviewRequired,
            Previewing,
            ConfirmationRequired,
            Confirmed
        }

        private readonly IStandardDialogs _standardDialogs;
        private readonly IAbstractFormFactory<BoneMappingWindow> _boneMappingWindowFactory;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;

        private ISkeletonBoneHighlighter? _skeletonBoneHighlighter;
        private RemappedAnimatedBoneConfiguration? _activeConfig;
        private SkeletonInfo? _sourceSkeleton;
        private SkeletonInfo? _targetSkeleton;
        private readonly HashSet<int> _intentionallyUnmappedTargetBones = [];
        private Dictionary<int, int>? _lastMappingSnapshot;
        private HashSet<int> _lastIntentionalUnmappedSnapshot = [];
        private MappingApprovalState _mappingApprovalState;
        private long _mappingRevision;

        [ObservableProperty] SkeletonBoneNode_new? _selectedBone;
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _bones = [];
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _flatBoneList = [];
        [ObservableProperty] BoneAutoMappingSummary? _lastAutoMappingSummary;
        [ObservableProperty] ObservableCollection<BoneMappingReviewItem> _reviewItems = [];
        [ObservableProperty] bool _isMappingStructurallyReady;
        [ObservableProperty] string _batchRetargetGateText = "";

        public bool IsPreviewingCurrentMapping => _mappingApprovalState == MappingApprovalState.Previewing;
        public bool HasPreviewedCurrentMapping => _mappingApprovalState is
            MappingApprovalState.ConfirmationRequired or MappingApprovalState.Confirmed;
        public bool IsMappingConfirmed => _mappingApprovalState == MappingApprovalState.Confirmed;
        public bool CanBatchRetarget => IsMappingStructurallyReady && IsMappingConfirmed;

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
            _intentionallyUnmappedTargetBones.Clear();
            _lastMappingSnapshot = null;
            _lastIntentionalUnmappedSnapshot.Clear();
            _mappingRevision++;
            LastAutoMappingSummary = null;
            ReviewItems = [];
            IsMappingStructurallyReady = false;
            SetApprovalState(MappingApprovalState.Unavailable);
            BatchRetargetGateText = "";
            AutoMapBonesCommand.NotifyCanExecuteChanged();
            ConfirmMappingCommand.NotifyCanExecuteChanged();
            ShowManualBoneMappingCommand.NotifyCanExecuteChanged();
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
            RefreshSummaryFromCurrentMappings();
        }

        private void SetSummary(BoneAutoMappingSummary summary)
        {
            ApplySummaryStatus(summary);
            LastAutoMappingSummary = summary;
            ReviewItems = new ObservableCollection<BoneMappingReviewItem>(summary.Items
                .Where(item => item.Status is
                    BoneAutoMappingStatus.ReviewRequired or BoneAutoMappingStatus.Unmatched)
                .Select(CreateReviewItem));
            IsMappingStructurallyReady = summary.CanBatchRetarget;
            BatchRetargetGateText = CreateGateText(summary);
            OnPropertyChanged(nameof(CanBatchRetarget));
            ConfirmCandidateCommand.NotifyCanExecuteChanged();
            ConfirmMappingCommand.NotifyCanExecuteChanged();
            MarkIntentionalUnmappedCommand.NotifyCanExecuteChanged();
            ShowManualBoneMappingCommand.NotifyCanExecuteChanged();
        }

        private static BoneMappingReviewItem CreateReviewItem(BoneAutoMappingItem item)
        {
            var statusText = item.Status == BoneAutoMappingStatus.ReviewRequired
                ? LocalizationManager.Instance.Get("AnimReTarget.BoneReview.Status.ReviewRequired")
                : LocalizationManager.Instance.Get("AnimReTarget.BoneReview.Status.Unmatched");
            var reasonText = item.IssueReason switch
            {
                BoneAutoMappingIssueReason.MultipleCandidates => LocalizationManager.Instance.GetFormat(
                    "AnimReTarget.BoneReview.Reason.MultipleCandidates",
                    item.Candidates.Count),
                BoneAutoMappingIssueReason.ParentConflict => LocalizationManager.Instance.Get(
                    "AnimReTarget.BoneReview.Reason.ParentConflict"),
                _ => LocalizationManager.Instance.Get("AnimReTarget.BoneReview.Reason.NoCandidate")
            };

            return new BoneMappingReviewItem(
                item.TargetBoneIndex,
                item.TargetBoneName,
                item.Status,
                statusText,
                reasonText,
                item.CanMarkIntentionalUnmapped,
                item.Candidates.Take(MaxReviewCandidates).Select(candidate => new BoneMappingReviewCandidate(
                    candidate.TargetBoneIndex,
                    candidate.SourceBoneIndex,
                    candidate.SourceBoneName,
                    LocalizationManager.Instance.GetFormat(
                        "AnimReTarget.BoneReview.ConfirmCandidate",
                        candidate.SourceBoneName)))
                    .ToArray());
        }

        private string CreateGateText(BoneAutoMappingSummary summary)
        {
            if (summary.CoreBlockingCount > 0)
            {
                return LocalizationManager.Instance.GetFormat(
                    "AnimReTarget.BatchGate.BlockedCore",
                    summary.CoreBlockingCount,
                    summary.BlockingCount - summary.CoreBlockingCount);
            }
            if (!summary.CanBatchRetarget)
            {
                return LocalizationManager.Instance.GetFormat(
                    "AnimReTarget.BatchGate.Blocked",
                    summary.BlockingCount);
            }
            if (IsMappingConfirmed)
                return LocalizationManager.Instance.Get("AnimReTarget.BatchGate.Confirmed");
            if (HasPreviewedCurrentMapping)
                return LocalizationManager.Instance.Get("AnimReTarget.BatchGate.ConfirmationRequired");
            if (IsPreviewingCurrentMapping)
                return LocalizationManager.Instance.Get("AnimReTarget.BatchGate.PreviewInProgress");

            return LocalizationManager.Instance.Get("AnimReTarget.BatchGate.PreviewRequired");
        }

        private void RefreshSummaryFromCurrentMappings()
        {
            var existingMappings = FlatBoneList
                .Where(bone => bone.HasMapping)
                .ToDictionary(bone => bone.BoneIndex, bone => bone.MappedIndex);
            var summary = HighConfidenceBoneMapper.CreateSummary(
                _sourceSkeleton!.Data,
                _targetSkeleton!.Data,
                existingMappings,
                _intentionallyUnmappedTargetBones);
            ApplyConfirmedMappings(summary, existingMappings);
            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig!);
            var currentMappings = GetCurrentMappings();
            if (!MappingStateMatchesSnapshot(currentMappings))
            {
                _mappingRevision++;
                SetApprovalState(MappingApprovalState.PreviewRequired);
            }
            _lastMappingSnapshot = currentMappings;
            _lastIntentionalUnmappedSnapshot = new HashSet<int>(_intentionallyUnmappedTargetBones);
            SetSummary(summary);
        }

        public long? BeginMappingPreview()
        {
            if (LastAutoMappingSummary == null)
                return null;

            if (!IsMappingConfirmed)
                SetApprovalState(MappingApprovalState.Previewing);
            return _mappingRevision;
        }

        public void CompleteMappingPreview(long mappingRevision)
        {
            if (LastAutoMappingSummary == null || mappingRevision != _mappingRevision || IsMappingConfirmed)
                return;

            SetApprovalState(MappingApprovalState.ConfirmationRequired);
        }

        public void CancelMappingPreview(long mappingRevision)
        {
            if (mappingRevision != _mappingRevision || !IsPreviewingCurrentMapping)
                return;

            SetApprovalState(MappingApprovalState.PreviewRequired);
        }

        private bool CanConfirmMapping()
        {
            return IsMappingStructurallyReady &&
                   _mappingApprovalState == MappingApprovalState.ConfirmationRequired;
        }

        [RelayCommand(CanExecute = nameof(CanConfirmMapping))]
        public void ConfirmMapping()
        {
            if (!CanConfirmMapping())
                return;

            SetApprovalState(MappingApprovalState.Confirmed);
        }

        private void SetApprovalState(MappingApprovalState state)
        {
            if (_mappingApprovalState == state)
                return;

            _mappingApprovalState = state;
            OnPropertyChanged(nameof(IsPreviewingCurrentMapping));
            OnPropertyChanged(nameof(HasPreviewedCurrentMapping));
            OnPropertyChanged(nameof(IsMappingConfirmed));
            OnPropertyChanged(nameof(CanBatchRetarget));
            if (LastAutoMappingSummary != null)
                BatchRetargetGateText = CreateGateText(LastAutoMappingSummary);
            ConfirmMappingCommand.NotifyCanExecuteChanged();
        }

        private Dictionary<int, int> GetCurrentMappings()
        {
            return FlatBoneList
                .Where(bone => bone.HasMapping)
                .ToDictionary(bone => bone.BoneIndex, bone => bone.MappedIndex);
        }

        private bool MappingStateMatchesSnapshot(IReadOnlyDictionary<int, int> currentMappings)
        {
            return _lastMappingSnapshot != null &&
                   _lastMappingSnapshot.Count == currentMappings.Count &&
                   _lastMappingSnapshot.All(mapping =>
                       currentMappings.TryGetValue(mapping.Key, out var sourceBoneIndex) &&
                       sourceBoneIndex == mapping.Value) &&
                   _lastIntentionalUnmappedSnapshot.SetEquals(_intentionallyUnmappedTargetBones);
        }

        private bool CanConfirmCandidate(BoneMappingReviewCandidate? candidate)
        {
            return candidate != null && ReviewItems.Any(item =>
                item.TargetBoneIndex == candidate.TargetBoneIndex &&
                item.Candidates.Any(current => current.SourceBoneIndex == candidate.SourceBoneIndex));
        }

        [RelayCommand(CanExecute = nameof(CanConfirmCandidate))]
        public void ConfirmCandidate(BoneMappingReviewCandidate? candidate)
        {
            if (candidate != null)
                ApplyManualMapping(candidate.TargetBoneIndex, candidate.SourceBoneIndex);
        }

        public bool ApplyManualMapping(int targetBoneIndex, int sourceBoneIndex)
        {
            if (_targetSkeleton == null || _sourceSkeleton == null)
                return false;

            CreateMappingConfig();
            var targetBone = SkeletonBoneNodeHelper.GetNodeFromId(targetBoneIndex, _activeConfig!.MeshBones);
            var sourceBone = SkeletonBoneNodeHelper.GetNodeFromId(sourceBoneIndex, _activeConfig.ParentModelBones);
            if (targetBone == null || sourceBone == null)
                return false;

            targetBone.MappedBoneIndex.Value = sourceBone.BoneIndex.Value;
            targetBone.MappedBoneName.Value = sourceBone.Name.Value;
            _intentionallyUnmappedTargetBones.Remove(targetBoneIndex);
            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig);
            RefreshSummaryFromCurrentMappings();
            return true;
        }

        private bool CanMarkIntentionalUnmapped(BoneMappingReviewItem? reviewItem)
        {
            return reviewItem?.CanMarkIntentionalUnmapped == true &&
                   ReviewItems.Any(item => item.TargetBoneIndex == reviewItem.TargetBoneIndex);
        }

        [RelayCommand(CanExecute = nameof(CanMarkIntentionalUnmapped))]
        public void MarkIntentionalUnmapped(BoneMappingReviewItem? reviewItem)
        {
            if (reviewItem == null || !CanMarkIntentionalUnmapped(reviewItem))
                return;

            CreateMappingConfig();
            var mapping = SkeletonBoneNodeHelper.GetNodeFromId(reviewItem.TargetBoneIndex, _activeConfig!.MeshBones);
            mapping?.ClearMapping(false);
            _intentionallyUnmappedTargetBones.Add(reviewItem.TargetBoneIndex);
            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig);
            RefreshSummaryFromCurrentMappings();
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
                    BoneAutoMappingStatus.IntentionallyUnmapped => LocalizationManager.Instance.Get(
                        "AnimReTarget.AutoMapBoneStatus.IntentionallyUnmapped"),
                    _ => LocalizationManager.Instance.Get("AnimReTarget.AutoMapSummary.Unmatched")
                };
            }
        }

        [RelayCommand] void ShowBoneMappingWindow()
        {
            ShowBoneMappingWindow(null);
        }

        private bool CanShowManualBoneMapping(BoneMappingReviewItem? reviewItem)
        {
            return reviewItem != null &&
                   _targetSkeleton != null &&
                   _sourceSkeleton != null &&
                   ReviewItems.Any(item => item.TargetBoneIndex == reviewItem.TargetBoneIndex);
        }

        [RelayCommand(CanExecute = nameof(CanShowManualBoneMapping))]
        public void ShowManualBoneMapping(BoneMappingReviewItem? reviewItem)
        {
            ShowBoneMappingWindow(reviewItem);
        }

        private void ShowBoneMappingWindow(BoneMappingReviewItem? reviewItem)
        {
            if (_targetSkeleton == null || _sourceSkeleton == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("AnimReTarget.BoneReview.MissingSkeleton"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            CreateMappingConfig();

            // Make this possible to use without being a modal!
            var editableConfig = CloneMappingConfig(_activeConfig!);
            var handle = _boneMappingWindowFactory.Create();
            handle.ViewModel.Initialize(editableConfig);
            handle.ViewModel.OnlyShowUsedBones.Value = false;
            handle.ViewModel.ParentModelBones.RefreshFilter();
            if (reviewItem != null)
            {
                handle.ViewModel.MeshBones.SelectedItem = SkeletonBoneNodeHelper.GetNodeFromId(
                    reviewItem.TargetBoneIndex,
                    editableConfig.MeshBones);
            }
            var result = handle.ShowDialog();

            if ((result.HasValue && result.Value == true) == false)
                return;

            _activeConfig = editableConfig;
            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig);
            foreach (var mappedBone in FlatBoneList.Where(bone => bone.HasMapping))
                _intentionallyUnmappedTargetBones.Remove(mappedBone.BoneIndex);
            RefreshSummaryFromCurrentMappings();
        }

        private static RemappedAnimatedBoneConfiguration CloneMappingConfig(
            RemappedAnimatedBoneConfiguration source)
        {
            return new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = source.MeshSkeletonName,
                MeshBones = new ObservableCollection<AnimatedBone>(source.MeshBones.Select(CloneBone)),
                ParnetModelSkeletonName = source.ParnetModelSkeletonName,
                ParentModelBones = new ObservableCollection<AnimatedBone>(source.ParentModelBones.Select(CloneBone)),
                SkeletonBoneHighlighter = source.SkeletonBoneHighlighter
            };
        }

        private static AnimatedBone CloneBone(AnimatedBone source)
        {
            return new AnimatedBone
            {
                IsVisible = new NotifyAttr<bool>(source.IsVisible.Value),
                Name = new NotifyAttr<string>(source.Name.Value),
                BoneIndex = new NotifyAttr<int>(source.BoneIndex.Value),
                Children = new ObservableCollection<AnimatedBone>(source.Children.Select(CloneBone)),
                IsUsedByCurrentModel = new NotifyAttr<bool>(source.IsUsedByCurrentModel.Value),
                MappedBoneName = new NotifyAttr<string>(source.MappedBoneName.Value),
                MappedBoneIndex = new NotifyAttr<int>(source.MappedBoneIndex.Value),
                BonePosOffset = source.BonePosOffset,
                BoneRotOffset = source.BoneRotOffset,
                BoneScaleOffset = source.BoneScaleOffset
            };
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
