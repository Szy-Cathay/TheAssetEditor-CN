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
            Loaded += (_, _) =>
            {
                UpdateStatisticsPosition();
                (DataContext as KitbasherViewModel)?.OperationFeedback.Activate();
            };
            Unloaded += (_, _) => (DataContext as KitbasherViewModel)?.OperationFeedback.Deactivate();
            DataContextChanged += (_, e) =>
            {
                (e.OldValue as KitbasherViewModel)?.OperationFeedback.Deactivate();
                if (IsLoaded)
                    (e.NewValue as KitbasherViewModel)?.OperationFeedback.Activate();
            };
        }

        private void SidebarOverlay_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateStatisticsPosition();

        private void UpdateStatisticsPosition()
        {
            if (DataContext is KitbasherViewModel viewModel)
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                viewModel.MenuBar.SetStatisticsPosition(
                    (float)((SidebarOverlay.ActualWidth + 12) * dpi.DpiScaleX),
                    (float)(8 * dpi.DpiScaleY));
            }
        }

        private void SelectionTool_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is KitbasherViewModel viewModel)
                viewModel.MenuBar.FocusScene();
        }

        private void treeView_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not IDropTarget<TreeNode> dropTarget)
                return;

            foreach (var node in GetDraggedNodes(e.Data).Distinct().Where(node => dropTarget.AllowDrop(node)))
            {
                dropTarget.Drop(node);
                e.Handled = true;
            }

            // Import references without moving files in the source tree.
            e.Effects = DragDropEffects.None;
        }

        private void treeView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            if (DataContext is IDropTarget<TreeNode> dropTarget &&
                GetDraggedNodes(e.Data).Any(node => dropTarget.AllowDrop(node)))
            {
                if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
                    e.Effects = DragDropEffects.Copy;
                else if ((e.AllowedEffects & DragDropEffects.Move) != 0)
                    e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private static IEnumerable<TreeNode> GetDraggedNodes(IDataObject data)
        {
            // The file tree sends a selection list, including for a single file.
            if (data.GetData(typeof(List<TreeNode>)) is List<TreeNode> nodes)
                return nodes;
            if (data.GetData(typeof(TreeNode[])) is TreeNode[] nodeArray)
                return nodeArray;
            if (data.GetData(typeof(TreeNode)) is TreeNode node)
                return [node];
            return [];
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
