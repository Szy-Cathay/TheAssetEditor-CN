using System;
using System.Linq;
using AnimationEditor.CampaignAnimationCreator.Commands;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace AnimationEditor.CampaignAnimationCreator
{
    public class CampaignAnimationCreatorViewModel : EditorHostBase
    {
        public override Type EditorViewModelType => typeof(EditorView);
        SceneObject? _selectedUnit;
        AnimationClip? _selectedAnimationClip;

        private readonly IPackFileService _packFileService;
        private readonly ConvertCampaignAnimationCommand _convertCommand;
        private readonly SaveCampaignAnimationCommand _saveCommand;

        public FilterCollection<SkeletonBoneNode> ModelBoneList { get; set; } = new([]);

        public CampaignAnimationCreatorViewModel(
            IEditorHostParameters editorHostParameters,
            IPackFileService packFileService,
            ConvertCampaignAnimationCommand convertCommand,
            SaveCampaignAnimationCommand saveCommand,
            ReferenceObjectSelectionComponent objectSelection,
            ReferenceObjectSelectionOutlineComponent selectionOutline)
            : base(editorHostParameters)
        {
            GameWorld!.AddComponent(objectSelection);
            GameWorld.AddComponent(selectionOutline);
            DisplayName = LocalizationManager.Instance.Get("DisplayName.CampaignAnimationTool");
            _packFileService = packFileService;
            _convertCommand = convertCommand;
            _saveCommand = saveCommand;
            Initialize();
        }

        public void SetDebugInputParameters(AnimationToolInput debugDataToLoad)
        {
            if (_selectedUnit == null)
                return;

            if (debugDataToLoad.Mesh != null)
                SceneObjectEditor.SetMesh(_selectedUnit, debugDataToLoad.Mesh);

            if (debugDataToLoad.Animation != null)
                SceneObjectEditor.SetAnimation(_selectedUnit, _packFileService.GetFullPath(debugDataToLoad.Animation));
        }

        private void Initialize()
        {
            var item = _sceneObjectViewModelBuilder.CreateAsset(
                "model",
                true,
                LocalizationManager.Instance.Get("CampaignAnim.Model"),
                Color.Black,
                new AnimationToolInput());
            Create(item.Data);
            SceneObjects.Add(item);
        }

        void Create(SceneObject rider)
        {
            _selectedUnit = rider;
            _selectedUnit.SkeletonChanged += SkeletonChanged;
            _selectedUnit.AnimationChanged += AnimationChanged;

            SkeletonChanged(_selectedUnit.Skeleton);
            AnimationChanged(_selectedUnit.AnimationClip);
        }

        public void SaveAnimation()
        {
            _saveCommand.Execute(_selectedUnit?.Skeleton, _selectedUnit?.AnimationClip);
        }

        public void Convert()
        {
            if (_convertCommand.Execute(
                    _selectedAnimationClip,
                    ModelBoneList.SelectedItem,
                    out var convertedAnimation) == false ||
                _selectedUnit == null)
            {
                return;
            }

            SceneObjectEditor.SetAnimationClip(
                _selectedUnit,
                convertedAnimation,
                _selectedUnit.AnimationName.Value);
        }

        private void AnimationChanged(AnimationClip? newValue)
        {
            _selectedAnimationClip = newValue;
        }

        private void SkeletonChanged(GameSkeleton? newValue)
        {
            if (newValue == null)
            {
                ModelBoneList.UpdatePossibleValues(null);
            }
            else
            {
                ModelBoneList.UpdatePossibleValues(SkeletonBoneNodeHelper.CreateFlatSkeletonList(newValue));
                ModelBoneList.SelectedItem = ModelBoneList.PossibleValues.FirstOrDefault(
                    x => string.Equals(x.BoneName, "animroot", StringComparison.InvariantCultureIgnoreCase));
            }
        }

        public override void Close()
        {
            if (_selectedUnit != null)
            {
                _selectedUnit.SkeletonChanged -= SkeletonChanged;
                _selectedUnit.AnimationChanged -= AnimationChanged;
            }

            base.Close();
        }
    }
}
