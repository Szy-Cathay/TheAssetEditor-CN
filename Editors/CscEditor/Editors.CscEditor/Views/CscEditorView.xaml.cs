using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Editors.CscEditor.Services;
using Editors.CscEditor.ViewModels;
using Shared.Core.ToolCreation;

namespace Editors.CscEditor.Views
{
    public enum CscHistoryShortcut
    {
        None,
        Undo,
        Redo,
    }

    public partial class CscEditorView : UserControl
    {
        Point _dragStart;
        CscElementViewModel? _dragCandidate;

        CscEditorViewModel? ViewModel => DataContext as CscEditorViewModel;

        public CscEditorView()
        {
            InitializeComponent();
            CurveEditor.CurvesModified += () => ViewModel?.OnCurvesModified();
            CurveEditor.EditGestureStarted +=
                () => ViewModel?.OnEditGestureStarted();
            CurveEditor.EditGestureCompleted +=
                () => ViewModel?.OnCurveEditGestureCompleted();
            DataContextChanged += (_, _) =>
            {
                if (ViewModel != null)
                    ViewModel.RedrawCurves = CurveEditor.Redraw;
            };
        }

        void ComponentTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ViewModel == null)
                return;

            if (e.NewValue is CscElementViewModel vm)
                ViewModel.SelectedElement = vm;
            else if (e.NewValue is CscSceneRootViewModel root)
                ViewModel.SelectedSceneRoot = root;
        }

        void CurveVisibility_Click(object sender, RoutedEventArgs e) => CurveEditor.Redraw();

        void AuxField_LostFocus(object sender, RoutedEventArgs e) => ViewModel?.OnAuxFieldModified();

        void Save_Click(object sender, RoutedEventArgs e)
        {
            var invalidTextBox = CommitPendingTextEdits(this);
            if (invalidTextBox != null)
            {
                invalidTextBox.Focus();
                return;
            }

            (DataContext as ISaveableEditor)?.Save();
        }

        void Undo_Click(
            object sender,
            RoutedEventArgs e) =>
            ExecuteHistoryAction(redo: false);

        void Redo_Click(
            object sender,
            RoutedEventArgs e) =>
            ExecuteHistoryAction(redo: true);

        void Editor_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            var shortcut = GetHistoryShortcut(
                e.Key,
                Keyboard.Modifiers);
            if (shortcut == CscHistoryShortcut.None)
                return;

            ExecuteHistoryAction(
                shortcut == CscHistoryShortcut.Redo);
            e.Handled = true;
        }

        internal static CscHistoryShortcut GetHistoryShortcut(
            Key key,
            ModifierKeys modifiers)
        {
            if (key == Key.Z &&
                modifiers ==
                (ModifierKeys.Control | ModifierKeys.Shift))
            {
                return CscHistoryShortcut.Redo;
            }

            if (modifiers != ModifierKeys.Control)
                return CscHistoryShortcut.None;

            return key switch
            {
                Key.Z => CscHistoryShortcut.Undo,
                Key.Y => CscHistoryShortcut.Redo,
                _ => CscHistoryShortcut.None,
            };
        }

        void ExecuteHistoryAction(bool redo)
        {
            var invalidTextBox =
                CommitPendingTextEdits(this);
            if (invalidTextBox != null)
            {
                invalidTextBox.Focus();
                return;
            }

            if (DataContext is not ICscUndoRedoEditor editor)
                return;

            if (redo)
                editor.Redo();
            else
                editor.Undo();
        }

        internal static TextBox? CommitPendingTextEdits(
            DependencyObject parent)
        {
            for (var index = 0;
                 index < VisualTreeHelper.GetChildrenCount(parent);
                 index++)
            {
                var child =
                    VisualTreeHelper.GetChild(parent, index);
                if (child is TextBox textBox &&
                    !textBox.IsReadOnly)
                {
                    var binding = textBox.GetBindingExpression(
                        TextBox.TextProperty);
                    if (binding?.IsDirty == true)
                        binding.UpdateSource();

                    if (Validation.GetHasError(textBox))
                        return textBox;
                }

                var match = CommitPendingTextEdits(child);
                if (match != null)
                    return match;
            }

            return null;
        }

        // ---------------------------------------------------------------------
        // Tree drag-drop re-parenting
        // ---------------------------------------------------------------------

        void ComponentTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(ComponentTree);
            _dragCandidate = FindElementVm(e.OriginalSource);
        }

        void ComponentTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var position = e.GetPosition(ComponentTree);
            if (System.Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                System.Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(ComponentTree, new DataObject(typeof(CscElementViewModel), dragged), DragDropEffects.Move);
        }

        void ComponentTree_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(CscElementViewModel)) is not CscElementViewModel dragged)
                return;

            var target = FindElementVm(e.OriginalSource);
            ViewModel?.ReparentElement(dragged, target); // target == null -> detach to top level
            e.Handled = true;
        }

        static CscElementViewModel? FindElementVm(object originalSource)
        {
            var current = originalSource as DependencyObject;
            while (current != null && current is not TreeViewItem)
                current = VisualTreeHelper.GetParent(current);
            return (current as TreeViewItem)?.DataContext as CscElementViewModel;
        }
    }
}
