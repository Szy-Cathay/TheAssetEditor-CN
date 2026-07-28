using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.SceneNodes;
using KitbasherEditor.Views.EditorViews;
using Shared.Ui.Common.DataTemplates;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes
{
    public partial class GroupNodeViewModel : ObservableObject, ISceneNodeEditor, IViewProvider<GroupView>
    {
        private readonly SceneNodePropertyEditor _propertyEditor;
        ISceneNode _node;

        [ObservableProperty] string _groupName = string.Empty;

        public GroupNodeViewModel(SceneNodePropertyEditor propertyEditor)
        {
            _propertyEditor = propertyEditor;
        }

        public void Initialize(ISceneNode node)
        {
            _node = node;
            GroupName = _node.Name;
        }

        partial void OnGroupNameChanged(string value) =>
            _propertyEditor.Update(
                _node.Name,
                value,
                newValue => _node.Name = newValue,
                newValue => GroupName = newValue);

        public void Dispose(){}
    }
}
