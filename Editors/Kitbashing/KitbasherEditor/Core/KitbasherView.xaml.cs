using System.Windows;
using System.Windows.Controls;
using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.Common;
using Shared.Ui.Common.MenuSystem;

namespace KitbasherEditor.Views
{
    /// <summary>
    /// Interaction logic for KitbasherView.xaml
    /// </summary>
    public partial class KitbasherView : UserControl
    {
        public KitbasherView()
        {
            InitializeComponent();
        }

        private void CircleSelection_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is KitbasherViewModel viewModel)
                viewModel.MenuBar.FocusScene();
        }

        private void treeView_Drop(object sender, DragEventArgs e)
        {
            var dropTarget = DataContext as IDropTarget<TreeNode>;
            if (dropTarget != null)
            {
                var formats = e.Data.GetFormats();
                object droppedObject = e.Data.GetData(formats[0]);
                var node = droppedObject as TreeNode;

                if (dropTarget.AllowDrop(node))
                {
                    dropTarget.Drop(node);
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                }
            }
        }
    }

    /// <summary>
    /// Template selector for sidebar buttons (Button vs GroupButton vs Separator)
    /// </summary>
    public class SidebarTemplateSelector : DataTemplateSelector
    {
        public DataTemplate DefaultTemplate { get; set; }
        public DataTemplate RadioTemplate { get; set; }
        public DataTemplate? SelectionTemplate { get; set; }
        public DataTemplate SeparatorTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is MenuBarButton button)
            {
                if (button.IsSeperator)
                    return SeparatorTemplate;
                if (button.Action is KitbasherMenuItem<SelectGizmoModeCommand> && SelectionTemplate != null)
                    return SelectionTemplate;
                if (button is MenuBarGroupButton)
                    return RadioTemplate;
                return DefaultTemplate;
            }
            return DefaultTemplate;
        }
    }
}
