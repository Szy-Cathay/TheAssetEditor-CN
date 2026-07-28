using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.Views.EditorViews;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.Types;
using Shared.Ui.Common.DataTemplates;
using static CommonControls.FilterDialog.FilterUserControl;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes
{

    public partial class AttachmentPoint : ObservableObject
    {
        [ObservableProperty] public partial int BoneIndex { get; set; }
        [ObservableProperty] public partial string Name { get; set; }
        [ObservableProperty] public partial bool IsIdentiy { get; set; }
    }

    public partial class MainEditableNodeViewModel : ObservableObject, ISceneNodeEditor, IViewProvider<MainEditableNodeView>
    {
        static public OnSeachDelegate FilterByFullPath { get { return (item, expression) => { return expression.Match(item.ToString()).Success; }; } }

        private readonly KitbasherRootScene _kitbasherRootScene;
        private readonly RenderEngineComponent _renderEngineComponent;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly SceneNodePropertyEditor _propertyEditor;

        MainEditableNode? _mainNode;
        private bool _isInitializing;

        [ObservableProperty] public partial ObservableCollection<string> SkeletonNameList { get; set; }
        [ObservableProperty] public partial ObservableCollection<AttachmentPoint> AttachmentPointList { get; set; } = [];
        [ObservableProperty] public partial string? SkeletonName { get; set; }

        public MainEditableNodeViewModel(KitbasherRootScene kitbasherRootScene,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            RenderEngineComponent renderEngineComponent, 
            IUiCommandFactory uiCommandFactory,
            IStandardDialogs standardDialogs,
            SceneNodePropertyEditor propertyEditor)
        {
            _kitbasherRootScene = kitbasherRootScene;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _renderEngineComponent = renderEngineComponent;
            _uiCommandFactory = uiCommandFactory;
            _standardDialogs = standardDialogs;
            _propertyEditor = propertyEditor;
            SkeletonNameList = _skeletonAnimationLookUpHelper.GetAllSkeletonFileNames();
     
        }

        static private ObservableCollection<AttachmentPoint> CreateAttachmentPointList(List<RmvAttachmentPoint> attachmentPoints)
        { 
            var output = new ObservableCollection<AttachmentPoint>();
            foreach (var attachmentPoint in attachmentPoints)
            {
                var name = attachmentPoint.Name;
                var newAttachmentPoint = new AttachmentPoint()
                {
                    BoneIndex = attachmentPoint.BoneIndex,
                    Name = name,
                    IsIdentiy = attachmentPoint.Matrix.IsIdentity()
                };
                output.Add(newAttachmentPoint);
            }

            return output;
        }

        [RelayCommand]
        private void ResetAttachmentPointList()
        {
            if (_mainNode != null)
            {
                var currentState = new AttachmentPointState(CloneAttachmentPoints(_mainNode.AttachmentPoints), false);
                var resetState = new AttachmentPointState([], true);
                _propertyEditor.Update(currentState, resetState, ApplyAttachmentPointState);
            }
        }

        partial void OnSkeletonNameChanged(string? oldValue, string? newValue)
        {
            if (_isInitializing || _mainNode == null)
                return;

            var currentState = new SkeletonState(
                oldValue,
                CloneAttachmentPoints(_mainNode.AttachmentPoints),
                false);
            var newState = new SkeletonState(newValue, [], true);
            _propertyEditor.Update(currentState, newState, ApplySkeletonState);
        }

        public void Initialize(ISceneNode node)
        {
            if (node is MainEditableNode mainEditableNode)
            {
                _mainNode = mainEditableNode;
                _isInitializing = true;
                try
                {
                    if (_mainNode.SkeletonNode != null && _mainNode.SkeletonNode.Skeleton != null)
                        SkeletonName = SkeletonNameList.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).ToLower() == _mainNode.SkeletonNode.Skeleton.SkeletonName.ToLower());
                }
                finally
                {
                    _isInitializing = false;
                }

                AttachmentPointList = CreateAttachmentPointList(_mainNode.AttachmentPoints);
            }
            else
            {
                throw new Exception($"{node} is not of type {nameof(MainEditableNode)}");
            }
        }

        public void CopyTexturesToOutputPack() => _uiCommandFactory.Create<CopyTexturesToPackCommand>().Execute(_mainNode);
        public void Dispose() { }

        private static List<RmvAttachmentPoint> CloneAttachmentPoints(IEnumerable<RmvAttachmentPoint> attachmentPoints)
            => attachmentPoints.Select(attachmentPoint => attachmentPoint.Clone()).ToList();

        private void ApplyAttachmentPointState(AttachmentPointState state)
        {
            if (_mainNode == null)
                return;

            _mainNode.SetAttachmentPoints(CloneAttachmentPoints(state.AttachmentPoints), state.GenerateFromSkeleton);
            AttachmentPointList = CreateAttachmentPointList(_mainNode.AttachmentPoints);
        }

        private void ApplySkeletonState(SkeletonState state)
        {
            if (_mainNode == null)
                return;

            SkeletonName = state.Name;
            var cleanSkeletonName = string.IsNullOrWhiteSpace(state.Name)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(state.Name);
            _kitbasherRootScene.SetSkeletonFromName(cleanSkeletonName);
            _mainNode.SetAttachmentPoints(CloneAttachmentPoints(state.AttachmentPoints), state.GenerateFromSkeleton);
            AttachmentPointList = CreateAttachmentPointList(_mainNode.AttachmentPoints);
        }

        private sealed record AttachmentPointState(
            List<RmvAttachmentPoint> AttachmentPoints,
            bool GenerateFromSkeleton);

        private sealed record SkeletonState(
            string? Name,
            List<RmvAttachmentPoint> AttachmentPoints,
            bool GenerateFromSkeleton);
    }
}
