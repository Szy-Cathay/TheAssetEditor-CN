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
    public partial class PinRiggingAlgorithm : ObservableObject
    {
        private readonly IStandardDialogs _standardDialogs;
        private readonly SelectionManager _selectionManager;
        private readonly CommandFactory _commandFactory;

        [ObservableProperty] List<int> _selectedVertex =[];
        [ObservableProperty] Rmv2MeshNode? _selectedMesh;
        [ObservableProperty] string _description = "";

        public PinRiggingAlgorithm(CommandFactory commandFactory, IStandardDialogs standardDialogs, SelectionManager selectionManager)
        {
            _commandFactory = commandFactory;
            _standardDialogs = standardDialogs;
            _selectionManager = selectionManager;
        }

        public bool Execute(List<Rmv2MeshNode> meshesToAffect)
        {
            if (SelectedMesh == null || SelectedVertex.Count == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.PinSourceRequired"),
                    LocalizationManager.Instance.Get("General.Error"));
                return false;
            }

            if (meshesToAffect.Any(x => x == SelectedMesh))
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SourceAlsoTarget"),
                    LocalizationManager.Instance.Get("General.Error"));
                return false;
            }

            var result = _commandFactory.Create<PinMeshToVertexCommand>()
                .Configure(x => x.Configure(meshesToAffect, SelectedMesh, SelectedVertex.First()))
                .BuildAndExecute();
            if (!result)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.RiggingFailed"),
                    LocalizationManager.Instance.Get("General.Error"));
            }
            return result;
        }

        [RelayCommand] void SetSelection()
        {
            SelectedVertex.Clear();
            SelectedMesh = null;

            var description = LocalizationManager.Instance.Get("Msg.Kitbash.NoMeshSelected");
            var selectionState = _selectionManager.GetState<VertexSelectionState>();
            if (selectionState == null || selectionState.SelectionCount() == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.NoVertexSelected"),
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
            
            SelectedMesh = selectionAsMeshNode;
            SelectedVertex = selectionState.SelectedVertices.ToList();

            description = LocalizationManager.Instance.GetFormat(
                "Msg.Kitbash.PinSourceDescription",
                SelectedMesh.Name,
                SelectedVertex.Count);
            Description = description;
        }
    }
}
