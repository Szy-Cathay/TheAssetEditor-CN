using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shared.Ui.Common;
using Shared.Ui.Common.Behaviors;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    public partial class PackFileBrowserView : UserControl
    {
        public PackFileBrowserView()
        {
            InitializeComponent();
        }

        Point _lastMouseDown;
        IReadOnlyList<TreeNode> _draggedItems = [];
        TreeNode? _pendingSingleClickNode;
        bool _dragInProgress;

        public System.Windows.Controls.ContextMenu CustomContextMenu
        {
            get { return (System.Windows.Controls.ContextMenu)GetValue(CustomContextMenuProperty); }
            set { SetValue(CustomContextMenuProperty, value); }
        }

        public bool ShowTitle
        {
            get { return (bool)GetValue(ShowTitleProperty); }
            set { SetValue(ShowTitleProperty, value); }
        }

        private void TreeView_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left ||
                sender is not TreeViewItem item ||
                e.OriginalSource is not DependencyObject source ||
                !ReferenceEquals(
                    item,
                    TreeViewExtension.VisualUpwardSearch(source)) ||
                WasExpanderClicked(source) ||
                item.DataContext is not TreeNode node ||
                DataContext is not PackFileBrowserViewModel viewModel)
            {
                return;
            }

            _lastMouseDown = e.GetPosition(tvParameters);
            _pendingSingleClickNode = null;
            var modifiers = Keyboard.Modifiers;
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                viewModel.SelectNode(
                    node,
                    PackFileTreeSelectionMode.Range);
            }
            else if (modifiers.HasFlag(ModifierKeys.Control))
            {
                var wasSelected = node.IsSelected;
                viewModel.SelectNode(
                    node,
                    PackFileTreeSelectionMode.Toggle);
                if (wasSelected)
                {
                    item.Focus();
                    e.Handled = true;
                }
            }
            else if (node.IsSelected)
            {
                viewModel.ActivateNode(node);
                _pendingSingleClickNode = node;
            }
            else
            {
                viewModel.SelectNode(
                    node,
                    PackFileTreeSelectionMode.Replace);
            }

            _draggedItems = node.IsSelected
                ? viewModel.SelectedItems.ToList()
                : [];
        }

        public void TriggerPreviewKeyDown()
        {
            var args = new KeyEventArgs(InputManager.Current.PrimaryKeyboardDevice, PresentationSource.FromVisual(this), 0, Key.F)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && args.Key == Key.F)
            {
                FilterTextBoxItem.Focus();
                FilterTextBoxItem.SelectAll();
                args.Handled = true;
            }
        }

        private void treeView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _draggedItems = [];
                return;
            }

            if (_dragInProgress)
                return;

            var currentPosition = e.GetPosition(tvParameters);
            if (Math.Abs(currentPosition.X - _lastMouseDown.X) <= 10.0 &&
                Math.Abs(currentPosition.Y - _lastMouseDown.Y) <= 10.0)
            {
                return;
            }

            if (_draggedItems.Count == 0)
                return;

            _dragInProgress = true;
            _pendingSingleClickNode = null;
            try
            {
                DragDrop.DoDragDrop(
                    tvParameters,
                    _draggedItems,
                    DragDropEffects.Move);
            }
            finally
            {
                _draggedItems = [];
                _dragInProgress = false;
            }
        }

        private void treeView_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not PackFileBrowserViewModel viewModel ||
                _draggedItems.Count == 0 ||
                sender is not TreeViewItem dropTargetItem ||
                dropTargetItem.DataContext is not TreeNode dropTargetNode)
            {
                return;
            }

            if (viewModel.AllowDrop(_draggedItems, dropTargetNode))
            {
                viewModel.Drop(_draggedItems, dropTargetNode);
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void ClearButtonClick(object sender, RoutedEventArgs e)
        {
            FilterTextBoxItem.Focus();
        }

        private void TreeViewItem_MouseRightButtonDown(
            object sender,
            MouseEventArgs e)
        {
            if (sender is TreeViewItem item &&
                item.DataContext is TreeNode node &&
                DataContext is PackFileBrowserViewModel viewModel)
            {
                viewModel.ActivateNode(node);
                item.Focus();
                e.Handled = true;
            }
        }

        private void TreeView_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (DataContext is not PackFileBrowserViewModel viewModel)
                return;

            if (Keyboard.Modifiers == ModifierKeys.Control &&
                e.Key == Key.C)
            {
                viewModel.CopySelection();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control &&
                     e.Key == Key.V)
            {
                viewModel.PasteSelection();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None &&
                     e.Key == Key.Delete)
            {
                viewModel.DeleteSelection();
                e.Handled = true;
            }
        }

        protected override void OnPreviewMouseLeftButtonUp(
            MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (_pendingSingleClickNode != null &&
                DataContext is PackFileBrowserViewModel viewModel)
            {
                viewModel.SelectNode(
                    _pendingSingleClickNode,
                    PackFileTreeSelectionMode.Replace);
            }
            _pendingSingleClickNode = null;
            _draggedItems = [];
        }

        private static bool WasExpanderClicked(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ToggleButton { Name: "Expander" })
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        public static readonly DependencyProperty CustomContextMenuProperty = DependencyProperty.Register("CustomContextMenu", typeof(System.Windows.Controls.ContextMenu), typeof(PackFileBrowserView), new UIPropertyMetadata(null));
        public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register("ShowTitle", typeof(bool), typeof(PackFileBrowserView), new PropertyMetadata(true));
    }
}
