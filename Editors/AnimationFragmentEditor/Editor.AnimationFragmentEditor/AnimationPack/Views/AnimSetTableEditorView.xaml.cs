using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;

namespace CommonControls.Editors.AnimationPack
{
    public partial class AnimSetTableEditorView : UserControl
    {
        private DispatcherTimer? _filterTimer;
        private ComboBox? _filterTarget;
        private bool _skipNextFilter; // Skip filter after ComboBox selection

        public AnimSetTableEditorView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (MainDataGrid != null)
            {
                MainDataGrid.SelectionChanged -= DataGrid_SelectionChanged;
                MainDataGrid.SelectionChanged += DataGrid_SelectionChanged;
            }
            RefreshCommandStates();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is AnimSetTableEditorViewModel vm)
                vm.MultiSelectedRows = MainDataGrid.SelectedItems;
        }

        // Save snapshot before cell editing starts, so Ctrl+Z can undo cell changes
        private void MainDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (DataContext is AnimSetTableEditorViewModel vm)
                vm.SaveSnapshot();
        }

        private void MainDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (DataContext is AnimSetTableEditorViewModel vm)
                        vm.DiscardLastSnapshotIfUnchanged();
                },
                DispatcherPriority.Background);
        }

        private void Editor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            RefreshCommandStates();

        private void MainDataGrid_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            RefreshCommandStates();

        private void MainDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e) =>
            RefreshCommandStates();

        private void RefreshCommandStates()
        {
            if (DataContext is AnimSetTableEditorViewModel vm)
                vm.RefreshCommandStates();
        }

        // === Shift+MouseWheel horizontal scrolling ===

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift) return;
            var scrollViewer = FindVisualChild<ScrollViewer>(MainDataGrid);
            if (scrollViewer == null) return;
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        // === File ComboBox filtering ===

        private void FileComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            cb.ApplyTemplate();
            var textBox = cb.Template.FindName("PART_EditableTextBox", cb) as TextBox;
            if (textBox == null) return;

            // Track subscriptions for cleanup
            TextCompositionEventHandler? previewInputHandler = null;
            TextChangedEventHandler? textChangeHandler = null;
            SelectionChangedEventHandler? selectionHandler = null;

            // Mark selection so we skip the TextChanged filter after it
            selectionHandler = (s, _) => _skipNextFilter = true;
            cb.SelectionChanged += selectionHandler;

            // Auto-open dropdown only on user typing (NOT on selection)
            previewInputHandler = (s, args) =>
            {
                if (!cb.IsDropDownOpen)
                    cb.IsDropDownOpen = true;
            };
            textBox.PreviewTextInput += previewInputHandler;

            // Filter on text change (skip after selection to prevent reopen)
            textChangeHandler = (s, _) =>
            {
                if (_skipNextFilter)
                {
                    _skipNextFilter = false;
                    return;
                }

                // Debounced filter (150ms)
                _filterTarget = cb;
                _filterTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _filterTimer.Stop();
                _filterTimer.Tick -= FilterTimer_Tick;
                _filterTimer.Tick += FilterTimer_Tick;
                _filterTimer.Start();
            };
            textBox.TextChanged += textChangeHandler;

            // Cleanup on Unloaded to prevent memory leaks from DataGrid virtualization
            cb.Unloaded += (_, _) =>
            {
                cb.SelectionChanged -= selectionHandler;
                textBox.PreviewTextInput -= previewInputHandler;
                textBox.TextChanged -= textChangeHandler;
            };
        }

        private void FileComboBox_DropDownOpened(object sender, EventArgs e)
        {
            // Populate initial results when dropdown opens
            if (sender is ComboBox cb)
                FilterFileComboBox(cb);
        }

        private void FilterTimer_Tick(object? sender, EventArgs e)
        {
            _filterTimer?.Stop();
            if (_filterTarget != null)
                FilterFileComboBox(_filterTarget);
        }

        private void FilterFileComboBox(ComboBox cb)
        {
            if (DataContext is not AnimSetTableEditorViewModel vm) return;
            var keyword = cb.Text;
            switch (cb.Tag as string)
            {
                case "Anim": vm.UpdateAnimFileFilter(keyword); break;
                case "Meta": vm.UpdateMetaFileFilter(keyword); break;
                case "Sound": vm.UpdateSoundFileFilter(keyword); break;
            }
        }
    }
}
