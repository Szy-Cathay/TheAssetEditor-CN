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
        private readonly CharacterRetargetProfileStore? _profileStore;

        private ISkeletonBoneHighlighter? _skeletonBoneHighlighter;
        private RemappedAnimatedBoneConfiguration? _activeConfig;
        private SkeletonInfo? _sourceSkeleton;
        private SkeletonInfo? _targetSkeleton;

        [ObservableProperty] SkeletonBoneNode_new? _selectedBone;
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _bones = [];
        [ObservableProperty] ObservableCollection<SkeletonBoneNode_new> _flatBoneList = [];
        [ObservableProperty] string _mappingSummary = "-";

        public bool HasValidMapping => FlatBoneList.Any(bone =>
            bone.HasMapping && bone.MappedIndex >= 0);

        public bool TryValidateBoneSettings(out string errorMessage)
        {
            if (FlatBoneList.Any(bone =>
                    !float.IsFinite(bone.BoneLengthMult) ||
                    bone.BoneLengthMult <= 0))
            {
                errorMessage = LocalizationManager.Instance.Get(
                    "AnimReTarget.Error.InvalidBoneLengthMultiplier");
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public bool ConfirmSparseMapping()
        {
            var mappedBoneCount = FlatBoneList.Count(bone =>
                bone.HasMapping && bone.MappedIndex >= 0);
            if (FlatBoneList.Count <= 1 || mappedBoneCount != 1)
                return true;

            return _standardDialogs.ShowYesNoBox(
                LocalizationManager.Instance.Get(
                    "AnimReTarget.Warning.SparseMapping"),
                LocalizationManager.Instance.Get(
                    "AnimReTarget.Warning.SparseMapping.Title")) ==
                ShowMessageBoxResult.OK;
        }

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
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            CharacterRetargetProfileStore? profileStore = null)
        {
            _standardDialogs = standardDialogs;
            _boneMappingWindowFactory = boneMappingWindowFactory;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _profileStore = profileStore;
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
                _skeletonBoneHighlighter.SelectTargetSkeletonBone(value.BoneIndex);
                if(value.HasMapping)
                    _skeletonBoneHighlighter.SelectSourceSkeletonBone(value.MappedIndex);
                else
                    _skeletonBoneHighlighter.SelectSourceSkeletonBone(-1);
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
            {
                Bones = SkeletonBoneNodeHelper.Build(_targetSkeleton.Data);
            }
            SelectedBone = Bones.FirstOrDefault();
            _activeConfig = null;
            MappingSummary = "-";
            RestoreProfile();
        }

        public void ApplyDefaultMapping()
        {
            CreateMappingConfig();
            BoneMappingHelper.AutomapDirectBoneLinksBasedOnNames(_activeConfig.MeshBones.First(), _activeConfig.ParentModelBones);
            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig);
            UpdateMappingSummary();
        }

        [RelayCommand]
        public void ApplyHumanoidMapping()
        {
            if (_targetSkeleton == null || _sourceSkeleton == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("AnimReTarget.Error.SkeletonSelectionRequired"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            var result = HumanoidBoneMapper.CreateMappings(
                _sourceSkeleton.Data,
                _targetSkeleton.Data);
            var humanoidTargetBoneIndices = result.Mappings
                .Select(mapping => mapping.TargetBoneIndex)
                .ToHashSet();
            var mappings = FlatBoneList
                .Where(bone =>
                    bone.HasMapping &&
                    bone.MappedIndex >= 0 &&
                    !HumanoidBoneMapper.IsRigSpecificDeformationBone(bone.BoneName))
                .ToDictionary(bone => bone.BoneIndex, bone => bone.MappedIndex);
            foreach (var mapping in result.Mappings)
                mappings[mapping.TargetBoneIndex] = mapping.SourceBoneIndex;

            foreach (var bone in FlatBoneList.Where(bone =>
                         humanoidTargetBoneIndices.Contains(bone.BoneIndex)))
            {
                ResetBoneSettings(bone);
            }

            CreateMappingConfig();
            ApplyMappings(mappings);
            foreach (var bone in FlatBoneList.Where(bone =>
                         humanoidTargetBoneIndices.Contains(bone.BoneIndex)))
            {
                bone.ApplyTranslation = result.TranslationTargetBoneIndices.Contains(
                    bone.BoneIndex);
            }
        }


        [RelayCommand] void ShowBoneMappingWindow()
        {
            if (_targetSkeleton == null || _sourceSkeleton == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("AnimReTarget.Error.SkeletonSelectionRequired"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
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
            UpdateMappingSummary();
            SaveActiveProfileWithFeedback();
        }

        void RestoreProfile()
        {
            if (_profileStore == null ||
                _sourceSkeleton == null ||
                _targetSkeleton == null)
            {
                return;
            }

            var sourceFingerprint = CharacterRetargetProfileStore
                .ComputeSkeletonFingerprint(_sourceSkeleton.Data);
            var targetFingerprint = CharacterRetargetProfileStore
                .ComputeSkeletonFingerprint(_targetSkeleton.Data);
            if (!_profileStore.TryLoad(
                    _sourceSkeleton.Name,
                    _targetSkeleton.Name,
                    sourceFingerprint,
                    targetFingerprint,
                    out var mappings))
            {
                return;
            }

            CreateMappingConfig();
            ApplyMappings(mappings);
            if (_profileStore.TryLoadSettings(
                    _sourceSkeleton.Name,
                    _targetSkeleton.Name,
                    sourceFingerprint,
                    targetFingerprint,
                    out var settings))
            {
                ApplyBoneSettings(settings);
            }
        }

        void ApplyMappings(IReadOnlyDictionary<int, int> mappings)
        {
            foreach (var targetRoot in _activeConfig!.MeshBones)
                targetRoot.ClearMapping();

            foreach (var mapping in mappings)
            {
                var targetBone = SkeletonBoneNodeHelper.GetNodeFromId(
                    mapping.Key,
                    _activeConfig.MeshBones);
                var sourceBone = SkeletonBoneNodeHelper.GetNodeFromId(
                    mapping.Value,
                    _activeConfig.ParentModelBones);
                if (targetBone == null || sourceBone == null)
                    continue;

                targetBone.MappedBoneIndex.Value = sourceBone.BoneIndex.Value;
                targetBone.MappedBoneName.Value = sourceBone.Name.Value;
            }

            SkeletonBoneNodeHelper.ApplyMapping(Bones, _activeConfig);
            UpdateMappingSummary();
        }

        void UpdateMappingSummary()
        {
            MappingSummary =
                $"{SkeletonBoneNodeHelper.CountMappedBones(Bones)} / {FlatBoneList.Count}";
        }

        [RelayCommand]
        public void SaveCharacterProfile()
        {
            if (_sourceSkeleton == null || _targetSkeleton == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("AnimReTarget.Error.SkeletonSelectionRequired"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            SaveActiveProfileWithFeedback();
        }

        void SaveActiveProfileWithFeedback()
        {
            if (!TryValidateBoneSettings(out var validationError))
            {
                _standardDialogs.ShowDialogBox(
                    validationError,
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            if (SaveActiveProfile())
                return;

            _standardDialogs.ShowDialogBox(
                LocalizationManager.Instance.Get(
                    "AnimReTarget.Profile.SaveFailed"),
                LocalizationManager.Instance.Get("Msg.GeneralError"));
        }

        bool SaveActiveProfile()
        {
            if (_profileStore == null ||
                _sourceSkeleton == null ||
                _targetSkeleton == null)
            {
                return false;
            }

            var mappings = FlatBoneList
                .Where(bone => bone.HasMapping && bone.MappedIndex >= 0)
                .ToDictionary(bone => bone.BoneIndex, bone => bone.MappedIndex);
            var settings = FlatBoneList.ToDictionary(
                bone => bone.BoneIndex,
                bone => new CharacterRetargetBoneSettings(
                    bone.BoneLengthMult,
                    bone.RotationOffset.X.Value,
                    bone.RotationOffset.Y.Value,
                    bone.RotationOffset.Z.Value,
                    bone.TranslationOffset.X.Value,
                    bone.TranslationOffset.Y.Value,
                    bone.TranslationOffset.Z.Value,
                    bone.ForceSnapToWorld,
                    bone.FreezeTranslation,
                    bone.FreezeRotation,
                    bone.FreezeRotationZ,
                    bone.ApplyTranslation,
                    bone.ApplyRotation,
                    bone.SelectedRelativeBone?.BoneIndex));
            return _profileStore.Save(
                _sourceSkeleton.Name,
                _targetSkeleton.Name,
                CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                    _sourceSkeleton.Data),
                CharacterRetargetProfileStore.ComputeSkeletonFingerprint(
                    _targetSkeleton.Data),
                mappings,
                settings);
        }

        void ApplyBoneSettings(
            IReadOnlyDictionary<int, CharacterRetargetBoneSettings> settings)
        {
            foreach (var item in settings)
            {
                var bone = BoneHelper_new.GetBoneFromId(Bones, item.Key);
                if (bone == null)
                    continue;

                var value = item.Value;
                bone.BoneLengthMult = value.BoneLengthMultiplier;
                bone.RotationOffset.X.Value = value.RotationOffsetX;
                bone.RotationOffset.Y.Value = value.RotationOffsetY;
                bone.RotationOffset.Z.Value = value.RotationOffsetZ;
                bone.TranslationOffset.X.Value = value.TranslationOffsetX;
                bone.TranslationOffset.Y.Value = value.TranslationOffsetY;
                bone.TranslationOffset.Z.Value = value.TranslationOffsetZ;
                bone.ForceSnapToWorld = value.ForceSnapToWorld;
                bone.FreezeTranslation = value.FreezeTranslation;
                bone.FreezeRotation = value.FreezeRotation;
                bone.FreezeRotationZ = value.FreezeRotationZ;
                bone.ApplyTranslation = value.ApplyTranslation;
                bone.ApplyRotation = value.ApplyRotation;
                bone.SelectedRelativeBone = value.RelativeTargetBoneIndex.HasValue
                    ? BoneHelper_new.GetBoneFromId(
                        Bones,
                        value.RelativeTargetBoneIndex.Value)
                    : null;
            }
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
            if (SelectedBone == null)
                return;

            ResetBoneSettings(SelectedBone);
        }

        static void ResetBoneSettings(SkeletonBoneNode_new bone)
        {
            bone.IsLocalOffset = false;
            bone.BoneLengthMult = 1;
            bone.RotationOffset.X.Value = 0;
            bone.RotationOffset.Y.Value = 0;
            bone.RotationOffset.Z.Value = 0;
            bone.TranslationOffset.X.Value = 0;
            bone.TranslationOffset.Y.Value = 0;
            bone.TranslationOffset.Z.Value = 0;
            bone.ForceSnapToWorld = false;
            bone.FreezeTranslation = false;
            bone.FreezeRotation = false;
            bone.FreezeRotationZ = false;
            bone.ApplyTranslation = true;
            bone.ApplyRotation = true;
            bone.SelectedRelativeBone = null;
        }

        [RelayCommand] void ClearRelativeSelectedBone()
        {
            if (SelectedBone != null)
                SelectedBone.SelectedRelativeBone = null;
        }


    }
}
