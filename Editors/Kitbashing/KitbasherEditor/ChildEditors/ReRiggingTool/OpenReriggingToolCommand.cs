using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.Ui.Common.MenuSystem;
using Shared.Ui.Editors.BoneMapping;

namespace Editors.KitbasherEditor.ChildEditors.ReRiggingTool
{
    public class OpenReriggingToolCommand : IScopedKitbasherUiCommand, IDisposable
    {
        public string ToolTip { get; set; } = "Open the re-rigging tool";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey { get; } = null;

        private readonly IStandardDialogs _standardDialogs;
        private readonly KitbasherRootScene _kitbasherRootScene;
        private readonly SelectionManager _selectionManager;

        private readonly ISkeletonAnimationLookUpHelper _skeletonHelper;
        private readonly IAbstractFormFactory<ReRiggingWindow> _formFactory;

        ReRiggingWindow? _windowHandle;

        public OpenReriggingToolCommand(IStandardDialogs standardDialogs, KitbasherRootScene kitbasherRootScene, SelectionManager selectionManager, ISkeletonAnimationLookUpHelper skeletonHelper, IAbstractFormFactory<ReRiggingWindow> formFactory)
        {
            _standardDialogs = standardDialogs;
            _kitbasherRootScene = kitbasherRootScene;
            _selectionManager = selectionManager;

            _skeletonHelper = skeletonHelper;
            _formFactory = formFactory;

        }
        public void Execute()
        {
            if (_windowHandle != null)
            {
                _windowHandle.BringIntoView();
                return;
            }

            var targetSkeleton = _kitbasherRootScene.Skeleton;
            if (targetSkeleton == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.TargetSkeletonUnavailable"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var targetSkeletonName = targetSkeleton.SkeletonName;
            var state = _selectionManager.GetState<ObjectSelectionState>();
            if (state == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectMesh"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var existingSkeletonFile = _skeletonHelper.GetSkeletonFileFromName(targetSkeletonName);
            if (existingSkeletonFile == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat("Msg.Kitbash.SourceSkeletonNotFound", targetSkeletonName),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var selectedObjects = state.SelectedObjects();
            var selectedMeshes = selectedObjects.OfType<Rmv2MeshNode>().ToList();
            if (selectedMeshes.Count == 0 || selectedMeshes.Count != selectedObjects.Count)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectOnlyMeshes"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            if (selectedMeshes.Count(x => x.Geometry.VertexFormat == UiVertexFormat.Static) != 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.StaticMeshCannotRerig"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var selectedMeshSkeletons = selectedMeshes
                .Select(x => x.Geometry.SkeletonName)
                .Distinct();

            if (selectedMeshSkeletons.Count() != 1)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat("Msg.UnexpectedSkeletonCount", selectedMeshSkeletons.Count()),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var selectedMeshSkeleton = selectedMeshSkeletons.First();
            var newSkeletonFile = _skeletonHelper.GetSkeletonFileFromName(selectedMeshSkeleton);
            if (newSkeletonFile == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat("Msg.Kitbash.SourceSkeletonNotFound", selectedMeshSkeleton),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            if (targetSkeletonName == selectedMeshSkeleton)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SameSkeleton"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            // Ensure all the bones have valid stuff
            var allUsedBoneIndexes = new List<byte>();
            var sourceBonesById = newSkeletonFile.Bones.ToDictionary(x => x.Id);
            foreach (var mesh in selectedMeshes)
            {
                var boneIndexes = mesh.Geometry.GetUniqeBlendIndices();
                var hasValidBoneMapping = boneIndexes.Count != 0 &&
                    boneIndexes.All(x => sourceBonesById.ContainsKey(x));
                if (!hasValidBoneMapping)
                {
                    _standardDialogs.ShowDialogBox(
                        LocalizationManager.Instance.GetFormat("Msg.Kitbash.InvalidBoneMapping", mesh.Name),
                        LocalizationManager.Instance.Get("General.Error"));
                    return;
                }
                allUsedBoneIndexes.AddRange(boneIndexes);
            }

            var animatedBoneIndexes = allUsedBoneIndexes
                .Distinct()
                .Select(x => new AnimatedBone(x, sourceBonesById[x].Name))
                .OrderBy(x => x.BoneIndex.Value).
                ToList();

            var config = new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = selectedMeshSkeleton,
                MeshBones = AnimatedBoneHelper.CreateFromSkeleton(newSkeletonFile, animatedBoneIndexes.Select(x => x.BoneIndex.Value).ToList()),

                ParnetModelSkeletonName = targetSkeletonName,
                ParentModelBones = AnimatedBoneHelper.CreateFromSkeleton(existingSkeletonFile)
            };

            _windowHandle = _formFactory.Create();
            _windowHandle.ViewModel.Initialize(selectedMeshes, config);
            _windowHandle.Closed += OnWindowClosed;
            _windowHandle.Show();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_windowHandle != null)
                _windowHandle.Closed -= OnWindowClosed;

            _windowHandle = null;
        }

        public void Dispose()
        {
            _windowHandle?.Close();
            _windowHandle = null;
        }
    }
}
