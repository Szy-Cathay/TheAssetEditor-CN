using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.PinTool
{
    public enum RiggingMode
    { 
        Pin,
        SkinWrap
    }

    public partial class PinToolViewModel : ObservableObject, IDisposable
    {
        private readonly SelectionManager _selectionManager;
        private readonly CommandFactory _commandFactory;
        private readonly IStandardDialogs _standardDialogs;

        [ObservableProperty] PinRiggingAlgorithm _pinMode;
        [ObservableProperty] SkinWrapAlgorithm _skinWrapMode;
        [ObservableProperty] RiggingMode _selectedRiggingMode = RiggingMode.Pin;
        [ObservableProperty] ObservableCollection<Rmv2MeshNode> _affectedMeshCollection = [];
        [ObservableProperty] ObservableCollection<Rmv2MeshNode> _sourceMeshCollection = [];

        public PinToolViewModel(SelectionManager selectionManager, CommandFactory commandFactory, IStandardDialogs standardDialogs)
        {
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
            _standardDialogs = standardDialogs;

            _pinMode = new PinRiggingAlgorithm(_commandFactory, _standardDialogs, _selectionManager);
            _skinWrapMode = new SkinWrapAlgorithm(_commandFactory, _standardDialogs, _selectionManager);
            AffectedMeshCollection.CollectionChanged += OnTargetsChanged;
            PinMode.PropertyChanged += OnSourceChanged;
            SkinWrapMode.PropertyChanged += OnSourceChanged;
        }

        public bool CanApply => AffectedMeshCollection.Count > 0 && (SelectedRiggingMode == RiggingMode.Pin
            ? PinMode.SelectedMesh?.Geometry?.WeightCount > 0 && PinMode.SelectedVertex.Count > 0 && !AffectedMeshCollection.Contains(PinMode.SelectedMesh)
            : SkinWrapMode.TakeAnimationFromMesh != null && !AffectedMeshCollection.Contains(SkinWrapMode.TakeAnimationFromMesh));

        public string SetupHint => LocalizationManager.Instance.Get(AffectedMeshCollection.Count == 0
            ? "PinTool.ChooseTargetsHint"
            : CanApply ? "PinTool.ReadyHint"
            : SelectedRiggingMode == RiggingMode.Pin ? "PinTool.ChooseVertexHint" : "PinTool.ChooseSourceHint");

        partial void OnSelectedRiggingModeChanged(RiggingMode value) => RefreshSetup();
        private void OnTargetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshSetup();
        private void OnSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshSetup();
        private void RefreshSetup()
        {
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(SetupHint));
            ClearAffectedMeshCollectionCommand.NotifyCanExecuteChanged();
        }

        public void Dispose()
        {
            AffectedMeshCollection.CollectionChanged -= OnTargetsChanged;
            PinMode.PropertyChanged -= OnSourceChanged;
            SkinWrapMode.PropertyChanged -= OnSourceChanged;
        }

        private bool HasTargets() => AffectedMeshCollection.Count > 0;

        [RelayCommand(CanExecute = nameof(HasTargets))] void ClearAffectedMeshCollection() => AffectedMeshCollection.Clear();
        [RelayCommand] void AddSelectionToAffectMeshCollection() => AddSelectionToList(AffectedMeshCollection);

        void AddSelectionToList(ObservableCollection<Rmv2MeshNode> itemList)
        {
            var selectionState = _selectionManager.GetState<ObjectSelectionState>();
            if (selectionState == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectMesh"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var selectedItems = selectionState.SelectedObjects();
            if (selectedItems.Count == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectMesh"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            var selectedObjects = selectedItems.OfType<Rmv2MeshNode>().ToList();
            if (selectedObjects.Count != selectedItems.Count)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectOnlyMeshes"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            if (selectedObjects.Any(x => x.PivotPoint != Vector3.Zero))
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.PivotUnsupported"),
                    LocalizationManager.Instance.Get("General.Error"));
                return;
            }

            foreach (var item in itemList)
                selectedObjects.Add(item);

            var sortedObjects = selectedObjects
                .Distinct()
                .OrderByDescending(x => x.Name);

            itemList.Clear();
            foreach (var item in sortedObjects)
                itemList.Add(item);
        }

        public bool Apply()
        {
            if (AffectedMeshCollection.Count == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.Kitbash.SelectTargetMeshes"),
                    LocalizationManager.Instance.Get("General.Error"));
                return false;
            }

            switch (SelectedRiggingMode)
            {
                case RiggingMode.Pin:
                    return PinMode.Execute(AffectedMeshCollection.ToList());
              
                case RiggingMode.SkinWrap:
                    return SkinWrapMode.Excute(AffectedMeshCollection.ToList());
                default:
                    throw new NotImplementedException($"unable to find an algorithm for selected mode '{SelectedRiggingMode}'");
            }

        }
    }
}
