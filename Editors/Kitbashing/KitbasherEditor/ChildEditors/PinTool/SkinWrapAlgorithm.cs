using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.PinTool
{
    public partial class SkinWrapAlgorithm : ObservableObject
    {
        private readonly IStandardDialogs _standardDialogs;
        private readonly SelectionManager _selectionManager;
        private readonly CommandFactory _commandFactory;

        [ObservableProperty] Rmv2MeshNode? _takeAnimationFromMesh;
        [ObservableProperty] string _description = "";

        public SkinWrapAlgorithm(CommandFactory commandFactory, IStandardDialogs standardDialogs, SelectionManager selectionManager)
        {
            _commandFactory = commandFactory;
            _standardDialogs = standardDialogs;
            _selectionManager = selectionManager;
        }

        [RelayCommand] void SetSelection()
        {
            TakeAnimationFromMesh = null;

            var description = LocalizationManager.Instance.Get("Msg.Kitbash.NoMeshSelected");
            var selectionState = _selectionManager.GetState<ObjectSelectionState>();
            if (selectionState == null || selectionState.SelectionCount() == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.NoMeshSelected"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            if(selectionState.SelectionCount() != 1)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectOneSourceMesh"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var selectionAsMeshNode = selectionState.GetSingleSelectedObject() as Rmv2MeshNode;
            if (selectionAsMeshNode == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectionIsNotMesh"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            if (selectionAsMeshNode.PivotPoint != Vector3.Zero)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.PivotUnsupported"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            TakeAnimationFromMesh = selectionAsMeshNode;

            description = TakeAnimationFromMesh.Name;
            Description = description;
        }


        internal bool Excute(List<Rmv2MeshNode> giveAnimationTo)
        {
            if (TakeAnimationFromMesh == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectOneSourceMesh"),
                    LocalizationManager.Instance.Get("General.Error"));
                return false;
            }

            if (giveAnimationTo.Count == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectTargetMeshes"),
                    LocalizationManager.Instance.Get("General.Error"));
                return false;
            }

            var isAlsoInInputList = giveAnimationTo.Contains(TakeAnimationFromMesh);
            if (isAlsoInInputList)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SourceAlsoTarget"),
                    LocalizationManager.Instance.Get("General.Error"));
                return false;
            }

            var result = _commandFactory.Create<SkinWrapRiggingCommand>()
                .Configure(x => x.Configure(giveAnimationTo, TakeAnimationFromMesh))
                .BuildAndExecute();
            if (!result)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.RiggingFailed"),
                    LocalizationManager.Instance.Get("General.Error"));
            }
            return result;
        }
    }
}
